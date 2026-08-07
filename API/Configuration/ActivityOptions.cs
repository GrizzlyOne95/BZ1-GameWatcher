namespace BZAPI.Configuration;

/// <summary>
/// Controls privacy-safe aggregate multiplayer activity sampling.
/// </summary>
public sealed class ActivityOptions
{
    public const string SectionName = "Activity";

    /// <summary>Whether aggregate history sampling is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often a historical aggregate sample is recorded.</summary>
    public TimeSpan SamplingInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum age of samples retained in memory and optional persistence.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Optional JSON persistence file. Leave empty for process-local history. Point this at durable
    /// mounted storage when uninterrupted multi-day history across restarts is required.
    /// </summary>
    public string? PersistencePath { get; set; }
}
