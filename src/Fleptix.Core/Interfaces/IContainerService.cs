namespace Fleptix.Core.Interfaces;

using Fleptix.Core.Models;

/// <summary>
/// Domain contract for container inspection, telemetry, and lifecycle management.
/// </summary>
public interface IContainerService
{
    /// <summary>
    /// Indicates whether the service is actively connected to the Docker daemon.
    /// </summary>
    bool IsConnectedToDocker { get; }

    /// <summary>
    /// The target Docker daemon endpoint URI.
    /// </summary>
    Uri DockerUri { get; }

    /// <summary>
    /// Holds the error description if the daemon is unreachable.
    /// </summary>
    string? LastConnectionError { get; }

    /// <summary>
    /// Asynchronously probes connectivity to the Docker engine.
    /// </summary>
    Task<bool> CheckConnectivityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all containers for dashboard display.
    /// </summary>
    Task<IReadOnlyList<ContainerSummary>> GetContainersAsync(
        bool includeAll = true, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves detailed configuration and metadata for a specific container.
    /// </summary>
    Task<ContainerDetails?> GetContainerDetailsAsync(
        string containerId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current snapshot of metrics for all running containers.
    /// </summary>
    Task<IReadOnlyDictionary<string, ContainerMetrics>> GetAllCurrentMetricsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves current metrics for a single container.
    /// </summary>
    Task<ContainerMetrics?> GetMetricsAsync(
        string containerId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves host and Docker daemon storage allocation breakdown.
    /// </summary>
    Task<DockerStorageInfo> GetStorageInfoAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams live metrics for a container as an asynchronous stream.
    /// </summary>
    IAsyncEnumerable<ContainerMetrics> GetMetricsStreamAsync(
        string containerId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes lifecycle commands: start, stop, restart, pause, unpause.
    /// </summary>
    Task<ContainerActionResult> ExecuteActionAsync(
        string containerId, 
        string action, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deploys a new container by pulling the image, creating the container with requested bindings, and starting it.
    /// </summary>
    Task<DeployContainerResult> DeployContainerAsync(
        DeployContainerRequest request, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches recent log output lines for a container.
    /// </summary>
    Task<IReadOnlyList<string>> GetLogsAsync(
        string containerId, 
        int tailLines = 100, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Browses files and directories in a container's filesystem at the specified path.
    /// </summary>
    Task<IReadOnlyList<ContainerFileSystemItem>> GetContainerFilesAsync(
        string containerId,
        string path = "/",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a shell command inside the specified container and returns execution results.
    /// </summary>
    Task<ContainerExecResult> ExecCommandAsync(
        string containerId,
        ContainerExecRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves file content and metadata for previewing a specific file using GetArchiveFromContainerAsync.
    /// </summary>
    Task<ContainerFileContentResult> GetFileContentAsync(
        string containerId,
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a stream to download a specific file using GetArchiveFromContainerAsync.
    /// </summary>
    Task<(Stream? Stream, string FileName, long Size)> GetFileArchiveStreamAsync(
        string containerId,
        string path,
        CancellationToken cancellationToken = default);
}

