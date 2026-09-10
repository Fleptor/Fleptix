namespace Fleptix.Core.Models;

/// <summary>
/// Host filesystem and Docker daemon storage breakdown.
/// </summary>
public record DockerStorageInfo
{
    public double TotalDiskGb { get; init; }
    public double UsedDiskGb { get; init; }
    public double FreeDiskGb { get; init; }
    public double DiskUsagePercentage => TotalDiskGb > 0 ? Math.Round((UsedDiskGb / TotalDiskGb) * 100.0, 1) : 0.0;
    public double ImagesSizeGb { get; init; }
    public double VolumesSizeGb { get; init; }
    public double ContainersSizeGb { get; init; }
    public string StorageDriver { get; init; } = "overlay2";
    public string FileSystemName { get; init; } = "ext4";
}
