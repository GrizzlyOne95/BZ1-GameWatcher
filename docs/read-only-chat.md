# Read-only public lobby chat

Game Watcher can observe configured public BZ98 chat lobbies using server-side WebSocket sessions.

- The observer authenticates as a Web user and clearly identifies itself as `BZ1 Game Watcher (read-only)`.
- Only configured public lobby names are observed; arbitrary user-created chat lobbies are ignored.
- Recent chat is bounded in memory and is not persisted.
- The public API exposes only author/speaker ID, message text, and timestamp.
- Browser clients receive no lobby-server credentials and no operation that can send a chat message.
- Player IP, WAN, and LAN addresses remain outside the public API contract.
