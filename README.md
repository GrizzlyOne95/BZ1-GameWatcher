# BZ1 Game Watcher

Live lobby list for **Battlezone 98 Redux**, running at https://bz1-gamewatcher.onrender.com/games

The site shows the games currently open, who is in them, and lets you jump straight into a lobby
through Steam.

<img width="3733" height="1919" alt="image" src="https://github.com/user-attachments/assets/fd3db1cd-2f7f-474f-92a4-eeeade5bb9ee" />

<img width="3769" height="1924" alt="image" src="https://github.com/user-attachments/assets/8761a887-d63b-48a5-82f1-befbb6ed905d" />


## How it works

```text
Rebellion lobby server ──WebSocket──▶ API (ASP.NET Core 10) ──REST──▶ Web (Angular 18)
```

- **API** (`API/`) maintains the lobby connection, keeps an in-memory snapshot, enriches Steam
  players with avatars, and serves `GET /api/BZ98Lobby`.
- **Web** (`Web/`) polls the API every few seconds and renders the game list.
- **Render image** (`Dockerfile.render`) builds both projects and serves Angular from the ASP.NET
  application's `wwwroot`, keeping the UI and API on one origin.
- **Optional lobby bot** can join or recreate a configured chat lobby, greet players, and send timed
  announcements.
- The original split-container nginx deployment remains available through Docker Compose.

Because the watcher is a continuously running background service, a static-only host cannot run the
complete application.

## Requirements

For local development:

- [.NET SDK 10.0](https://dotnet.microsoft.com/download)
- [Node.js 22](https://nodejs.org/) LTS

For container deployment:

- Docker Engine
- Docker Compose v2 for the split self-hosted stack

## Configuration

The API reads settings from `API/appsettings.json`, environment variables, and .NET user secrets.

| Setting | Environment variable | Description |
| --- | --- | --- |
| `Steam:ApiKey` | `Steam__ApiKey` | Steam Web API key. Without it, the site still works but players show without avatars. |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0` | Origins allowed to call the API directly. Not needed when the UI and API are same-origin. |
| `Battlezone:LobbyServerUrl` | `Battlezone__LobbyServerUrl` | WebSocket endpoint of the lobby server. |
| `Battlezone:FlaggedSteamIds` | `Battlezone__FlaggedSteamIds__0` | Steam IDs marked with `isDangerous` in API responses. Empty by default. |
| `LobbyBot:Enabled` | `LobbyBot__Enabled` | Enables the optional chat-lobby bot. Disabled by default. |
| `LobbyBot:PlayerName` | `LobbyBot__PlayerName` | Bot identity shown in the lobby. |
| `LobbyBot:LobbyName` | `LobbyBot__LobbyName` | Named chat lobby the bot should join or claim. |

Additional greeting, announcement, cooldown, member-limit, and auto-claim settings are documented in
[`RENDER_DEPLOYMENT.md`](RENDER_DEPLOYMENT.md).

Keep secrets out of source control. For local API development:

```bash
cd API
dotnet user-secrets set "Steam:ApiKey" "<your key>"
```

## Running locally

API — `http://localhost:5283`, with Swagger UI at the root:

```bash
cd API
dotnet run
```

Web — `http://localhost:4200`:

```bash
cd Web
npm ci
npm start
```

The development web server calls `/api/` on its own origin, so use its proxy configuration or run
the full stack under Docker Compose.

## Tests

```bash
cd Web
npm run test:ci
npm test
```

Build the API:

```bash
cd API
dotnet build
```

Build and run the combined Render image locally:

```bash
docker build -f Dockerfile.render -t bz1-gamewatcher:render .
docker run --rm -p 10000:10000 -e PORT=10000 bz1-gamewatcher:render
```

Then open `http://localhost:10000` and `http://localhost:10000/api/health`.

## Deploy on Render

The recommended free-hosting configuration is defined by:

- `render.yaml` — one free Docker web service in Render's Ohio region
- `Dockerfile.render` — Angular and ASP.NET Core combined into one image
- `/api/health` — process, lobby-snapshot, and non-secret bot status

See [`RENDER_DEPLOYMENT.md`](RENDER_DEPLOYMENT.md) for the exact dashboard, secret, bot, custom-domain,
and DNS steps.

A free Render service can spin down after 15 minutes without inbound traffic. The next visitor wakes
it, which can take about one minute. The optional lobby bot is also unavailable while the free service
is asleep; an always-on bot requires upgrading the same service later.

## Self-host with Docker Compose

```bash
docker compose up --build
```

This starts nginx, the API, and certificate-renewal tooling. The API remains internal to the Docker
network and nginx proxies `/api/`.

## Continuous integration

Pushing to `main` builds and tests both projects, then publishes images to GHCR tagged `latest` and
with the commit SHA:

- `ghcr.io/battlezonescrapfield/battlezone-api-ghcr`
- `ghcr.io/battlezonescrapfield/battlezone-web-ghcr`

The owner segment is derived from the repository owner at build time, so forks publish to their own
namespace. Pull requests build and test without publishing.

Thanks to JJ173 for initial creation of this project. 
