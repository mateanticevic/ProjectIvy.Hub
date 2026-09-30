# ProjectIvy.Hub

ASP.NET Core SignalR host (.NET 10) for live GPS tracking. Clients send positions to `/TrackingHub`; the hub stores them in SQL Server and broadcasts them. A background service resolves city, country, and location from geohash prefix tables. `/JobHub` reprocesses a date range and reports progress.

There is no test project. Verify changes by building the solution and, when hub methods change, exercising `ProjectIvy.Hub.Demo.Client`.

## Layout

- `ProjectIvy.Hub/` — web host (`Program` + `Startup`)
  - `Hubs/` — SignalR hubs
  - `Services/TrackingProcessingService.cs` — background queue and geohash resolution
  - `Models/`, `Constants/`, `Enrichers/`
- `ProjectIvy.Hub.Demo.Client/` — console client that calls both hubs
- `Dockerfile`, `Jenkinsfile`, `azure-pipelines.yml` — image build and push
- `KEYCLOAK_AUTH_SETUP.md` — JWT cookie and bearer setup

## Run

```bash
dotnet build ProjectIvy.Hub.sln
dotnet run --project ProjectIvy.Hub
```

Required environment:

- `CONNECTION_STRING_MAIN` — SQL Server connection used by both hubs and the processing service
- `GRAYLOG_HOST` and `GRAYLOG_PORT` — Serilog UDP sink; the process expects both at startup

Keycloak comes from `appsettings.json` or `Keycloak__*` variables. Copy `.env.example`. Do not commit real secrets, connection strings, or tokens.

Demo client:

```bash
HUB_BASE_URL=http://localhost:5000 ACCESS_TOKEN=<jwt> dotnet run --project ProjectIvy.Hub.Demo.Client
```

`TrackingHub.Send` requires a token. `JobHub.ProcessDay` does not.

## Hubs and processing

- `/TrackingHub`
  - `OnConnectedAsync` sends the latest `Tracking.Tracking` row for `UserId = 1` as `Receive`.
  - `Send` is `[Authorize]`. It maps `preferred_username` to `[User].[User].Id` (memory cache, 1 hour), inserts the row with a precision-9 geohash, enqueues processing, then broadcasts `Receive` to all clients.
- `/JobHub`
  - `ProcessDay(from, to, reprocess)` pages unprocessed rows (`Processed IS NULL`, or all rows when `reprocess` is true) and enqueues them. Progress is sent only to the caller as `ProcessDayProgress` (0–100).
- Event names live in `Constants/TrackingEvents` and `Constants/JobEvents`. Add new names there; do not hardcode strings at call sites.
- `TrackingProcessingService` is a singleton hosted service. It loads `Tracking.LocationGeohash` at startup and drains a queue every second. City and country lookup uses prefixes of length 2–8 against `Common.CityGeohash` and `Common.CountryGeohash`. Longest matching prefix wins. Resolved ids are written back with `Processed = UtcNow`.

Hub method signatures are the public API. Keep `ProjectIvy.Hub.Demo.Client` in sync when they change.

## Conventions

- Target `net10.0`. File-scoped namespaces under `ProjectIvy.Hub.*`.
- Data access is Dapper plus `Microsoft.Data.SqlClient`. Open a new connection per operation from `CONNECTION_STRING_MAIN` and dispose it with `using`.
- SQL is inline. Keep parameter names aligned with the columns they write. Schema touched today: `Tracking.Tracking`, `Tracking.Location`, `Tracking.LocationGeohash`, `[User].[User]`, `Common.CityGeohash`, `Common.CountryGeohash`.
- Auth is Keycloak JWT (`Keycloak.AuthServices`). Token order is the `AccessToken` cookie, then `Authorization: Bearer`. See `KEYCLOAK_AUTH_SETUP.md` before changing `Startup`.
- Log with Serilog. Never log a raw token or cookie. Mask tokens to the last 6 characters, as `Startup` request logging already does.
- CORS policy `CorsPolicy` allows any origin with credentials. SignalR needs `AllowCredentials`.
- Docker image is `mateanticevic/project-ivy-hub`, based on `mcr.microsoft.com/dotnet/aspnet:10.0`. Jenkins tags from GitVersion and pushes only when `CommitsSinceVersionSource` is 0.

## Changes

- Persist tracking before broadcast. Saving runs off the hub thread so the SignalR context is not used after the method returns; resolve user id and other `Context` data first.
- Geohash on insert stays length 9. Shorter prefixes are only for lookup caches.
- Do not add a new configuration system. Environment variables and the existing `Keycloak` section are the config surface.
