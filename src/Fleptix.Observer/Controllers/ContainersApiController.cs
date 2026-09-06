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
    private readonly ContainerServiceManager _manager;

    public ContainersApiController(
        IContainerService containerService,
        ContainerServiceManager manager)
    {
        _containerService = containerService;
        _manager = manager;
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

    [HttpGet("/api/system/status")]
    public IActionResult GetSystemStatus()
    {
        return Ok(new
        {
            isConnectedToDocker = _containerService.IsConnectedToDocker,
            forceDemoMode = _manager.ForceDemoMode,
            engineType = _containerService.IsConnectedToDocker ? "Docker Engine (Native)" : "Simulated Engine (Live Demo)"
        });
    }

    [HttpPost("/api/system/toggle-mode")]
    public IActionResult ToggleMode([FromBody] ToggleModeRequest? request)
    {
        if (request != null && request.ForceDemo.HasValue)
        {
            _manager.ForceDemoMode = request.ForceDemo.Value;
        }
        else
        {
            _manager.ForceDemoMode = !_manager.ForceDemoMode;
        }

        return Ok(new
        {
            isConnectedToDocker = _containerService.IsConnectedToDocker,
            forceDemoMode = _manager.ForceDemoMode,
            engineType = _containerService.IsConnectedToDocker ? "Docker Engine (Native)" : "Simulated Engine (Live Demo)"
        });
    }
}

public record ToggleModeRequest(bool? ForceDemo);
