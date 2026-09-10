namespace BZAPI.Configuration;

/// <summary>
/// Controls the server-side, read-only observers used to retain a small amount of recent public
/// chat history for selected Battlezone chat lobbies.
/// </summary>
public sealed class ChatObserverOptions
{
    public const string SectionName = "ChatObserver";

    /// <summary>Whether read-only chat observation is enabled.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Public identity used by the observer while it is joined to a chat lobby. Keeping the
    /// read-only purpose in the name makes the extra Web user transparent to other players.
    /// </summary>
    public string PlayerName { get; set; } = "BZ1 Game Watcher (read-only)";

    /// <summary>
    /// Friendly chat-lobby names to observe. An empty list observes nothing; this prevents an
    /// arbitrary user-created chat lobby from causing the service to open another connection.
    /// </summary>
    public string[] LobbyNames { get; set; } = ["default", "discord"];

    /// <summary>Hard cap on simultaneous observer connections.</summary>
    public int MaxObservedLobbies { get; set; } = 4;

    /// <summary>Maximum recent messages retained in memory per lobby.</summary>
    public int MaxMessagesPerLobby { get; set; } = 50;

    /// <summary>Maximum characters retained from one upstream chat message.</summary>
    public int MaxMessageLength { get; set; } = 500;

    /// <summary>
    /// Delay before retrying a genuinely lost or failed observer connection. Quiet chat rooms do
    /// not use this timer: inactivity-based reconnects are disabled so a healthy observer keeps a
    /// single server session and lobby membership for as long as possible.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum observer socket creations across all rooms in one window.</summary>
    public int MaxConnectionAttemptsPerWindow { get; set; } = 3;

    /// <summary>Sliding window used by the process-wide observer connection budget.</summary>
    public TimeSpan ConnectionAttemptWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long all observers pause after the shared connection budget is exhausted.</summary>
    public TimeSpan CircuitOpenDuration { get; set; } = TimeSpan.FromHours(1);
}
