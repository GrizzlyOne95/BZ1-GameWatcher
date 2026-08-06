# Deploy BZ1 Game Watcher on Render

The repository includes a Render Blueprint (`render.yaml`) and a combined production image
(`Dockerfile.render`). The image builds Angular and ASP.NET Core, copies the Angular bundle into the
API's `wwwroot`, and runs the complete site as one Render web service.

This single-service layout keeps the browser and API on the same origin, makes the UI and watcher wake
together, and lets Render manage the public URL, reverse proxy, HTTPS certificates, logs, and deploy
rollbacks.

## Free-tier behavior

A free Render web service spins down after 15 minutes without inbound HTTP traffic or inbound
WebSocket messages. The Game Watcher's connection to the Rebellion lobby is initiated by this service,
so it should not be treated as inbound traffic that guarantees the service remains awake.

Expected behavior:

- while somebody has the site open, its three-second API polling keeps the service active
- after the site has been unused for 15 minutes, Render can stop the service
- the next visitor wakes it; startup can take about one minute
- the lobby watcher reconnects and rebuilds its in-memory snapshot after startup
- the optional lobby bot also stops while the free service is asleep

The free service is therefore suitable for the public game list and for testing the bot. A continuously
available lobby bot requires an always-on instance later; the same deployment can be upgraded without
rewriting the application.

## What is automated

The repository provides:

- one Docker image containing Angular and ASP.NET Core
- binding to Render's runtime `PORT`
- same-origin `/api/` routing and Angular route fallback
- `/api/health` deployment and runtime checks
- a free Ohio-region web-service Blueprint
- deploys after linked-branch checks pass
- a dashboard prompt for the Steam Web API key
- an opt-in BZ98 chat-lobby bot

## Create the Render service

1. Sign in at <https://dashboard.render.com/>.
2. Connect the GitHub account that can access `GrizzlyOne95/BZ1-GameWatcher`.
3. Select **New + → Blueprint**.
4. Connect this repository.
5. Select `agent/free-hosting-deployment` while testing the pull request, or `main` after it is merged.
6. Confirm that Render detects `render.yaml` in the repository root.
7. Provide `Steam__ApiKey` when prompted. It is optional; without it, player avatars are unavailable.
8. Apply the Blueprint.

Render creates one free Docker web service named `bz1-gamewatcher` in Ohio. The first build can take
several minutes because it compiles both projects.

## Validate the first deployment

Open the service's **Logs** or **Events** page. A successful deployment should show:

- the Angular production build completing
- `dotnet publish` completing
- ASP.NET listening on Render's assigned port
- the lobby watcher connecting or reconnecting
- `/api/health` passing

Render assigns an address similar to:

```text
https://bz1-gamewatcher.onrender.com
```

Test both:

```text
https://YOUR-SERVICE.onrender.com/
https://YOUR-SERVICE.onrender.com/api/health
```

The health response includes the lobby count, snapshot timestamp, and non-secret lobby-bot status.
`lobbyCount` can legitimately be zero, especially immediately after a cold start.

Do not move the public domain until the `onrender.com` URL works.

## Optional lobby bot

The first server-side port from `Battlezone_LobbyMonitor` supports:

- a configurable bot identity
- joining an existing named chat lobby
- optionally recreating that lobby when it is missing
- welcome messages with `{player}` substitution and per-user cooldown
- periodic announcements
- reconnect recovery
- status in `/api/health`

The bot is disabled by default. In the Render service's **Environment** page, add the variables you
want and redeploy:

| Environment variable | Example | Purpose |
| --- | --- | --- |
| `LobbyBot__Enabled` | `true` | Enables all bot behavior. |
| `LobbyBot__PlayerName` | `BZ Community Bot` | Name shown for the web client. |
| `LobbyBot__LobbyName` | `Battlezone Community` | Chat lobby to join or claim, without the internal prefix. |
| `LobbyBot__AutoClaim` | `true` | Creates the configured chat lobby when it does not exist. |
| `LobbyBot__MemberLimit` | `20000` | Member limit used when creating the lobby. |
| `LobbyBot__WelcomeMessage` | `Welcome, {player}!` | Message sent when somebody joins. Leave blank to disable. |
| `LobbyBot__WelcomeCooldown` | `00:01:00` | Minimum delay before welcoming the same user again. |
| `LobbyBot__AnnouncementMessage` | `Join our Discord: ...` | Repeating message. Leave blank to disable. |
| `LobbyBot__AnnouncementInterval` | `00:10:00` | Delay between announcements. |

