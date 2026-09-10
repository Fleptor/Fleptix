namespace Fleptix.Observer.Services;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;

/// <summary>
/// Orchestrates active container service, providing automated fallback to Demo simulation
/// upon Docker daemon connection failure and manual switching between Real Docker and Demo Mode.
/// </summary>
public class ContainerServiceManager : IContainerService
{
    private readonly DockerContainerService _dockerService;
    private readonly DemoContainerService _demoService;
    private bool _forceDemoMode;

    public ContainerServiceManager(
        DockerContainerService dockerService, 
        DemoContainerService demoService)
    {
        _dockerService = dockerService;
        _demoService = demoService;
        // Default to false: always attempt real Docker first, only fall back on connection failure
        _forceDemoMode = false;
    }

    public bool IsConnectedToDocker => !_forceDemoMode && _dockerService.IsConnectedToDocker;

    public bool ForceDemoMode
    {
        get => _forceDemoMode;
        set => _forceDemoMode = value;
    }

    public async Task<IReadOnlyList<ContainerSummary>> GetContainersAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        if (!_forceDemoMode)
        {
            try
            {
                if (await _dockerService.CheckConnectivityAsync(cancellationToken))
                {
                    return await _dockerService.GetContainersAsync(includeAll, cancellationToken);
                }
            }
            catch
            {
                // Fallback to DemoContainerService on Docker connection failure
            }
        }

        return await _demoService.GetContainersAsync(includeAll, cancellationToken);
    }

    public async Task<ContainerDetails?> GetContainerDetailsAsync(string containerId, CancellationToken cancellationToken = default)
    {
        if (!_forceDemoMode)
        {
            try
            {
                if (await _dockerService.CheckConnectivityAsync(cancellationToken))
                {
                    var details = await _dockerService.GetContainerDetailsAsync(containerId, cancellationToken);
                    if (details != null) return details;
                }
            }
            catch
            {
                // Fallback to DemoContainerService on Docker connection failure
            }
        }

        return await _demoService.GetContainerDetailsAsync(containerId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, ContainerMetrics>> GetAllCurrentMetricsAsync(CancellationToken cancellationToken = default)
    {
        if (!_forceDemoMode)
        {
            try
            {
                if (await _dockerService.CheckConnectivityAsync(cancellationToken))
                {
                    return await _dockerService.GetAllCurrentMetricsAsync(cancellationToken);
                }
            }
            catch
            {
                // Fallback to DemoContainerService on Docker connection failure
            }
        }

        return await _demoService.GetAllCurrentMetricsAsync(cancellationToken);
    }

    public async Task<ContainerMetrics?> GetMetricsAsync(string containerId, CancellationToken cancellationToken = default)
    {
        if (!_forceDemoMode)
        {
            try
            {
                if (await _dockerService.CheckConnectivityAsync(cancellationToken))
                {
                    var metrics = await _dockerService.GetMetricsAsync(containerId, cancellationToken);
                    if (metrics != null) return metrics;
                }
            }
            catch
            {
                // Fallback to DemoContainerService on Docker connection failure
            }
        }

        return await _demoService.GetMetricsAsync(containerId, cancellationToken);
    }

    public IAsyncEnumerable<ContainerMetrics> GetMetricsStreamAsync(string containerId, CancellationToken cancellationToken = default)
    {
        return (_forceDemoMode || !_dockerService.IsConnectedToDocker)
            ? _demoService.GetMetricsStreamAsync(containerId, cancellationToken)
            : _dockerService.GetMetricsStreamAsync(containerId, cancellationToken);
    }

    public async Task<ContainerActionResult> ExecuteActionAsync(string containerId, string action, CancellationToken cancellationToken = default)
    {
        if (!_forceDemoMode)
        {
            try
            {
                if (await _dockerService.CheckConnectivityAsync(cancellationToken))
                {
                    return await _dockerService.ExecuteActionAsync(containerId, action, cancellationToken);
                }
            }
            catch
            {
                // Fallback to DemoContainerService on Docker connection failure
            }
        }

        return await _demoService.ExecuteActionAsync(containerId, action, cancellationToken);
    }

    public async Task<DeployContainerResult> DeployContainerAsync(DeployContainerRequest request, CancellationToken cancellationToken = default)
    {
        if (!_forceDemoMode)
        {
            try
            {
                if (await _dockerService.CheckConnectivityAsync(cancellationToken))
                {
                    return await _dockerService.DeployContainerAsync(request, cancellationToken);
                }
            }
            catch
            {
                // Fallback to DemoContainerService on Docker connection failure
            }
        }

        return await _demoService.DeployContainerAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetLogsAsync(string containerId, int tailLines = 100, CancellationToken cancellationToken = default)
    {
        if (!_forceDemoMode)
        {
            try
            {
                if (await _dockerService.CheckConnectivityAsync(cancellationToken))
                {
                    return await _dockerService.GetLogsAsync(containerId, tailLines, cancellationToken);
                }
            }
            catch
            {
                // Fallback to DemoContainerService on Docker connection failure
            }
        }

        return await _demoService.GetLogsAsync(containerId, tailLines, cancellationToken);
    }

    private async Task<bool> IsRealDockerContainerAsync(string containerId, CancellationToken cancellationToken)
    {
        if (_forceDemoMode) return false;
        try
        {
            if (await _dockerService.CheckConnectivityAsync(cancellationToken))
            {
                var details = await _dockerService.GetContainerDetailsAsync(containerId, cancellationToken);
                return details != null;
            }
        }
        catch
        {
            // Fall back
        }
        return false;
    }

    public async Task<IReadOnlyList<ContainerFileSystemItem>> GetContainerFilesAsync(string containerId, string path = "/", CancellationToken cancellationToken = default)
    {
        if (await IsRealDockerContainerAsync(containerId, cancellationToken))
        {
            try
            {
                return await _dockerService.GetContainerFilesAsync(containerId, path, cancellationToken);
            }
            catch
            {
                // Fallback to DemoContainerService on failure
            }
        }

        return await _demoService.GetContainerFilesAsync(containerId, path, cancellationToken);
    }

    public async Task<ContainerExecResult> ExecCommandAsync(string containerId, ContainerExecRequest request, CancellationToken cancellationToken = default)
    {
        if (await IsRealDockerContainerAsync(containerId, cancellationToken))
        {
            try
            {
                return await _dockerService.ExecCommandAsync(containerId, request, cancellationToken);
            }
            catch
            {
                // Fallback to DemoContainerService on failure
            }
        }

        return await _demoService.ExecCommandAsync(containerId, request, cancellationToken);
    }

    public async Task<ContainerFileContentResult> GetFileContentAsync(string containerId, string path, CancellationToken cancellationToken = default)
    {
        if (await IsRealDockerContainerAsync(containerId, cancellationToken))
        {
            try
            {
                return await _dockerService.GetFileContentAsync(containerId, path, cancellationToken);
            }
            catch
            {
                // Fallback to DemoContainerService on failure
            }
        }

        return await _demoService.GetFileContentAsync(containerId, path, cancellationToken);
    }

    public async Task<(Stream? Stream, string FileName, long Size)> GetFileArchiveStreamAsync(string containerId, string path, CancellationToken cancellationToken = default)
    {
        if (await IsRealDockerContainerAsync(containerId, cancellationToken))
        {
            try
            {
                return await _dockerService.GetFileArchiveStreamAsync(containerId, path, cancellationToken);
            }
            catch
            {
                // Fallback to DemoContainerService on failure
            }
        }

        return await _demoService.GetFileArchiveStreamAsync(containerId, path, cancellationToken);
    }
}


