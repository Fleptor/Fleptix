namespace Fleptix.Core.Models;

/// <summary>
/// Retention and automated scheduling configuration for Time Machine container snapshots.
/// </summary>
public record TimeMachineRetentionSettings
{
    /// <summary>
    /// Whether automated background snapshots are globally enabled by default.
    /// </summary>
    public bool AutoSnapshotEnabled { get; init; } = false;

    /// <summary>
    /// Interval in hours between scheduled snapshot executions.
    /// </summary>
    public int IntervalHours { get; init; } = 24;

    /// <summary>
    /// If true, old snapshots are never deleted based on count (MaxSnapshotCount is ignored).
    /// Note: Storage safety cap (MaxTotalStorageMB) is still strictly enforced.
    /// </summary>
    public bool NeverDeleteOldSnapshots { get; init; } = false;

    /// <summary>
    /// Maximum number of snapshots to retain per container.
    /// Enforced only when NeverDeleteOldSnapshots is false.
    /// </summary>
    public int? MaxSnapshotCount { get; init; } = 10;

    /// <summary>
    /// Total storage limit in megabytes across all snapshots for a container.
    /// Acts as an unconditional safety cap, enforced regardless of NeverDeleteOldSnapshots.
    /// </summary>
    public int? MaxTotalStorageMB { get; init; } = 1024;
}
