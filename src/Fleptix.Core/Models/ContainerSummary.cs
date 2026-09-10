namespace Fleptix.Core.Models;

/// <summary>
/// Lightweight model representing a container on the dashboard.
/// </summary>
public record ContainerSummary
{
    public string Id { get; init; } = string.Empty;
    public string ShortId => Id.Length > 12 ? Id[..12] : Id;
    public string Name { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty; // "running", "paused", "exited", "created"
    public string Status { get; init; } = string.Empty; // "Up 2 hours", "Exited (0) 5 mins ago"
    public DateTime Created { get; init; }
    public IReadOnlyList<PortMapping> PortMappings { get; init; } = [];
    public IReadOnlyList<string> Ports { get; init; } = [];
}
