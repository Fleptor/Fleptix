namespace Fleptix.Core.Models;

/// <summary>
/// Result of a Time Machine container snapshot operation.
/// </summary>
public record SnapshotResult
{
    public bool Success { get; init; }
    public string SnapshotId { get; init; } = string.Empty;
    public string ContainerId { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string ImageHash { get; init; } = string.Empty;
    public string ArchivePath { get; init; } = string.Empty;
    public string SidecarPath { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public long ArchiveSizeBytes { get; init; }
}
