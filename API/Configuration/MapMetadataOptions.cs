namespace BZAPI.Configuration
{
    /// <summary>
    /// Settings for optional Battlezone map-title/image/game-mode enrichment.
    /// </summary>
    public sealed class MapMetadataOptions
    {
        public const string SectionName = "MapMetadata";

        /// <summary>
        /// Public BZ98R map metadata service. Leave empty to disable map enrichment while keeping
        /// the raw lobby map filename and settings available.
        /// </summary>
        public string BaseUrl { get; set; } = "https://gamelistassets.iondriver.com/bz98r";

        /// <summary>How long successful map metadata is cached.</summary>
        public TimeSpan CacheDuration { get; set; } = TimeSpan.FromHours(24);

        /// <summary>How long failed/missing lookups are negatively cached.</summary>
        public TimeSpan FailureCacheDuration { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Maximum time a lobby response waits on an uncached map metadata request. Enrichment is
        /// optional and must never make the Rebellion lobby data appear unavailable.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(3);
    }
}
