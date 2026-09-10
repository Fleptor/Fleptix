namespace Fleptix.Core.Models;

/// <summary>
/// Represents a container network port mapping or published host binding.
/// </summary>
public record PortMapping
{
    public ushort HostPort { get; init; }
    public ushort ContainerPort { get; init; }
    public string Protocol { get; init; } = "tcp";
    public string HostIp { get; init; } = string.Empty;

    public bool IsMapped => HostPort > 0;
    public bool IsLinkable => IsMapped && Protocol.Equals("tcp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Scheme to use for browser navigation (detects SSL/TLS ports like 443 or 8443).
    /// </summary>
    public string Scheme => (ContainerPort == 443 || HostPort == 443 || HostPort == 8443) ? "https" : "http";

    /// <summary>
    /// Short display format e.g. "8080:80" or "80/tcp" if unmapped.
    /// </summary>
    public string DisplayText => HostPort > 0
        ? $"{HostPort}:{ContainerPort}"
        : $"{ContainerPort}/{Protocol}";

    /// <summary>
    /// Full display format including protocol e.g. "8080:80/tcp" or "80/tcp".
    /// </summary>
    public string FullText => HostPort > 0
        ? $"{HostPort}:{ContainerPort}/{Protocol}"
        : $"{ContainerPort}/{Protocol}";

    /// <summary>
    /// Host binding display e.g. "0.0.0.0:8080".
    /// </summary>
    public string HostBindingDisplay => HostPort > 0
        ? $"{(string.IsNullOrWhiteSpace(HostIp) ? "0.0.0.0" : HostIp)}:{HostPort}"
        : "-";

    /// <summary>
    /// Fallback direct URL using localhost.
    /// </summary>
    public string FallbackUrl => IsLinkable ? $"{Scheme}://localhost:{HostPort}" : string.Empty;
}
