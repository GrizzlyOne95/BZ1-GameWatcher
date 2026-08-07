namespace BZAPI.Configuration
{
    /// <summary>
    /// Settings for the connection to the Battlezone 98 Redux lobby server.
    /// </summary>
    public sealed class BattlezoneOptions
    {
        public const string SectionName = "Battlezone";

        /// <summary>
        /// Websocket endpoint that broadcasts lobby state.
        /// </summary>
        public string LobbyServerUrl { get; set; } = "ws://battlezone98mp.webdev.rebellion.co.uk:1337/";

        /// <summary>
        /// Reconnect if no message has been received for this long. Acts as a watchdog for a
        /// connection that is open but no longer receiving updates, which would otherwise leave
        /// the API serving stale lobbies indefinitely.
        /// </summary>
        public TimeSpan? StaleConnectionTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How long to wait before reconnecting after a connection error.
        /// </summary>
        public TimeSpan ErrorReconnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Optional addresses whose users should be omitted from the visible lobby member list.
        /// Empty by default: third-party bridge/service accounts are treated like any other user.
        /// Network addresses themselves are still excluded from the public API response model.
        /// </summary>
        public string[] HiddenUserIpAddresses { get; set; } = [];

        /// <summary>
        /// Steam IDs flagged with <see cref="Models.BZ98User.IsDangerous"/>. The default mirrors
        /// the known-user warning maintained by the Battlezone Lobby Monitor project.
        /// </summary>
        public ulong[] FlaggedSteamIds { get; set; } = [76561198297657246UL];
    }
}
