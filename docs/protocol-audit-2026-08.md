# BZ98 lobby protocol audit notes

The Game Watcher implementation was cross-checked against the public Rebellion admin client, Nielk1's `MultiplayerSessionList` BZ98 Redux plugin, and the existing `Battlezone_LobbyMonitor` protocol handling.

## Public player identity

`authType` is authoritative for platform classification (`steam`, `gog`, `web`). ID prefixes are used only for platform-specific enrichment, such as extracting a Steam64 ID. A Web account such as `B1000002` remains Web even though its identifier does not begin with `S`. The public API continues to omit IP, WAN, and LAN address fields.

## Lobby name envelope

The upstream metadata name uses five `~`-separated fields: empty prefix, lobby type, visibility, password marker, and friendly name. Game Watcher retains the raw envelope for diagnostics and exposes only a nullable `hasPassword` boolean; it never exposes the upstream password value.

## Game settings tuple

The `*`-separated settings tuple is decoded as:

0. metadata version
1. map file
2. CRC32
3. Workshop/mod ID
4. sync join
5. satellite enabled
6. barracks enabled
7. time limit
8. lives
9. player limit
10. sniper enabled
11. kill limit
12. splinter enabled

Missing or malformed fields remain unknown/null rather than being silently converted to false or zero.

## `gameType` and launch state

The lobby metadata `gameType` value is not an MPI/Strategy selector. Current protocol research treats `0` as broken/invalid and `1` as valid. The actual multiplayer mode should come from optional map metadata enrichment when available, while the base lobby remains usable if that enrichment is unavailable.

`launched` is tri-state: `1` means in progress, `0` means still in the lobby, and a missing/unknown value remains not reported. An explicitly reported `gameended` value is preserved rather than inferred.

## Read-only chat

Selected public chat lobbies can be observed by server-side WebSocket sessions. The observer stores a bounded in-memory window of recent messages only. No API endpoint or browser-side code can send chat into Battlezone, and no chat history is persisted. Game Watcher's observer identity is excluded from the public roster/count, but owner identity is captured before that optional filtering so host display remains stable.

## Executable protocol corpus

`API.Tests/Fixtures/Protocol/` contains a small sanitized corpus of representative BZ98R lobby payloads. The fixtures run through the production protocol normalizer and public response mapper and protect the known behavior above, including authentication type, the 13-field settings tuple, password state, launch-state uncertainty, owner filtering, chat safety, and the public network-data boundary.

The corpus also exercises stock and Workshop/custom map identifiers with stubbed enrichment providers. CI never requires Rebellion servers, Steam, `gamelistassets.iondriver.com`, or Render for these tests; enrichment misses must leave the raw base lobby available.

Fixtures are synthetic protocol research artifacts, not recordings of real users. Real network identifiers, authentication secrets, tokens, and passwords are prohibited. If a network field is structurally required for a privacy regression test, only RFC 5737 IPv4 documentation ranges or the RFC 3849 `2001:db8::/32` IPv6 range may be used. The fixture-safety test enforces this rule.

Never commit a raw lobby-server capture without sanitizing network identifiers first.
