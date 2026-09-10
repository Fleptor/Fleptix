namespace Fleptix.Core.Models;

/// <summary>
/// Real-time host machine hardware and operating system telemetry.
/// </summary>
public record HostSystemInfo
{
    // CPU telemetry
    public string CpuModelName { get; init; } = "Generic x86_64";
    public string CpuModelShort { get; init; } = "x86_64";
    public int CpuCores { get; init; } = Environment.ProcessorCount;
    public double LoadAvg1m { get; init; }
    public double LoadAvg5m { get; init; }
    public double LoadAvg15m { get; init; }

    // Memory telemetry
    public double TotalMemoryGb { get; init; }
    public double UsedMemoryGb { get; init; }
    public double AvailableMemoryGb { get; init; }
    public double MemoryUsagePercentage => TotalMemoryGb > 0 
        ? Math.Round((UsedMemoryGb / TotalMemoryGb) * 100.0, 1) 
        : 0.0;
    public string MemoryLabel { get; init; } = "Host Memory";

    // System profile
    public string OperatingSystem { get; init; } = "Linux";
    public string KernelVersion { get; init; } = "";
    public string Architecture { get; init; } = "";
}
