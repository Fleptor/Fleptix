namespace Fleptix.Core.Models;

/// <summary>
/// Telemetry data point capturing resource utilization for Chart.js and SignalR.
/// </summary>
public record ContainerMetrics
{
    public string ContainerId { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public double CpuPercentage { get; init; }
    public ulong MemoryUsageBytes { get; init; }
    public ulong MemoryLimitBytes { get; init; }
    public double MemoryPercentage => MemoryLimitBytes > 0 
        ? Math.Round((double)MemoryUsageBytes / MemoryLimitBytes * 100.0, 2) 
        : 0.0;
    public double MemoryUsageMb => Math.Round((double)MemoryUsageBytes / (1024.0 * 1024.0), 2);
    public double MemoryLimitMb => Math.Round((double)MemoryLimitBytes / (1024.0 * 1024.0), 2);
    public ulong NetworkRxBytes { get; init; }
    public ulong NetworkTxBytes { get; init; }
}