Start conservatively: enable identity, lobby name, and auto-claim first; confirm the bot joins the
expected lobby; then add greeting and announcement messages. This avoids accidental spam while the
protocol integration is being exercised on the live lobby server.

The public health response does not reveal the Steam key or any other secret. It reports whether the
bot is enabled/configured, its current and target lobby IDs, and its latest action.

Not ported in this first pass:

- player IP/geolocation display
- automatic kick/ban or griefer enforcement
- two-way Discord chat relay
- persistent activity history and CSV logging
- local Discord Rich Presence, desktop sounds, tray behavior, proxy/Tor tooling, or BZCC RakNet tools

Those features either do not belong on a public website, need a moderation/security design, require
persistent storage, or are desktop-specific. Outbound Discord status/webhook support is a reasonable
next addition after the bot is proven stable.

## Move bz98gamewatcher.com to Render

Render manages HTTPS automatically. Do not run Certbot or upload certificates.

### Add the domain

From the web service:

1. Open **Settings**.
2. Find **Custom Domains**.
3. Choose **Add Custom Domain**.
4. Enter `bz98gamewatcher.com`.

Adding the root domain also adds `www.bz98gamewatcher.com` and redirects `www` to the root.

### Update DNS

Use the exact values displayed by Render. For a DNS provider without ALIAS, ANAME, or root-CNAME
flattening, the typical records are:

| Type | Host | Value |
| --- | --- | --- |
| A | `@` | `216.24.57.1` |
| CNAME | `www` | the service's `*.onrender.com` hostname |

Before adding them:

- remove old root A records that point to the previous server
- remove old CNAME or forwarding records for `www`
- remove conflicting AAAA records because Render custom domains currently use IPv4

If the domain uses Cloudflare, follow Render's Cloudflare-specific instructions and use DNS-only mode
until verification succeeds.

Return to Render's **Custom Domains** section and choose **Verify**. After DNS propagation, Render
issues and renews the TLS certificate automatically and redirects HTTP to HTTPS.

Test:

```text
https://bz98gamewatcher.com/
https://bz98gamewatcher.com/api/health
```

Keep the `onrender.com` hostname available until the custom domain is verified and tested.

## Routine operation

- **Deploy updates:** commits deploy automatically after checks pass. Use **Manual Deploy → Deploy latest commit** when needed.
- **View logs:** open the service's **Logs** page. Free services do not provide SSH or dashboard shell access.
- **Roll back:** the **Deploys** page can roll back to either of the two most recent previous deploys on the free plan.
- **Change secrets/config:** edit the service's **Environment** values and redeploy.

## Troubleshooting

### Build fails during `npm ci`

Confirm that `Web/package.json` and `Web/package-lock.json` are synchronized. The image deliberately
uses `npm ci` so lockfile drift fails visibly.

### Build fails while copying Angular output

The expected output is:

```text
Web/dist/bz1-game-watcher/browser/
```

If Angular's output path changes, update the matching `COPY` line in `Dockerfile.render`.

### Render says no open port was detected

The image starts ASP.NET on:

```text
http://0.0.0.0:$PORT
```

Confirm the service is using `Dockerfile.render` and that no custom Docker command overrides its
entrypoint.

### The site shows a loading page after being idle

That is the normal free-tier cold start. Wait for the service and lobby watcher to reconnect.

### The page loads but lobby requests fail

Check `/api/health` and `/api/BZ98Lobby`, then inspect the Render logs. The production UI and API are
same-origin, so production CORS configuration should not be necessary.

### The bot is enabled but does nothing

Check `/api/health` and confirm `lobbyBot.configured` is true. Both `LobbyBot__PlayerName` and
`LobbyBot__LobbyName` must be non-empty. Then inspect logs for authorization, join, or create actions.
Remember that the bot is unavailable whenever a free service is spun down.

### Domain verification fails

Confirm the Render hostname works, remove conflicting A/CNAME/forwarding/AAAA records, use the exact
values Render displays, and allow time for DNS propagation.

Official references:

- <https://render.com/docs/blueprint-spec>
- <https://render.com/docs/docker>
- <https://render.com/docs/free>
- <https://render.com/docs/custom-domains>
- <https://render.com/docs/health-checks>
