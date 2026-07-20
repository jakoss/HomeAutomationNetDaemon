FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY global.json HomeAutomationNetDaemon.slnx ./
COPY src/HomeAutomationNetDaemon/HomeAutomationNetDaemon.csproj src/HomeAutomationNetDaemon/
COPY tests/HomeAutomationNetDaemon.Tests/HomeAutomationNetDaemon.Tests.csproj tests/HomeAutomationNetDaemon.Tests/
RUN dotnet restore HomeAutomationNetDaemon.slnx

# Copy everything else and build
COPY . ./
RUN dotnet publish src/HomeAutomationNetDaemon/HomeAutomationNetDaemon.csproj --configuration Release --no-restore --output out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build-env /app/out .
ENTRYPOINT ["dotnet", "HomeAutomationNetDaemon.dll"]
