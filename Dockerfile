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

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends git openssh-client \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

ENTRYPOINT ["dotnet", "MindBot.Bot.dll"]
