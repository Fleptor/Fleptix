namespace Fleptix.Core.Models;

/// <summary>
/// Detailed container configuration, environment variables, mounts, and network settings.
/// </summary>
public record ContainerDetails
{
    public ContainerSummary Summary { get; init; } = default!;
    public string ImageHash { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string WorkingDir { get; init; } = string.Empty;
    public string IPAddress { get; init; } = string.Empty;
    public string NetworkMode { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } 
        = new Dictionary<string, string>();
    public IReadOnlyList<string> Mounts { get; init; } = [];
    public IReadOnlyList<PortMapping> PortMappings => Summary?.PortMappings ?? [];
    public IReadOnlyList<string> PortBindings { get; init; } = [];
    public IReadOnlyDictionary<string, string> Labels { get; init; } 
        = new Dictionary<string, string>();
    public string RestartPolicy { get; init; } = "no";
    public DateTime? StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public string User { get; init; } = string.Empty;
    public bool Privileged { get; init; }
    public bool ReadonlyRootfs { get; init; }
    public string AppArmorProfile { get; init; } = string.Empty;
}
