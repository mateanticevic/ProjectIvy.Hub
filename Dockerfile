FROM mcr.microsoft.com/dotnet/aspnet:7.0-bullseye-slim AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["ProjectIvy.Hub/ProjectIvy.Hub.csproj", "ProjectIvy.Hub/"]
RUN dotnet restore "ProjectIvy.Hub/ProjectIvy.Hub.csproj"
COPY . .
WORKDIR "/src/ProjectIvy.Hub"
RUN dotnet build "ProjectIvy.Hub.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "ProjectIvy.Hub.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "ProjectIvy.Hub.dll"]