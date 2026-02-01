# Stage 1: Restore & publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# copy everything first to leverage cached restores
COPY . ./

RUN dotnet restore Halina.sln
RUN dotnet publish experiments/Halina.Experiments/Halina.Experiments.csproj -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: runtime
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Halina.Experiments.dll"]
