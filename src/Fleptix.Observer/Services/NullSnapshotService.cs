namespace Fleptix.Observer.Services;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;

/// <summary>
/// Null Object pattern implementation of ISnapshotService for the open-source Fleptix Observer edition.
/// Used when the commercial Fleptix.TimeMachine extension is not present or activated.
/// </summary>
public class NullSnapshotService : ISnapshotService
{
    public Task<SnapshotResult> CreateSnapshotAsync(
        string containerId, 
        string? customSnapshotDirectory = null, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SnapshotResult
        {
            Success = false,
            ContainerId = containerId,
            Message = "Time Machine snapshot capabilities require a licensed edition of Fleptix Time Machine."
        });
    }

    public Task<RestoreResult> RestoreSnapshotAsync(
        string snapshotId, 
        string? containerId = null, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RestoreResult
        {
            Success = false,
            SnapshotId = snapshotId,
            ContainerId = containerId ?? string.Empty,
            Message = "Time Machine restore capabilities require a licensed edition of Fleptix Time Machine."
        });
    }

    public Task<IReadOnlyList<SnapshotResult>> GetSnapshotsAsync(
        string? containerId = null, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<SnapshotResult>>(Array.Empty<SnapshotResult>());
    }

    public Task<long> GetContainerSnapshotStorageBytesAsync(
        string containerId, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(0L);
    }

    public Task<bool> DeleteSnapshotAsync(
        string snapshotId, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<PruneResult> PruneSnapshotsAsync(
        string? containerId = null, 
        TimeMachineRetentionSettings? customSettings = null, 
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PruneResult
        {
            Success = false,
            ContainerId = containerId,
            Message = "Time Machine snapshot retention pruning requires Fleptix Time Machine."
        });
    }
}
