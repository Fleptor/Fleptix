namespace Fleptix.Observer.Controllers;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Fleptix.Observer.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/containers")]
public class ContainersApiController : ControllerBase
{
    private readonly IContainerService _containerService;
    private readonly ITimeMachineSettingsStore _settingsStore;
    private readonly ISnapshotService _snapshotService;
    private readonly ILicenseService _licenseService;

    public ContainersApiController(
        IContainerService containerService,
        ITimeMachineSettingsStore settingsStore,
        ISnapshotService snapshotService,
        ILicenseService licenseService)
    {
        _containerService = containerService;
        _settingsStore = settingsStore;
        _snapshotService = snapshotService;
        _licenseService = licenseService;
    }

    [HttpGet]
    public async Task<IActionResult> GetContainers(CancellationToken cancellationToken)
    {
        var containers = await _containerService.GetContainersAsync(includeAll: true, cancellationToken);
        var metrics = await _containerService.GetAllCurrentMetricsAsync(cancellationToken);

        return Ok(new
        {
            containers,
            metrics,
            isConnectedToDocker = _containerService.IsConnectedToDocker
        });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetContainerDetails(string id, CancellationToken cancellationToken)
    {
        var details = await _containerService.GetContainerDetailsAsync(id, cancellationToken);
        if (details == null) return NotFound(new { message = $"Container {id} not found." });

        var metrics = await _containerService.GetMetricsAsync(id, cancellationToken);
        return Ok(new { details, metrics });
    }

    [HttpPost("action")]
    public async Task<IActionResult> ExecuteAction([FromBody] ContainerActionRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ContainerId) || string.IsNullOrWhiteSpace(request.Action))
        {
            return BadRequest(new { message = "ContainerId and Action are required." });
        }

