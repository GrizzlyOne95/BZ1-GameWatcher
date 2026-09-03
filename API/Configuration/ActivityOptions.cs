namespace BZAPI.Configuration;

/// <summary>
/// Controls privacy-safe aggregate multiplayer activity sampling and lobby-state transition history.
/// </summary>
public sealed class ActivityOptions
{
    public const string SectionName = "Activity";

    /// <summary>Whether aggregate history sampling and lobby-event tracking are enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often a historical aggregate sample is recorded.</summary>
    public TimeSpan SamplingInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum age of aggregate samples retained in memory and optional persistence.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Optional JSON persistence file for aggregate samples. Leave empty for memory-only history.
    /// On hosted platforms the file is only durable if this path resides on mounted persistent storage.
    /// </summary>
    public string? PersistencePath { get; set; }

    /// <summary>
    /// Explicitly declares that <see cref="PersistencePath"/> is backed by storage that survives
    /// service restarts/redeploys. This is intentionally opt-in: merely writing a file on an
    /// ephemeral container filesystem must never be presented to visitors as durable history.
    /// </summary>
    public bool PersistenceIsDurable { get; set; } = false;

    /// <summary>Maximum age of discrete lobby-state events retained in memory and optional persistence.</summary>
    public TimeSpan EventRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Safety cap for retained lobby-state events. Oldest entries are discarded when the cap is exceeded.
    /// Events contain lobby/map state and aggregate player counts only — never player identities.
    /// </summary>
    public int EventMaxEntries { get; set; } = 10_000;

    /// <summary>
    /// Optional JSON persistence file for lobby-state events. Leave empty for memory-only event history.
    /// </summary>
    public string? EventPersistencePath { get; set; }

    /// <summary>
    /// Explicitly declares that <see cref="EventPersistencePath"/> survives service restarts/redeploys.
    /// </summary>
    public bool EventPersistenceIsDurable { get; set; } = false;
}
