# BZ98 lobby protocol audit notes

The Game Watcher implementation was cross-checked against the public Rebellion admin client, Nielk1's `MultiplayerSessionList` BZ98 Redux plugin, and the existing `Battlezone_LobbyMonitor` protocol handling.

## Public player identity

`authType` is authoritative for platform classification (`steam`, `gog`, `web`). ID prefixes are used only for platform-specific enrichment, such as extracting a Steam64 ID. The public API continues to omit IP, WAN, and LAN address fields.

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

## Read-only chat

Selected public chat lobbies can be observed by server-side WebSocket sessions. The observer stores a bounded in-memory window of recent messages only. No API endpoint or browser-side code can send chat into Battlezone, and no chat history is persisted.
