# Read-only public lobby chat

Game Watcher can observe configured public BZ98 chat lobbies using server-side WebSocket sessions.

- The observer authenticates as a Web user and clearly identifies itself as `BZ1 Game Watcher (read-only)`. The name is declared in the `Authorization` payload, which is what sets the user's top-level name on the lobby server; a `SetPlayerData` update only reaches the user metadata bag and leaves the account showing as `unknown` to other players and in the lobby `userPack`. Both are sent, so the two views of the user agree.
- Only configured public lobby names are observed; arbitrary user-created chat lobbies are ignored.
- Each configured lobby uses one long-lived observer session so incoming `OnChatMessage` events remain live and can be attributed to the correct lobby.
- Observer creation/removal is driven by authoritative lobby snapshot changes from the main watcher; chat observation does not poll the matchmaking service for messages or periodically rejoin rooms.
- Inactivity-based WebSocket reconnects are disabled. A quiet chat room therefore keeps the same server session and lobby membership instead of obtaining a new Web user ID every few minutes.
- Genuine transport failures still reconnect so live chat can recover, but the application creates each replacement socket itself after the configured `ReconnectDelay` (five minutes by default). A process-wide budget permits at most three observer socket creations across all rooms in 30 minutes, then opens a one-hour circuit breaker. Lobby-ID churn cannot reset or multiply that budget.
- Protocol-level rejections also open the circuit breaker instead of consuming budget on doomed retries: an authorization rejection, a failed lobby join, or an unable-to-send socket all pause that room (and any new observers) for the configured cooldown.
- Recent chat is bounded in memory and is not persisted.
- The public API exposes only author/speaker ID, message text, and timestamp.
- Browser clients receive no lobby-server credentials and no operation that can send a chat message.
- Player IP, WAN, and LAN addresses remain outside the public API contract.

## Declared identity

Game Watcher's sessions are named accounts on the lobby server, not anonymous ones. The lounge watcher declares `Battlezone:PlayerName` (`BZ1 Game Watcher`) and each chat observer declares `ChatObserver:PlayerName` (`BZ1 Game Watcher (read-only)`), both inside `Authorization`. Community service accounts that appear under their own name upstream — the `!BRIDGE` chat bridge is the visible example — identify themselves the same way, as does `Battlezone_LobbyMonitor`.

This was a real gap rather than a policy choice. Before it was fixed the admin roster listed the observer as `unknown` with `{"name":"BZ1 Game Watcher (read-only)", ...}` sitting in its metadata column, so the transparency this document promises was not visible to anyone actually in the room. `API.Tests/BzrNetAuthorizationTests.cs` pins the payload against that regression.

Setting either name to empty restores an anonymous session. That remains a supported configuration, but it is a deliberate choice and not the default.

## Protocol basis

The behavior follows the public Rebellion Web client rather than inventing a chat polling protocol. The Rebellion admin page opens one WebSocket, sends `Authorization` as a Web client, sends `DoEnterLounge` after `OnAuthorization`, and consumes chat as server-pushed `OnChatMessage` events. `DoJoinLobby` is a discrete lobby action in that client rather than a chat refresh operation.

Game Watcher retains a dedicated joined observer per configured room because the currently relied-on `OnChatMessage` fields do not provide a documented lobby identifier suitable for safely multiplexing several room histories through one unjoined connection. The important safety property is therefore stable membership: join once for a healthy connection, receive pushed chat continuously, and only establish a new membership after a real connection loss or a real lobby replacement.
