FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MindBot.slnx ./
COPY src/MindBot.Bot/MindBot.Bot.csproj src/MindBot.Bot/
COPY src/MindBot.Core/MindBot.Core.csproj src/MindBot.Core/
COPY src/MindBot.Infrastructure/MindBot.Infrastructure.csproj src/MindBot.Infrastructure/
COPY tests/MindBot.Tests/MindBot.Tests.csproj tests/MindBot.Tests/
RUN dotnet restore src/MindBot.Bot/MindBot.Bot.csproj

COPY src/ src/
RUN dotnet publish src/MindBot.Bot/MindBot.Bot.csproj -c Release -o /app --no-restore

# The web client, exported as static files for the bot to serve on WEB__PORT.
# Node 24.21.0 matches client CI; pinned by exact version per CLAUDE.md.
FROM node:24.21.0-bookworm-slim AS client
WORKDIR /client
ENV EXPO_NO_TELEMETRY=1 CI=1

COPY client/package.json client/package-lock.json ./
RUN npm ci --no-fund --no-audit

COPY client/ ./
# The bot serves this page under a CSP that forbids inline script, so an export containing one
# would be a blank page in production. Fail the image build instead.
RUN npx expo export --platform web --output-dir dist \
    && if grep -Eq '<script>|<script [^>]*>[^<]' dist/index.html; then \
         echo "dist/index.html contains an inline <script>, which the bot's CSP blocks." >&2; exit 1; \
       fi

# aspnet rather than runtime: the bot serves HTTP (health, and the web chat when enabled).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# curl is here for the compose healthcheck, which probes the endpoint from inside the container.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git openssh-client curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./
COPY --from=client /client/dist ./wwwroot

# Only used when WEB__ENABLED=true; harmless otherwise.
ENV WEB__STATICROOT=/app/wwwroot

# 8080 serves /health and stays inside the container. 8081 is the web chat (WEB__ENABLED=true),
# which docker-compose.yml publishes to the host's loopback only, for `tailscale serve`.
EXPOSE 8080 8081

ENTRYPOINT ["dotnet", "MindBot.Bot.dll"]
