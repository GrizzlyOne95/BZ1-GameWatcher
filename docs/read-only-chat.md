# Read-only public lobby chat

Game Watcher can observe configured public BZ98 chat lobbies using server-side WebSocket sessions.

- The observer authenticates as a Web user and clearly identifies itself as `BZ1 Game Watcher (read-only)`.
- Only configured public lobby names are observed; arbitrary user-created chat lobbies are ignored.
- Each configured lobby uses one long-lived observer session so incoming `OnChatMessage` events remain live and can be attributed to the correct lobby.
- Observer creation/removal is driven by authoritative lobby snapshot changes from the main watcher; chat observation does not poll the matchmaking service for messages or periodically rejoin rooms.
- Inactivity-based WebSocket reconnects are disabled. A quiet chat room therefore keeps the same server session and lobby membership instead of obtaining a new Web user ID every few minutes.
- Genuine transport failures still reconnect so live chat can recover, but retries use the configured `ReconnectDelay` (one minute by default) before re-authorizing and rejoining the same configured lobby.
- Recent chat is bounded in memory and is not persisted.
- The public API exposes only author/speaker ID, message text, and timestamp.
- Browser clients receive no lobby-server credentials and no operation that can send a chat message.
- Player IP, WAN, and LAN addresses remain outside the public API contract.

## Protocol basis

The behavior follows the public Rebellion Web client rather than inventing a chat polling protocol. The Rebellion admin page opens one WebSocket, sends `Authorization` as a Web client, sends `DoEnterLounge` after `OnAuthorization`, and consumes chat as server-pushed `OnChatMessage` events. `DoJoinLobby` is a discrete lobby action in that client rather than a chat refresh operation.

Game Watcher retains a dedicated joined observer per configured room because the currently relied-on `OnChatMessage` fields do not provide a documented lobby identifier suitable for safely multiplexing several room histories through one unjoined connection. The important safety property is therefore stable membership: join once for a healthy connection, receive pushed chat continuously, and only establish a new membership after a real connection loss or a real lobby replacement.
