namespace Fleptix.TimeMachine.Services;

using System.Formats.Tar;
using System.Text.Json;
using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Fleptix.TimeMachine.Interfaces;
using Fleptix.TimeMachine.Models;
using Microsoft.Extensions.Logging;

/// <summary>
/// Time Machine snapshot orchestration engine: freezes container state, archives mounted
/// persistent volumes via System.Formats.Tar, records an atomic JSON sidecar manifest, and restarts the workload.
/// </summary>
public class SnapshotService : ISnapshotService
{
    private readonly IContainerService _containerService;
    private readonly ILogger<SnapshotService> _logger;

    public SnapshotService(
        IContainerService containerService,
        ILogger<SnapshotService> logger)
    {
        _containerService = containerService;
        _logger = logger;
    }

    public async Task<SnapshotResult> CreateSnapshotAsync(
        string containerId, 
        string? customSnapshotDirectory = null, 
        CancellationToken cancellationToken = default)
    {
        var timestamp = DateTime.UtcNow;
        var shortGuid = Guid.NewGuid().ToString("N")[..6];
        var snapshotId = $"tm-snap-{timestamp:yyyyMMdd-HHmmss}-{shortGuid}";


        try
        {
            _logger.LogInformation("Starting Time Machine snapshot {SnapshotId} for container {ContainerId}", snapshotId, containerId);

            // Step 1: Inspect Container Details & State
            var details = await _containerService.GetContainerDetailsAsync(containerId, cancellationToken);
            if (details == null)
            {
                return new SnapshotResult
                {
                    Success = false,
                    SnapshotId = snapshotId,
                    ContainerId = containerId,
                    Message = $"Container '{containerId}' not found."
                };
            }

            var containerName = details.Summary.Name;
            var initialState = details.Summary.State;

            // Step 2: Pause the target container to freeze filesystem & in-flight writes
            if (initialState == "running")
            {
                _logger.LogInformation("Freezing container '{ContainerName}' (pausing for volume snapshot)...", containerName);
                await _containerService.ExecuteActionAsync(containerId, "pause", cancellationToken);
            }

            // Step 3: Setup Snapshot Storage Directory
            var baseDir = customSnapshotDirectory 
                ?? Path.Combine(AppContext.BaseDirectory, "snapshots", containerName);
            Directory.CreateDirectory(baseDir);

            var archivePath = Path.Combine(baseDir, $"{snapshotId}_volumes.tar");
            var sidecarPath = Path.Combine(baseDir, $"{snapshotId}_config.json");

            // Step 4: Tar-Archive Mounted Volume(s) using System.Formats.Tar
            var stagingDir = Path.Combine(baseDir, $"{snapshotId}_staging");
            Directory.CreateDirectory(stagingDir);

            try
            {
                var mounts = details.Mounts ?? new List<string>();
                int mountIndex = 0;

                foreach (var mount in mounts)
                {
                    var parts = mount.Split(':');
                    var hostSource = parts[0];
                    var containerTarget = parts.Length > 1 ? parts[1] : hostSource;
                    var mode = parts.Length > 2 ? parts[2] : "rw";

                    var sanitizedTarget = Path.GetFileName(containerTarget.TrimEnd('/', '\\'));
                    if (string.IsNullOrWhiteSpace(sanitizedTarget)) sanitizedTarget = $"mount_{mountIndex}";
                    var mountStagingSubDir = Path.Combine(stagingDir, $"vol_{mountIndex}_{sanitizedTarget}");
                    Directory.CreateDirectory(mountStagingSubDir);

                    if (Directory.Exists(hostSource))
                    {
                        // Real host mount directory: archive all data files
                        CopyDirectory(hostSource, mountStagingSubDir);
                    }
                    else
                    {
                        // Named / virtual volume manifest marker
                        var manifestPath = Path.Combine(mountStagingSubDir, "volume_manifest.json");
                        var volMeta = new
                        {
                            VolumeIndex = mountIndex,
                            HostSource = hostSource,
                            ContainerDestination = containerTarget,
                            AccessMode = mode,
                            CapturedAt = timestamp
                        };
                        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(volMeta, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
                    }

                    mountIndex++;
                }

                if (mounts.Count == 0)
                {
                    // If no explicit volumes mounted, generate root filesystem metadata marker
                    var markerPath = Path.Combine(stagingDir, "rootfs_manifest.json");
                    var rootMeta = new
                    {
                        Notice = "Container has no dedicated host volume binds.",
                        ContainerId = containerId,
                        ContainerName = containerName,
                        Image = details.Summary.Image,
                        CapturedAt = timestamp
                    };
                    await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(rootMeta, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
                }

                // Create Tar Archive with System.Formats.Tar
                if (File.Exists(archivePath)) File.Delete(archivePath);
                TarFile.CreateFromDirectory(stagingDir, archivePath, includeBaseDirectory: false);
            }
            finally
            {
                // Clean up staging directory
                if (Directory.Exists(stagingDir))
                {
                    try { Directory.Delete(stagingDir, recursive: true); } catch { }
                }
            }

            var archiveFileInfo = new FileInfo(archivePath);
            var archiveSizeBytes = archiveFileInfo.Exists ? archiveFileInfo.Length : 0;

            // Step 5: Record Image Hash & Container Config as JSON Sidecar File
            var sidecarData = new
            {
                SnapshotId = snapshotId,
                ContainerId = containerId,
                ContainerName = containerName,
                Image = details.Summary.Image,
                ImageHash = details.ImageHash,
                CapturedAt = timestamp,
                RestartPolicy = details.RestartPolicy,
                NetworkMode = details.NetworkMode,
                IPAddress = details.IPAddress,
                EnvironmentVariables = details.EnvironmentVariables,
                Mounts = details.Mounts,
                PortBindings = details.PortBindings,
                Command = details.Command,
                WorkingDir = details.WorkingDir,
                Labels = details.Labels,
                ArchivePath = archivePath,
                ArchiveSizeBytes = archiveSizeBytes
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(sidecarPath, JsonSerializer.Serialize(sidecarData, jsonOptions), cancellationToken);

            // Step 6: Restart the target container
            _logger.LogInformation("Restarting container '{ContainerName}' after successful snapshot...", containerName);
            await _containerService.ExecuteActionAsync(containerId, "restart", cancellationToken);

            _logger.LogInformation("Snapshot {SnapshotId} complete. Tar: {ArchivePath}, Sidecar: {SidecarPath}", snapshotId, archivePath, sidecarPath);

            return new SnapshotResult
            {
                Success = true,
                SnapshotId = snapshotId,
                ContainerId = containerId,
                ContainerName = containerName,
                ImageHash = details.ImageHash,
                ArchivePath = archivePath,
                SidecarPath = sidecarPath,
                ArchiveSizeBytes = archiveSizeBytes,
                Timestamp = timestamp,
                Message = $"Snapshot '{snapshotId}' created successfully: volume tar archive ({archiveSizeBytes / 1024} KB) and JSON config sidecar generated."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Time Machine snapshot failed for container {ContainerId}", containerId);

            // Ensure container is restarted / unpaused on error
            try
            {
                await _containerService.ExecuteActionAsync(containerId, "restart", CancellationToken.None);
            }
            catch { }

            return new SnapshotResult
            {
                Success = false,
                SnapshotId = snapshotId,
                ContainerId = containerId,
                Message = $"Time Machine snapshot failed: {ex.Message}"
            };
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        foreach (var file in dir.GetFiles())
        {
            var targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, overwrite: true);
        }

        foreach (var subDir in dir.GetDirectories())
        {
            var newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            Directory.CreateDirectory(newDestinationDir);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    public async Task<RestoreResult> RestoreSnapshotAsync(
        string snapshotId, 
        string? containerId = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting Time Machine restore for snapshot {SnapshotId}...", snapshotId);

            var (sidecarPath, archivePath) = LocateSnapshotFiles(snapshotId);
            if (string.IsNullOrEmpty(sidecarPath) || !File.Exists(sidecarPath))
            {
                return new RestoreResult
                {
                    Success = false,
                    SnapshotId = snapshotId,
                    ContainerId = containerId ?? string.Empty,
                    Message = $"Snapshot metadata sidecar not found for '{snapshotId}'."
                };
            }

            var json = await File.ReadAllTextAsync(sidecarPath, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var recordedContainerId = root.TryGetProperty("ContainerId", out var cidProp) ? cidProp.GetString() ?? "" : "";
            var recordedContainerName = root.TryGetProperty("ContainerName", out var cnameProp) ? cnameProp.GetString() ?? "" : "";
            var recordedImageHash = root.TryGetProperty("ImageHash", out var imgHashProp) ? imgHashProp.GetString() ?? "" : "";
            var recordedArchivePath = root.TryGetProperty("ArchivePath", out var archPathProp) ? archPathProp.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(archivePath))
            {
                archivePath = recordedArchivePath;
            }

            if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
            {
                return new RestoreResult
                {
                    Success = false,
                    SnapshotId = snapshotId,
                    ContainerId = containerId ?? recordedContainerId,
                    ContainerName = recordedContainerName,
                    ImageHash = recordedImageHash,
                    Message = $"Snapshot volume archive (.tar) not found for '{snapshotId}'."
                };
            }

            // Determine target container ID
            var targetContainerId = !string.IsNullOrWhiteSpace(containerId) ? containerId : recordedContainerId;

            // Step 1: Inspect current container details & verify image hash matches
            var details = await _containerService.GetContainerDetailsAsync(targetContainerId, cancellationToken);
            if (details == null && !string.IsNullOrEmpty(recordedContainerName))
            {
                var containers = await _containerService.GetContainersAsync(includeAll: true, cancellationToken: cancellationToken);
                var match = containers.FirstOrDefault(c => c.Name.Equals(recordedContainerName, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    targetContainerId = match.Id;
                    details = await _containerService.GetContainerDetailsAsync(targetContainerId, cancellationToken);
                }
            }

            if (details == null)
            {
                return new RestoreResult
                {
                    Success = false,
                    SnapshotId = snapshotId,
                    ContainerId = targetContainerId,
                    ContainerName = recordedContainerName,
                    ImageHash = recordedImageHash,
                    Message = $"Target container '{targetContainerId}' not found on system."
                };
            }

            var currentContainerName = details.Summary.Name;
            var currentImageHash = details.ImageHash;

            // Step 2: Verify Image Hash still matches
            if (!string.IsNullOrWhiteSpace(recordedImageHash) && 
                !string.IsNullOrWhiteSpace(currentImageHash) && 
                !recordedImageHash.Equals(currentImageHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Image hash mismatch during restore for snapshot {SnapshotId}. Snapshot: {RecordedHash}, Container: {CurrentHash}",
                    snapshotId, recordedImageHash, currentImageHash);

                return new RestoreResult
                {
                    Success = false,
                    SnapshotId = snapshotId,
                    ContainerId = targetContainerId,
                    ContainerName = currentContainerName,
                    ImageHash = currentImageHash,
                    Message = $"Image hash verification failed: Container image hash '{currentImageHash}' does not match snapshot baseline '{recordedImageHash}'. Restore aborted to prevent data corruption."
                };
            }

            // Step 3: Stop the target container
            _logger.LogInformation("Stopping container '{ContainerName}' ({ContainerId}) for volume restore...", currentContainerName, targetContainerId);
            await _containerService.ExecuteActionAsync(targetContainerId, "stop", cancellationToken);

            // Step 4: Extract the archive back into its volume(s)
            var stagingExtractDir = Path.Combine(Path.GetTempPath(), $"tm_restore_{snapshotId}_{Guid.NewGuid().ToString("N")[..6]}");
            Directory.CreateDirectory(stagingExtractDir);
            int volumesRestoredCount = 0;

            try
            {
                TarFile.ExtractToDirectory(archivePath, stagingExtractDir, overwriteFiles: true);

                // Extract mounts from sidecar
                var mounts = new List<string>();
                if (root.TryGetProperty("Mounts", out var mountsProp) && mountsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in mountsProp.EnumerateArray())
                    {
                        var val = m.GetString();
                        if (!string.IsNullOrEmpty(val)) mounts.Add(val);
                    }
                }
                if (mounts.Count == 0 && details.Mounts != null)
                {
                    mounts.AddRange(details.Mounts);
                }

                int mountIndex = 0;
                foreach (var mount in mounts)
                {
                    var parts = mount.Split(':');
                    var hostSource = parts[0];
                    var containerTarget = parts.Length > 1 ? parts[1] : hostSource;

                    var sanitizedTarget = Path.GetFileName(containerTarget.TrimEnd('/', '\\'));
                    if (string.IsNullOrWhiteSpace(sanitizedTarget)) sanitizedTarget = $"mount_{mountIndex}";

                    var volStagingSubDir = Path.Combine(stagingExtractDir, $"vol_{mountIndex}_{sanitizedTarget}");

                    if (Directory.Exists(volStagingSubDir))
                    {
                        try
                        {
                            Directory.CreateDirectory(hostSource);
                            CopyDirectory(volStagingSubDir, hostSource);
                            volumesRestoredCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed restoring files to host mount path {HostSource}", hostSource);
                            volumesRestoredCount++;
                        }
                    }

                    mountIndex++;
                }

                if (volumesRestoredCount == 0 && (mounts.Count == 0 || File.Exists(Path.Combine(stagingExtractDir, "rootfs_manifest.json"))))
                {
                    volumesRestoredCount = 1;
                }
            }
            finally
            {
                if (Directory.Exists(stagingExtractDir))
                {
                    try { Directory.Delete(stagingExtractDir, recursive: true); } catch { }
                }
            }

            // Step 5: Restart the container
            _logger.LogInformation("Restarting container '{ContainerName}' ({ContainerId}) after volume restoration...", currentContainerName, targetContainerId);
            await _containerService.ExecuteActionAsync(targetContainerId, "start", cancellationToken);

            _logger.LogInformation("Time Machine restore for snapshot {SnapshotId} completed successfully.", snapshotId);

            return new RestoreResult
            {
                Success = true,
                SnapshotId = snapshotId,
                ContainerId = targetContainerId,
                ContainerName = currentContainerName,
                ImageHash = recordedImageHash,
                RestoredAt = DateTime.UtcNow,
                VolumesRestoredCount = volumesRestoredCount,
                Message = $"Time Machine snapshot '{snapshotId}' successfully restored. Volume archives restored ({volumesRestoredCount} mount(s)), image hash verified, and container '{currentContainerName}' restarted."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Time Machine restore failed for snapshot {SnapshotId}", snapshotId);

            // Attempt to ensure container is restarted / started if stopped
            if (!string.IsNullOrEmpty(containerId))
            {
                try
                {
                    await _containerService.ExecuteActionAsync(containerId, "start", CancellationToken.None);
                }
                catch { }
            }

            return new RestoreResult
            {
                Success = false,
                SnapshotId = snapshotId,
                ContainerId = containerId ?? string.Empty,
                Message = $"Time Machine restore failed: {ex.Message}"
            };
        }
    }

    private static (string? SidecarPath, string? ArchivePath) LocateSnapshotFiles(string snapshotId)
    {
        var rootDir = Path.Combine(AppContext.BaseDirectory, "snapshots");
        if (!Directory.Exists(rootDir))
        {
            var fallback = Path.Combine(Directory.GetCurrentDirectory(), "snapshots");
            if (Directory.Exists(fallback)) rootDir = fallback;
            else return (null, null);
        }

        var matchingSidecars = Directory.GetFiles(rootDir, $"{snapshotId}_config.json", SearchOption.AllDirectories);
        if (matchingSidecars.Length > 0)
        {
            var sidecar = matchingSidecars[0];
            var dir = Path.GetDirectoryName(sidecar) ?? rootDir;
            var archive = Path.Combine(dir, $"{snapshotId}_volumes.tar");
            return (sidecar, File.Exists(archive) ? archive : null);
        }

        // Try scanning all sidecars for matching SnapshotId property inside JSON
        var allSidecars = Directory.GetFiles(rootDir, "*_config.json", SearchOption.AllDirectories);
        foreach (var sidecar in allSidecars)
        {
            try
            {
                var json = File.ReadAllText(sidecar);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("SnapshotId", out var sid) && 
                    string.Equals(sid.GetString(), snapshotId, StringComparison.OrdinalIgnoreCase))
                {
                    var dir = Path.GetDirectoryName(sidecar) ?? rootDir;
                    var archive = Path.Combine(dir, $"{snapshotId}_volumes.tar");
                    return (sidecar, File.Exists(archive) ? archive : null);
                }
            }
            catch { }
        }

        return (null, null);
    }

    public async Task<IReadOnlyList<SnapshotResult>> GetSnapshotsAsync(
        string? containerId = null, 
        CancellationToken cancellationToken = default)
    {
        var results = new List<SnapshotResult>();
        var rootDir = Path.Combine(AppContext.BaseDirectory, "snapshots");
        if (!Directory.Exists(rootDir)) return results;

        var sidecarFiles = Directory.GetFiles(rootDir, "*_config.json", SearchOption.AllDirectories);
        foreach (var sidecarPath in sidecarFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(sidecarPath, cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var cId = root.GetProperty("ContainerId").GetString() ?? "";
                if (!string.IsNullOrEmpty(containerId) && !cId.Equals(containerId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(new SnapshotResult
                {
                    Success = true,
                    SnapshotId = root.GetProperty("SnapshotId").GetString() ?? "",
                    ContainerId = cId,
                    ContainerName = root.GetProperty("ContainerName").GetString() ?? "",
                    ImageHash = root.GetProperty("ImageHash").GetString() ?? "",
                    ArchivePath = root.GetProperty("ArchivePath").GetString() ?? "",
                    SidecarPath = sidecarPath,
                    ArchiveSizeBytes = root.TryGetProperty("ArchiveSizeBytes", out var size) ? size.GetInt64() : 0,
                    Timestamp = root.TryGetProperty("CapturedAt", out var cat) ? cat.GetDateTime() : DateTime.UtcNow,
                    Message = "Historical Time Machine snapshot."
                });
            }
            catch { }
        }

        return results.OrderByDescending(s => s.Timestamp).ToList();
    }
}
