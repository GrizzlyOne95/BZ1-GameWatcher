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
    /// Optional JSON persistence file. Leave empty for memory-only history. On hosted platforms the
    /// file is only durable if this path resides on mounted persistent storage.
    /// </summary>
    public string? PersistencePath { get; set; }

    /// <summary>
    /// Explicitly declares that <see cref="PersistencePath"/> is backed by storage that survives
    /// service restarts/redeploys. This is intentionally opt-in: merely writing a file on an
    /// ephemeral container filesystem must never be presented to visitors as durable history.
    /// </summary>
    public bool PersistenceIsDurable { get; set; } = false;
}
