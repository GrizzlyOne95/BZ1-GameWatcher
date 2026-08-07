namespace BZAPI.Configuration
{
    /// <summary>
    /// Settings for Steam profile and Workshop enrichment.
    /// </summary>
    public sealed class SteamOptions
    {
        public const string SectionName = "Steam";

        /// <summary>Battlezone 98 Redux Steam application ID.</summary>
        public uint AppId { get; set; } = 301650;

        /// <summary>
        /// Steam Web API key, obtained from https://steamcommunity.com/dev/apikey.
        /// Supplied via configuration: the <c>Steam__ApiKey</c> environment variable or user
        /// secrets. When empty, avatar lookups are skipped. Public Workshop item metadata can
        /// still be resolved without a key.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// How long a successfully resolved avatar URL is cached before Steam is queried again.
        /// </summary>
        public TimeSpan AvatarCacheDuration { get; set; } = TimeSpan.FromHours(6);

        /// <summary>
        /// How long a failed lookup is cached, to avoid hammering Steam for accounts that
        /// consistently fail to resolve.
        /// </summary>
        public TimeSpan AvatarFailureCacheDuration { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>How long successful Workshop item metadata is cached.</summary>
        public TimeSpan WorkshopCacheDuration { get; set; } = TimeSpan.FromHours(6);

        /// <summary>How long a failed or missing Workshop lookup is negatively cached.</summary>
        public TimeSpan WorkshopFailureCacheDuration { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Maximum time a lobby response waits for an uncached Workshop lookup. Steam enrichment
        /// is optional, so a slow third-party endpoint must not make the lobby list feel offline.
        /// </summary>
        public TimeSpan WorkshopRequestTimeout { get; set; } = TimeSpan.FromSeconds(4);
    }
}
