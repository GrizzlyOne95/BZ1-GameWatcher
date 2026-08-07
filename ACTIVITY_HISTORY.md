# Activity history and persistence

The `/activity` page records privacy-safe multiplayer activity aggregates every five minutes by
default. The retained sample contains only:

- UTC sample time
- players currently reported in non-chat game lobbies
- active game-lobby count
- launched/in-progress game-lobby count
- public waiting-room user count, excluding Game Watcher's own read-only chat observer

It does **not** store player names or IDs, chat text, lobby names/settings, Steam IDs, IP/WAN/LAN
addresses, or other network identifiers.

## Storage modes

`Activity:PersistencePath` controls whether samples are written to a JSON file in addition to memory.
The public API reports the storage kind as `memory` or `file`.

A configured file path is **not automatically considered durable**. Hosted containers commonly use an
ephemeral filesystem, so `Activity:PersistenceIsDurable` must also be explicitly set to `true` before
the UI/API describes history as durable.

This prevents a path such as `/tmp/activity.json` from accidentally being presented to visitors as
persistent history.

## Current free Render deployment

The active `render.yaml` intentionally remains on Render's Free web-service instance. Free Render web
services use an ephemeral filesystem and cannot attach persistent disks, so activity history can reset
when the service spins down, restarts, or redeploys.

Render references:

- <https://render.com/docs/free>
- <https://render.com/docs/disks>
- <https://render.com/docs/blueprint-spec>

The application therefore leaves `Activity:PersistencePath` empty and reports non-durable history on
the default free deployment.

## Opt-in durable Render deployment

`render.persistent.example.yaml` is an **example only** and is not used automatically. It demonstrates
the paid-service configuration needed for a persistent disk:

```yaml
plan: starter
disk:
  name: activity-history
  mountPath: /var/data
  sizeGB: 1
envVars:
  - key: Activity__PersistencePath
    value: /var/data/activity-history.json
  - key: Activity__PersistenceIsDurable
    value: "true"
```

Only use that Blueprint intentionally: changing from `free` to `starter` and attaching a disk is a
paid Render configuration.

With a persistent disk mounted at `/var/data`, the activity JSON file survives application deploys and
restarts. The service remains single-instance, which is appropriate for this file-backed activity
store.

## Configuration

| Setting | Environment variable | Default | Purpose |
| --- | --- | --- | --- |
| `Activity:Enabled` | `Activity__Enabled` | `true` | Enables aggregate sampling. |
| `Activity:SamplingInterval` | `Activity__SamplingInterval` | `00:05:00` | Interval between retained samples. |
| `Activity:Retention` | `Activity__Retention` | `30.00:00:00` | Maximum retained sample age. |
| `Activity:PersistencePath` | `Activity__PersistencePath` | empty | Optional JSON file path. |
| `Activity:PersistenceIsDurable` | `Activity__PersistenceIsDurable` | `false` | Declares that the configured path survives host restarts/redeploys. |

## Export and migration

`GET /api/activity/export` returns the complete retained aggregate window as JSON. This endpoint is
read-only and contains the same privacy-safe counts used by the Activity dashboard.

It can be used to:

- take a manual backup before changing hosting/storage
- inspect the raw five-minute samples
- migrate historical aggregates to a future datastore without exposing user-level records

There is intentionally no unauthenticated import endpoint. Allowing arbitrary web visitors to replace
historical data would create an integrity problem.

## API status fields

`GET /api/activity` reports:

- `historyStorage`: `memory` or `file`
- `durableHistory`: whether the configured file storage is explicitly declared durable
- `historyStartedUtc`
- `lastHistoricalSampleUtc`

`GET /api/health` reports the corresponding activity storage and durability fields for operational
checks.
