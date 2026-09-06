namespace Fleptix.Core.Models;

/// <summary>
/// Specification for deploying and launching a container instance.
/// </summary>
public record DeployContainerRequest
{
    /// <summary>
    /// Image repository and tag to pull and run (e.g. "nginx:alpine", "redis:7.2-alpine").
    /// </summary>
    public string Image { get; init; } = string.Empty;

    /// <summary>
    /// Optional container name. If omitted, engine creates a random name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Port mapping strings in format "hostPort:containerPort[/protocol]" (e.g. "8080:80", "5432:5432/tcp").
    /// </summary>
    public IList<string> PortMappings { get; init; } = new List<string>();

    /// <summary>
    /// Environment variables as key-value pairs or "KEY=VALUE" strings.
    /// </summary>
    public IDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Volume bindings in format "source:destination[:mode]" (e.g. "/host/data:/app/data:rw").
    /// </summary>
    public IList<string> VolumeMounts { get; init; } = new List<string>();

    /// <summary>
    /// Container restart policy: "unless-stopped", "always", "on-failure", "no".
    /// </summary>
    public string RestartPolicy { get; init; } = "unless-stopped";

    /// <summary>
    /// Custom command override for container entrypoint.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Working directory inside container.
    /// </summary>
    public string? WorkingDir { get; init; }

    /// <summary>
    /// Key-value metadata labels.
    /// </summary>
    public IDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Network mode (e.g. "bridge", "host", or custom network name).
    /// </summary>
    public string NetworkMode { get; init; } = "bridge";

    /// <summary>
    /// Whether to start the container immediately after creation. Defaults to true.
    /// </summary>
    public bool AutoStart { get; init; } = true;

    /// <summary>
    /// Memory limit in Megabytes (0 for unlimited).
    /// </summary>
    public long MemoryLimitMb { get; init; } = 0;

    /// <summary>
    /// CPU limit in millicores (e.g. 1000 = 1 core, 500 = 0.5 core, 0 for unlimited).
    /// </summary>
    public long CpuLimitMillicores { get; init; } = 0;
}
