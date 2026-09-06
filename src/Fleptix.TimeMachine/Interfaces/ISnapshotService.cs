namespace Fleptix.TimeMachine.Interfaces;

using Fleptix.TimeMachine.Models;

/// <summary>
/// Domain contract for Time Machine container state and volume snapshot operations.
/// </summary>
public interface ISnapshotService
{
    /// <summary>
    /// Executes the full snapshot workflow: pauses the container, tar-archives mounted volumes,
    /// writes the image hash and container configuration as a JSON sidecar file, and restarts the container.
    /// </summary>
    Task<SnapshotResult> CreateSnapshotAsync(
        string containerId, 
        string? customSnapshotDirectory = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a container from a snapshot archive and its metadata sidecar:
    /// stops the target container, extracts the archive back into its volume(s),
    /// verifies the image hash still matches, and restarts the container.
    /// </summary>
    Task<RestoreResult> RestoreSnapshotAsync(
        string snapshotId,
        string? containerId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves historical snapshots recorded for a container or the entire fleet.
    /// </summary>
    Task<IReadOnlyList<SnapshotResult>> GetSnapshotsAsync(
        string? containerId = null, 
        CancellationToken cancellationToken = default);
}
