namespace Fleptix.Core.Models;

/// <summary>
/// Outcome of a Time Machine snapshot retention pruning operation.
/// </summary>
public record PruneResult
{
    public bool Success { get; init; } = true;
    public string? ContainerId { get; init; }
    public int SnapshotsDeletedCount { get; init; }
    public long BytesReclaimed { get; init; }
    public IReadOnlyList<string> DeletedSnapshotIds { get; init; } = [];
    public string Message { get; init; } = string.Empty;
}
