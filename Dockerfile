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

# aspnet rather than runtime: the bot now serves an HTTP health endpoint.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# curl is here for the compose healthcheck, which probes the endpoint from inside the container.
RUN apt-get update \
    && apt-get install -y --no-install-recommends git openssh-client curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# Bound to the container only; docker-compose.yml deliberately does not publish this port.
EXPOSE 8080

ENTRYPOINT ["dotnet", "MindBot.Bot.dll"]