        var result = await _containerService.ExecuteActionAsync(request.ContainerId, request.Action, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("deploy")]
    public async Task<IActionResult> DeployContainer([FromBody] DeployContainerRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Image))
        {
            return BadRequest(new DeployContainerResult
            {
                Success = false,
                Message = "Image repository/tag is required."
            });
        }

        var result = await _containerService.DeployContainerAsync(request, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpGet("{id}/logs")]

    public async Task<IActionResult> GetLogs(string id, [FromQuery] int tail = 100, CancellationToken cancellationToken = default)
    {
        var logs = await _containerService.GetLogsAsync(id, tail, cancellationToken);
        return Ok(new { containerId = id, logs });
    }

    [HttpGet("{id}/metrics")]
    public async Task<IActionResult> GetMetrics(string id, CancellationToken cancellationToken)
    {
        var metrics = await _containerService.GetMetricsAsync(id, cancellationToken);
        if (metrics == null) return NotFound(new { message = $"Metrics for {id} not found." });
        return Ok(metrics);
    }

    [HttpGet("{id}/files")]
    public async Task<IActionResult> GetContainerFiles(
        string id, 
        [FromQuery] string path = "/", 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "ContainerId is required." });
        }

        try
        {
            var files = await _containerService.GetContainerFilesAsync(id, path, cancellationToken);
            return Ok(new
            {
                containerId = id,
                path = string.IsNullOrWhiteSpace(path) ? "/" : path,
                items = files
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("{id}/file/content")]
    public async Task<IActionResult> GetFileContent(
        string id,
        [FromQuery] string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(new { message = "ContainerId and path are required." });
        }

        var result = await _containerService.GetFileContentAsync(id, path, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpGet("{id}/file/download")]
    public async Task<IActionResult> DownloadFile(
        string id,
        [FromQuery] string path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path))
        {
            return BadRequest(new { message = "ContainerId and path are required." });
        }

        var (stream, fileName, size) = await _containerService.GetFileArchiveStreamAsync(id, path, cancellationToken);
        if (stream == null)
        {
            return NotFound(new { message = $"File '{path}' could not be downloaded." });
        }

        return File(stream, "application/octet-stream", fileName);
    }

    [HttpPost("{id}/exec")]
    public async Task<IActionResult> ExecCommand(
        string id,
        [FromBody] ContainerExecRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new ContainerExecResult
            {
                Success = false,
                ExitCode = -1,
                ErrorMessage = "Container ID is required."
            });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Command))
        {
            return BadRequest(new ContainerExecResult
            {
                Success = false,
                ExitCode = -1,
                ErrorMessage = "Command is required."
            });
        }

        var result = await _containerService.ExecCommandAsync(id, request, cancellationToken);
        return Ok(result);
    }

    [HttpGet("/api/system/status")]
    public async Task<IActionResult> GetSystemStatus(CancellationToken cancellationToken)
    {
        if (!_containerService.IsConnectedToDocker)
        {
            await _containerService.CheckConnectivityAsync(cancellationToken);
        }

        return Ok(new
        {
            isConnectedToDocker = _containerService.IsConnectedToDocker,
            engineType = _containerService.IsConnectedToDocker ? "Docker Engine (Native)" : "Disconnected",
            endpoint = _containerService.DockerUri.ToString(),
            lastError = _containerService.LastConnectionError
        });
    }

    [HttpPost("/api/system/retry-connection")]
    public async Task<IActionResult> RetryConnection(CancellationToken cancellationToken)
    {
        var isConnected = await _containerService.CheckConnectivityAsync(cancellationToken);
        return Ok(new
        {
            isConnectedToDocker = isConnected,
            engineType = isConnected ? "Docker Engine (Native)" : "Disconnected",
            endpoint = _containerService.DockerUri.ToString(),
            lastError = _containerService.LastConnectionError
        });
    }

    // ==========================================
    // Time Machine Retention & Auto-Snapshot API
    // ==========================================

    /// <summary>
    /// Gets global Time Machine retention and auto-snapshot configuration.
    /// </summary>
    [HttpGet("retention/settings")]
    public IActionResult GetGlobalRetentionSettings()
    {
        if (!_licenseService.IsTimeMachineEnabled())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Time Machine settings are not accessible in observer-only mode." });
        }

        var settings = _settingsStore.GetGlobalSettings();
        return Ok(settings);
    }

    /// <summary>
    /// Updates global Time Machine retention and auto-snapshot configuration.
    /// </summary>
    [HttpPut("retention/settings")]
    public IActionResult UpdateGlobalRetentionSettings([FromBody] TimeMachineRetentionSettings settings)
    {
        if (!_licenseService.IsTimeMachineEnabled())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Time Machine settings are not accessible in observer-only mode." });
        }

        if (settings == null)
        {
            return BadRequest(new { message = "Settings payload is required." });
        }

        _settingsStore.UpdateGlobalSettings(settings);
        return Ok(_settingsStore.GetGlobalSettings());
    }

    /// <summary>
    /// Gets the auto-snapshot override state and effective enabled status for a specific container.
    /// </summary>
    [HttpGet("{id}/retention/override")]
    [HttpGet("{id}/autosnapshot")]
    public IActionResult GetContainerOverride(string id)
    {
        if (!_licenseService.IsTimeMachineEnabled())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Time Machine settings are not accessible in observer-only mode." });
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "Container ID is required." });
        }

        var overrideState = _settingsStore.GetContainerOverride(id);
        var isEnabled = _settingsStore.IsAutoSnapshotEnabledForContainer(id);

        return Ok(new
        {
            containerId = id,
            @override = overrideState.ToString(),
            overrideState = (int)overrideState,
            isAutoSnapshotEnabled = isEnabled
        });
    }

    /// <summary>
    /// Updates the auto-snapshot override state (UseGlobalDefault, AlwaysOn, AlwaysOff) for a container.
    /// </summary>
    [HttpPut("{id}/retention/override")]
    [HttpPut("{id}/autosnapshot")]
    public IActionResult SetContainerOverride(string id, [FromBody] ContainerOverrideRequest request)
    {
        if (!_licenseService.IsTimeMachineEnabled())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Time Machine settings are not accessible in observer-only mode." });
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "Container ID is required." });
        }

        var state = request?.Override ?? ContainerAutoSnapshotOverride.UseGlobalDefault;
        if (request != null && !string.IsNullOrWhiteSpace(request.OverrideState) &&
            Enum.TryParse<ContainerAutoSnapshotOverride>(request.OverrideState, true, out var parsed))
        {
            state = parsed;
        }

        _settingsStore.SetContainerOverride(id, state);
        var isEnabled = _settingsStore.IsAutoSnapshotEnabledForContainer(id);

        return Ok(new
        {
            containerId = id,
            @override = state.ToString(),
            overrideState = (int)state,
            isAutoSnapshotEnabled = isEnabled
        });
    }

    /// <summary>
    /// Computes total snapshot storage consumed by a container and returns its latest snapshot timestamp.
    /// </summary>
    [HttpGet("{id}/snapshots/storage")]
    [HttpGet("{id}/retention/storage")]
    public async Task<IActionResult> GetContainerSnapshotStorage(string id, CancellationToken cancellationToken)
    {
        if (!_licenseService.IsTimeMachineEnabled())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Time Machine snapshots and storage telemetry are not accessible in observer-only mode." });
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "Container ID is required." });
        }

        var totalBytes = await _snapshotService.GetContainerSnapshotStorageBytesAsync(id, cancellationToken);
        var snapshots = await _snapshotService.GetSnapshotsAsync(id, cancellationToken);

        DateTime? lastSnapshotTimestamp = snapshots.Count > 0
            ? snapshots.Max(s => s.Timestamp)
            : null;

        return Ok(new
        {
            containerId = id,
            totalStorageBytes = totalBytes,
            totalStorageMB = Math.Round(totalBytes / (1024.0 * 1024.0), 2),
            snapshotCount = snapshots.Count,
            lastSnapshotTimestamp,
            lastAutoSnapshotTimestamp = lastSnapshotTimestamp
        });
    }

    /// <summary>
    /// Triggers retention pruning on demand for a specific container.
    /// </summary>
    [HttpPost("{id}/snapshots/prune")]
    [HttpPost("{id}/retention/prune")]
    public async Task<IActionResult> PruneContainerSnapshots(
        string id,
        [FromBody] TimeMachineRetentionSettings? customSettings,
        CancellationToken cancellationToken)
    {
        if (!_licenseService.IsTimeMachineEnabled())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Time Machine snapshot pruning is not accessible in observer-only mode." });
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { message = "Container ID is required." });
        }

        var result = await _snapshotService.PruneSnapshotsAsync(id, customSettings, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Triggers fleet-wide retention pruning on demand across all containers.
    /// </summary>
    [HttpPost("snapshots/prune")]
    [HttpPost("retention/prune")]
    public async Task<IActionResult> PruneAllSnapshots(
        [FromBody] TimeMachineRetentionSettings? customSettings,
        CancellationToken cancellationToken)
    {
        if (!_licenseService.IsTimeMachineEnabled())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Time Machine snapshot pruning is not accessible in observer-only mode." });
        }

        var result = await _snapshotService.PruneSnapshotsAsync(null, customSettings, cancellationToken);
        return Ok(result);
    }
}

public record ContainerOverrideRequest
{
    public ContainerAutoSnapshotOverride? Override { get; init; }
    public string? OverrideState { get; init; }
}
