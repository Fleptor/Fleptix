namespace Fleptix.Core.Models;

/// <summary>
/// Operational outcome of a container deployment operation.
/// </summary>
public record DeployContainerResult
{
    public bool Success { get; init; }
    public string ContainerId { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public IList<string> Logs { get; init; } = new List<string>();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
