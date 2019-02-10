FROM microsoft/dotnet:2.2-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 80

FROM microsoft/dotnet:2.2-sdk AS build
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