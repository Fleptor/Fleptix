namespace Fleptix.Core.Models;

/// <summary>
/// DTO representing an action to perform on a container (start, stop, restart, pause, unpause).
/// </summary>
public record ContainerActionRequest
{
    public string ContainerId { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty; // "start", "stop", "restart", "pause", "unpause"
}

public record ContainerActionResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? NewState { get; init; }
    public string ContainerId { get; init; } = string.Empty;
}
