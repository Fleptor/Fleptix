namespace Fleptix.Core.Models;

/// <summary>
/// Result of a Time Machine container restore operation.
/// </summary>
public record RestoreResult
{
    public bool Success { get; init; }
    public string SnapshotId { get; init; } = string.Empty;
    public string ContainerId { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string ImageHash { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime RestoredAt { get; init; } = DateTime.UtcNow;
    public int VolumesRestoredCount { get; init; }
}
