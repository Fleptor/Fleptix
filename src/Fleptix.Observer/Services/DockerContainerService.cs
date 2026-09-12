namespace Fleptix.Observer.Services;

using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Docker.DotNet;
using Docker.DotNet.Models;
using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;

/// <summary>
/// Live Docker daemon service powered by Docker.DotNet.
/// Connects to local Docker engine on Windows named pipe or Unix socket.
/// </summary>
public class DockerContainerService : IContainerService
{
    private volatile bool _isConnectedToDocker;
    private volatile string? _lastConnectionError;

    public bool IsConnectedToDocker => _isConnectedToDocker;
    public string? LastConnectionError => _lastConnectionError;
    public Uri DockerUri { get; }

    private readonly DockerClient _client;
    private readonly ILogger<DockerContainerService> _logger;

    public DockerContainerService(ILogger<DockerContainerService> logger)
    {
        _logger = logger;

        string? dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            DockerUri = new Uri(dockerHost);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            DockerUri = new Uri("npipe://./pipe/docker_engine");
        }
        else
        {
            DockerUri = new Uri("unix:///var/run/docker.sock");
        }

        _logger.LogInformation("DockerContainerService configured with Docker endpoint: {DockerUri}", DockerUri);
        _client = new DockerClientConfiguration(DockerUri).CreateClient();
    }

    /// <summary>
    /// Non-blocking asynchronous connectivity probe with a 1000ms timeout.
    /// </summary>
    public async Task<bool> CheckConnectivityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(1000));

            await _client.System.PingAsync(cts.Token);
            _isConnectedToDocker = true;
            _lastConnectionError = null;
            return true;
        }
        catch (Exception ex)
        {
            _isConnectedToDocker = false;
            _lastConnectionError = ex.Message;
            _logger.LogDebug(ex, "Docker connectivity check probe failed at {DockerUri}: {Error}", DockerUri, ex.Message);
            return false;
        }
    }

    public async Task<IReadOnlyList<ContainerSummary>> GetContainersAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var containers = await _client.Containers.ListContainersAsync(
                new ContainersListParameters { All = includeAll }, 
                cancellationToken);

            _isConnectedToDocker = true;
            _lastConnectionError = null;

            return containers.Select(c =>
            {
                var portMappings = (c.Ports ?? Enumerable.Empty<Port>())
                    .Where(p => p.PrivatePort > 0)
                    .GroupBy(p => (p.PublicPort, p.PrivatePort, (p.Type ?? "tcp").ToLowerInvariant()))
                    .Select(g =>
                    {
                        var first = g.First();
                        return new PortMapping
                        {
                            HostPort = first.PublicPort,
                            ContainerPort = first.PrivatePort,
                            Protocol = (first.Type ?? "tcp").ToLowerInvariant(),
                            HostIp = first.IP ?? string.Empty
                        };
                    })
                    .OrderBy(p => p.HostPort > 0 ? 0 : 1)
                    .ThenBy(p => p.HostPort)
                    .ThenBy(p => p.ContainerPort)
                    .ToList();

                return new ContainerSummary
                {
                    Id = c.ID,
                    Name = c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID[..12],
                    Image = c.Image,
                    State = c.State?.ToLowerInvariant() ?? "unknown",
                    Status = c.Status,
                    Created = c.Created,
                    PortMappings = portMappings,
                    Ports = portMappings.Select(p => p.FullText).ToList()
                };
            }).ToList();
        }
        catch (Exception ex)
        {
            _isConnectedToDocker = false;
            _lastConnectionError = ex.Message;
            _logger.LogError(ex, "Failed to list containers from Docker engine.");
            return Array.Empty<ContainerSummary>();
        }
    }

    public async Task<ContainerDetails?> GetContainerDetailsAsync(string containerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var inspect = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
            if (inspect == null) return null;

            var detailPortMappings = new List<PortMapping>();
            var portsSource = (inspect.NetworkSettings?.Ports != null && inspect.NetworkSettings.Ports.Count > 0)
                ? inspect.NetworkSettings.Ports
                : inspect.HostConfig?.PortBindings;

            if (portsSource != null)
            {
                foreach (var kvp in portsSource)
                {
                    var keyParts = kvp.Key.Split('/');
                    var containerPort = ushort.TryParse(keyParts[0], out var cp) ? cp : (ushort)0;
                    var protocol = keyParts.Length > 1 ? keyParts[1].ToLowerInvariant() : "tcp";

                    if (kvp.Value != null && kvp.Value.Count > 0)
                    {
                        foreach (var binding in kvp.Value)
                        {
                            var hostPort = ushort.TryParse(binding.HostPort, out var hp) ? hp : (ushort)0;
                            detailPortMappings.Add(new PortMapping
                            {
                                HostPort = hostPort,
                                ContainerPort = containerPort,
                                Protocol = protocol,
                                HostIp = binding.HostIP ?? string.Empty
                            });
                        }
                    }
                    else
                    {
                        detailPortMappings.Add(new PortMapping
                        {
                            HostPort = 0,
                            ContainerPort = containerPort,
                            Protocol = protocol,
                            HostIp = string.Empty
                        });
                    }
                }
            }

            detailPortMappings = detailPortMappings
                .GroupBy(p => (p.HostPort, p.ContainerPort, p.Protocol))
                .Select(g =>
                {
                    var first = g.First();
                    var ips = g.Select(x => x.HostIp).Where(ip => !string.IsNullOrWhiteSpace(ip)).Distinct().ToList();
                    var combinedIp = ips.Count switch
                    {
                        0 => string.Empty,
                        1 => ips[0],
                        _ => string.Join(", ", ips)
                    };
                    return new PortMapping
                    {
                        HostPort = first.HostPort,
                        ContainerPort = first.ContainerPort,
                        Protocol = first.Protocol,
                        HostIp = combinedIp
                    };
                })
                .OrderBy(p => p.HostPort > 0 ? 0 : 1)
                .ThenBy(p => p.HostPort)
                .ThenBy(p => p.ContainerPort)
                .ToList();

            var summary = new ContainerSummary
            {
                Id = inspect.ID,
                Name = inspect.Name?.TrimStart('/') ?? inspect.ID[..12],
                Image = inspect.Config?.Image ?? "unknown",
                State = inspect.State?.Status?.ToLowerInvariant() ?? "unknown",
                Status = inspect.State?.Status ?? "unknown",
                Created = inspect.Created,
                PortMappings = detailPortMappings,
                Ports = detailPortMappings.Select(p => p.FullText).ToList()
            };

            var env = inspect.Config?.Env?
                .Select(e => e.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1]) ?? new Dictionary<string, string>();

            var mounts = inspect.Mounts?.Select(m => $"{m.Source}:{m.Destination}:{(m.RW ? "rw" : "ro")}").ToList() ?? [];

            var ip = inspect.NetworkSettings?.IPAddress;
            if (string.IsNullOrEmpty(ip) && inspect.NetworkSettings?.Networks?.Count > 0)
            {
                ip = inspect.NetworkSettings.Networks.Values.FirstOrDefault()?.IPAddress ?? "";
            }

            return new ContainerDetails
            {
                Summary = summary,
                ImageHash = inspect.Image ?? "",
                Command = string.Join(" ", inspect.Config?.Cmd ?? Array.Empty<string>()),
                WorkingDir = inspect.Config?.WorkingDir ?? "/",
                IPAddress = ip ?? "",
                NetworkMode = inspect.HostConfig?.NetworkMode ?? "bridge",
                EnvironmentVariables = env,
                Mounts = mounts,
                PortBindings = detailPortMappings.Select(p => p.FullText).ToList(),
                Labels = inspect.Config?.Labels != null ? new Dictionary<string, string>(inspect.Config.Labels) : new Dictionary<string, string>(),
                RestartPolicy = inspect.HostConfig?.RestartPolicy != null ? inspect.HostConfig.RestartPolicy.Name.ToString() : "no",
                StartedAt = DateTime.TryParse(inspect.State?.StartedAt, out var s) ? s : null,
                FinishedAt = DateTime.TryParse(inspect.State?.FinishedAt, out var f) ? f : null,
                User = inspect.Config?.User ?? string.Empty,
                Privileged = inspect.HostConfig?.Privileged ?? false,
                ReadonlyRootfs = inspect.HostConfig?.ReadonlyRootfs ?? false,
                AppArmorProfile = inspect.AppArmorProfile ?? string.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to inspect container {ContainerId}", containerId);
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<string, ContainerMetrics>> GetAllCurrentMetricsAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, ContainerMetrics>();
        try
        {
            var containers = await GetContainersAsync(includeAll: false, cancellationToken);
            foreach (var container in containers)
            {
                var metric = await GetMetricsAsync(container.Id, cancellationToken);
                if (metric != null)
                {
                    result[container.Id] = metric;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting all metrics");
        }
        return result;
    }

    public async Task<ContainerMetrics?> GetMetricsAsync(string containerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var progress = new SingleStatProgress();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            await _client.Containers.GetContainerStatsAsync(
                containerId,
                new ContainerStatsParameters { Stream = false, OneShot = true },
                progress,
                cts.Token);

            if (progress.LastStats != null)
            {
                return ConvertStatsToMetrics(containerId, progress.LastStats);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get one-shot stats for {ContainerId}", containerId);
        }
        return null;
    }

    private class SingleStatProgress : IProgress<ContainerStatsResponse>
    {
        public ContainerStatsResponse? LastStats { get; private set; }
        public void Report(ContainerStatsResponse value) => LastStats = value;
    }

    private ContainerMetrics ConvertStatsToMetrics(string containerId, ContainerStatsResponse stats)
    {
        var cpuDelta = (double)(stats.CPUStats.CPUUsage.TotalUsage - stats.PreCPUStats.CPUUsage.TotalUsage);
        var systemDelta = (double)(stats.CPUStats.SystemUsage - stats.PreCPUStats.SystemUsage);
        var cpuPercent = 0.0;

        if (systemDelta > 0 && cpuDelta > 0)
        {
            var onlineCpus = stats.CPUStats.OnlineCPUs;
            if (onlineCpus == 0 && stats.CPUStats.CPUUsage.PercpuUsage != null)
            {
                onlineCpus = (uint)stats.CPUStats.CPUUsage.PercpuUsage.Count;
            }
            if (onlineCpus == 0) onlineCpus = 1;

            cpuPercent = Math.Round((cpuDelta / systemDelta) * onlineCpus * 100.0, 2);
        }

        var memUsage = stats.MemoryStats.Usage;
        var memLimit = stats.MemoryStats.Limit;

        ulong rx = 0;
        ulong tx = 0;
        if (stats.Networks != null)
        {
            foreach (var net in stats.Networks.Values)
            {
                rx += net.RxBytes;
                tx += net.TxBytes;
            }
        }

        ulong blockRead = 0;
        ulong blockWrite = 0;
        if (stats.BlkioStats?.IoServiceBytesRecursive != null)
        {
            foreach (var entry in stats.BlkioStats.IoServiceBytesRecursive)
            {
                if (string.Equals(entry.Op, "Read", StringComparison.OrdinalIgnoreCase))
                {
                    blockRead += entry.Value;
                }
                else if (string.Equals(entry.Op, "Write", StringComparison.OrdinalIgnoreCase))
                {
                    blockWrite += entry.Value;
                }
            }
        }

        return new ContainerMetrics
        {
            ContainerId = containerId,
            Timestamp = DateTime.UtcNow,
            CpuPercentage = cpuPercent,
            MemoryUsageBytes = memUsage,
            MemoryLimitBytes = memLimit,
            NetworkRxBytes = rx,
            NetworkTxBytes = tx,
            BlockReadBytes = blockRead,
            BlockWriteBytes = blockWrite
        };
    }

    public async Task<DockerStorageInfo> GetStorageInfoAsync(CancellationToken cancellationToken = default)
    {
        DriveInfo? drive = null;
        try
        {
            drive = new DriveInfo("/");
        }
        catch
        {
            try
            {
                drive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady);
            }
            catch { }
        }

        double totalGb = drive != null ? Math.Round((double)drive.TotalSize / (1024.0 * 1024.0 * 1024.0), 1) : 100.0;
        double freeGb = drive != null ? Math.Round((double)drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 1) : 50.0;
        double usedGb = Math.Max(0.0, Math.Round(totalGb - freeGb, 1));

        string driver = "overlay2";
        string fsName = drive?.DriveFormat ?? "ext4";

        try
        {
            var sysInfo = await _client.System.GetSystemInfoAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(sysInfo.Driver))
            {
                driver = sysInfo.Driver;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not query Docker system info");
        }

        double imagesGb = 0.0;
        try
        {
            var images = await _client.Images.ListImagesAsync(new ImagesListParameters { All = true }, cancellationToken);
            long totalImgBytes = images.Sum(i => i.Size);
            imagesGb = Math.Round((double)totalImgBytes / (1024.0 * 1024.0 * 1024.0), 1);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not query images list for storage calculation");
        }

        double containersGb = 0.0;
        try
        {
            var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true, Size = true }, cancellationToken);
            long totalContainerBytes = containers.Sum(c => c.SizeRw);
            containersGb = Math.Round((double)totalContainerBytes / (1024.0 * 1024.0 * 1024.0), 2);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not query containers list for storage calculation");
        }

        double volumesGb = 0.0;
        try
        {
            var volumes = await _client.Volumes.ListAsync(cancellationToken);
            if (volumes?.Volumes != null)
            {
                long totalVolBytes = volumes.Volumes.Sum(v => v.UsageData?.Size ?? 0);
                volumesGb = Math.Round((double)totalVolBytes / (1024.0 * 1024.0 * 1024.0), 1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not query volumes list for storage calculation");
        }

        return new DockerStorageInfo
        {
            TotalDiskGb = totalGb,
            UsedDiskGb = usedGb,
            FreeDiskGb = freeGb,
            ImagesSizeGb = imagesGb,
            VolumesSizeGb = volumesGb,
            ContainersSizeGb = containersGb,
            StorageDriver = driver,
            FileSystemName = fsName
        };
    }

    public async Task<HostSystemInfo> GetHostSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        int cpuCores = Environment.ProcessorCount;
        string cpuModel = "Generic x86_64";
        string cpuShort = "x86_64";
        double load1 = 0.0, load5 = 0.0, load15 = 0.0;

        // 1. CPU detection from /proc/cpuinfo
        try
        {
            if (File.Exists("/proc/cpuinfo"))
            {
                var lines = await File.ReadAllLinesAsync("/proc/cpuinfo", cancellationToken);
                var modelLine = lines.FirstOrDefault(l => l.StartsWith("model name", StringComparison.OrdinalIgnoreCase));
                if (modelLine != null)
                {
                    var parts = modelLine.Split(':', 2);
                    if (parts.Length > 1)
                    {
                        cpuModel = parts[1].Trim();
                        // Shorten model name cleanly (e.g. "AMD Ryzen 5 2600X Six-Core Processor" -> "AMD Ryzen 5 2600X")
                        cpuShort = Regex.Replace(cpuModel, @"\s*\(R\)\s*|\s*\(TM\)\s*|\s*Processor\s*|\s*(Six|Eight|Quad|Ten|Twelve|Sixteen)-Core\s*", " ", RegexOptions.IgnoreCase);
                        cpuShort = Regex.Replace(cpuShort, @"\s*CPU\s*", " ", RegexOptions.IgnoreCase);
                        cpuShort = string.Join(" ", cpuShort.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse /proc/cpuinfo");
        }

        // 2. Load average from /proc/loadavg
        try
        {
            if (File.Exists("/proc/loadavg"))
            {
                var loadContent = await File.ReadAllTextAsync("/proc/loadavg", cancellationToken);
                var tokens = loadContent.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 3)
                {
                    double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out load1);
                    double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out load5);
                    double.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out load15);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse /proc/loadavg");
        }

        // 3. Memory detection from /proc/meminfo
        double totalMemGb = 0.0;
        double availMemGb = 0.0;
        try
        {
            if (File.Exists("/proc/meminfo"))
            {
                var lines = await File.ReadAllLinesAsync("/proc/meminfo", cancellationToken);
                long memTotalKb = 0;
                long memAvailKb = 0;
                long memFreeKb = 0;
                long buffersKb = 0;
                long cachedKb = 0;

                foreach (var line in lines)
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length != 2) continue;
                    var key = parts[0].Trim();
                    var valStr = parts[1].Replace("kB", "").Trim();
                    if (!long.TryParse(valStr, out var valKb)) continue;

                    if (key.Equals("MemTotal", StringComparison.OrdinalIgnoreCase)) memTotalKb = valKb;
                    else if (key.Equals("MemAvailable", StringComparison.OrdinalIgnoreCase)) memAvailKb = valKb;
                    else if (key.Equals("MemFree", StringComparison.OrdinalIgnoreCase)) memFreeKb = valKb;
                    else if (key.Equals("Buffers", StringComparison.OrdinalIgnoreCase)) buffersKb = valKb;
                    else if (key.Equals("Cached", StringComparison.OrdinalIgnoreCase)) cachedKb = valKb;
                }

                if (memAvailKb == 0 && memFreeKb > 0)
                {
                    memAvailKb = memFreeKb + buffersKb + cachedKb;
                }

                totalMemGb = Math.Round((double)memTotalKb / (1024.0 * 1024.0), 1);
                availMemGb = Math.Round((double)memAvailKb / (1024.0 * 1024.0), 1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse /proc/meminfo");
        }

        // 4. Docker system info fallback / enrichment
        string osName = "Linux";
        string kernel = "";
        string arch = RuntimeInformation.ProcessArchitecture.ToString();

        try
        {
            var sysInfo = await _client.System.GetSystemInfoAsync(cancellationToken);
            if (sysInfo != null)
            {
                if (sysInfo.NCPU > 0) cpuCores = (int)sysInfo.NCPU;
                if (!string.IsNullOrWhiteSpace(sysInfo.OperatingSystem)) osName = sysInfo.OperatingSystem;
                if (!string.IsNullOrWhiteSpace(sysInfo.KernelVersion)) kernel = sysInfo.KernelVersion;
                if (!string.IsNullOrWhiteSpace(sysInfo.Architecture)) arch = sysInfo.Architecture;

                if (totalMemGb <= 0 && sysInfo.MemTotal > 0)
                {
                    totalMemGb = Math.Round((double)sysInfo.MemTotal / (1024.0 * 1024.0 * 1024.0), 1);
                    availMemGb = Math.Round(totalMemGb * 0.5, 1);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not query Docker system info for host stats");
        }

        if (totalMemGb <= 0)
        {
            var gcMem = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            totalMemGb = gcMem > 0 ? Math.Round((double)gcMem / (1024.0 * 1024.0 * 1024.0), 1) : 16.0;
            availMemGb = Math.Round(totalMemGb * 0.5, 1);
        }

        double usedMemGb = Math.Max(0.0, Math.Round(totalMemGb - availMemGb, 1));

        return new HostSystemInfo
        {
            CpuModelName = cpuModel,
            CpuModelShort = cpuShort,
            CpuCores = cpuCores,
            LoadAvg1m = Math.Round(load1, 2),
            LoadAvg5m = Math.Round(load5, 2),
            LoadAvg15m = Math.Round(load15, 2),
            TotalMemoryGb = totalMemGb,
            UsedMemoryGb = usedMemGb,
            AvailableMemoryGb = availMemGb,
            MemoryLabel = !string.IsNullOrWhiteSpace(osName) ? osName : "Host RAM",
            OperatingSystem = osName,
            KernelVersion = kernel,
            Architecture = arch
        };
    }

    public async IAsyncEnumerable<ContainerMetrics> GetMetricsStreamAsync(
        string containerId, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var metric = await GetMetricsAsync(containerId, cancellationToken);
            if (metric != null) yield return metric;
            await Task.Delay(1000, cancellationToken);
        }
    }

    public async Task<ContainerActionResult> ExecuteActionAsync(string containerId, string action, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (action.ToLowerInvariant())
            {
                case "start":
                    await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
                    return new ContainerActionResult { Success = true, ContainerId = containerId, NewState = "running", Message = "Container started successfully." };

                case "stop":
                    await _client.Containers.StopContainerAsync(containerId, new ContainerStopParameters { WaitBeforeKillSeconds = 10 }, cancellationToken);
                    return new ContainerActionResult { Success = true, ContainerId = containerId, NewState = "exited", Message = "Container stopped successfully." };

                case "restart":
                    await _client.Containers.RestartContainerAsync(containerId, new ContainerRestartParameters { WaitBeforeKillSeconds = 10 }, cancellationToken);
                    return new ContainerActionResult { Success = true, ContainerId = containerId, NewState = "running", Message = "Container restarted successfully." };

                case "pause":
                    await _client.Containers.PauseContainerAsync(containerId, cancellationToken);
                    return new ContainerActionResult { Success = true, ContainerId = containerId, NewState = "paused", Message = "Container paused." };

                case "unpause":
                    await _client.Containers.UnpauseContainerAsync(containerId, cancellationToken);
                    return new ContainerActionResult { Success = true, ContainerId = containerId, NewState = "running", Message = "Container unpaused." };

                default:
                    return new ContainerActionResult { Success = false, ContainerId = containerId, Message = $"Unsupported action '{action}'." };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute {Action} on {ContainerId}", action, containerId);
            return new ContainerActionResult { Success = false, ContainerId = containerId, Message = ex.Message };
        }
    }

    public async Task<DeployContainerResult> DeployContainerAsync(DeployContainerRequest request, CancellationToken cancellationToken = default)
    {
        var deployLogs = new List<string>();
        var timestamp = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(request.Image))
        {
            return new DeployContainerResult
            {
                Success = false,
                Message = "Image name is required for deployment.",
                Logs = new[] { $"[{timestamp:HH:mm:ss}] [ERROR] Image name cannot be empty." }
            };
        }

        var rawImage = request.Image.Trim();
        var containerName = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();

        try
        {
            // 1. Parse Image & Tag
            int lastColon = rawImage.LastIndexOf(':');
            int lastSlash = rawImage.LastIndexOf('/');
            string fromImage;
            string tag;

            if (lastColon > lastSlash && lastColon != -1)
            {
                fromImage = rawImage[..lastColon];
                tag = rawImage[(lastColon + 1)..];
            }
            else
            {
                fromImage = rawImage;
                tag = "latest";
            }

            deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] [STEP 1/4] Pulling image '{fromImage}:{tag}' from registry...");

            var pullProgress = new Progress<JSONMessage>(msg =>
            {
                if (!string.IsNullOrEmpty(msg.ErrorMessage))
                {
                    deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] [PULL ERR] {msg.ErrorMessage}");
                }
                else if (!string.IsNullOrEmpty(msg.Status))
                {
                    var idPart = !string.IsNullOrEmpty(msg.ID) ? $"[{msg.ID}] " : "";
                    var progPart = !string.IsNullOrEmpty(msg.ProgressMessage) ? $" {msg.ProgressMessage}" : "";
                    deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] [PULL] {idPart}{msg.Status}{progPart}");
                }
            });

            try
            {
                await _client.Images.CreateImageAsync(
                    new ImagesCreateParameters
                    {
                        FromImage = fromImage,
                        Tag = tag
                    },
                    null,
                    pullProgress,
                    cancellationToken);

                deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Image pull completed successfully.");
            }
            catch (Exception pullEx)
            {
                _logger.LogWarning(pullEx, "Failed pulling image {Image}, checking local cache", rawImage);
                deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] [NOTICE] Registry pull notice: {pullEx.Message} (proceeding with local image if available)");
            }

            // 2. Parse Port Mappings
            var exposedPorts = new Dictionary<string, EmptyStruct>();
            var portBindings = new Dictionary<string, IList<PortBinding>>();

            if (request.PortMappings != null)
            {
                foreach (var mapping in request.PortMappings.Where(p => !string.IsNullOrWhiteSpace(p)))
                {
                    var raw = mapping.Trim();
                    string hostIp = "";
                    string hostPort = "";
                    string containerPort = "";
                    string protocol = "tcp";

                    if (raw.Contains('/'))
                    {
                        var protoParts = raw.Split('/');
                        raw = protoParts[0];
                        protocol = protoParts[1].ToLowerInvariant();
                    }

                    var colonParts = raw.Split(':');
                    if (colonParts.Length == 1)
                    {
                        containerPort = colonParts[0];
                    }
                    else if (colonParts.Length == 2)
                    {
                        hostPort = colonParts[0];
                        containerPort = colonParts[1];
                    }
                    else if (colonParts.Length == 3)
                    {
                        hostIp = colonParts[0];
                        hostPort = colonParts[1];
                        containerPort = colonParts[2];
                    }

                    if (!string.IsNullOrEmpty(containerPort))
                    {
                        var portKey = $"{containerPort}/{protocol}";
                        exposedPorts[portKey] = default;

                        if (!portBindings.TryGetValue(portKey, out var bindingsList))
                        {
                            bindingsList = new List<PortBinding>();
                            portBindings[portKey] = bindingsList;
                        }

                        bindingsList.Add(new PortBinding
                        {
                            HostIP = string.IsNullOrWhiteSpace(hostIp) ? "" : hostIp,
                            HostPort = hostPort
                        });

                        deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Configured port mapping: host {(!string.IsNullOrEmpty(hostPort) ? hostPort : "dynamic")} -> container {portKey}");
                    }
                }
            }

            // 3. Environment Variables
            var envList = new List<string>();
            if (request.EnvironmentVariables != null)
            {
                foreach (var (k, v) in request.EnvironmentVariables)
                {
                    if (!string.IsNullOrWhiteSpace(k))
                    {
                        envList.Add($"{k.Trim()}={v?.Trim()}");
                    }
                }
            }

            // 4. Volume Mounts & Binds
            var binds = new List<string>();
            if (request.VolumeMounts != null)
            {
                foreach (var m in request.VolumeMounts.Where(v => !string.IsNullOrWhiteSpace(v)))
                {
                    binds.Add(m.Trim());
                    deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Configured volume bind: {m.Trim()}");
                }
            }

            // 5. Restart Policy
            RestartPolicy restartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped };
            switch (request.RestartPolicy?.ToLowerInvariant())
            {
                case "always":
                    restartPolicy = new RestartPolicy { Name = RestartPolicyKind.Always };
                    break;
                case "on-failure":
                    restartPolicy = new RestartPolicy { Name = RestartPolicyKind.OnFailure, MaximumRetryCount = 5 };
                    break;
                case "no":
                case "never":
                    restartPolicy = new RestartPolicy { Name = RestartPolicyKind.No };
                    break;
                default:
                    restartPolicy = new RestartPolicy { Name = RestartPolicyKind.UnlessStopped };
                    break;
            }

            // 6. Host Configuration
            var hostConfig = new HostConfig
            {
                PortBindings = portBindings.Count > 0 ? portBindings : null,
                Binds = binds.Count > 0 ? binds : null,
                RestartPolicy = restartPolicy,
                NetworkMode = string.IsNullOrWhiteSpace(request.NetworkMode) ? "bridge" : request.NetworkMode
            };

            if (request.MemoryLimitMb > 0)
            {
                hostConfig.Memory = request.MemoryLimitMb * 1024 * 1024;
            }
            if (request.CpuLimitMillicores > 0)
            {
                hostConfig.NanoCPUs = request.CpuLimitMillicores * 1_000_000;
            }

            // 7. Create Container Instance
            deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] [STEP 2/4] Creating container with image '{rawImage}'...");

            var createParams = new CreateContainerParameters
            {
                Image = rawImage,
                Name = containerName,
                Env = envList.Count > 0 ? envList : null,
                ExposedPorts = exposedPorts.Count > 0 ? exposedPorts : null,
                HostConfig = hostConfig,
                Labels = request.Labels?.Count > 0 ? new Dictionary<string, string>(request.Labels) : null
            };

            if (!string.IsNullOrWhiteSpace(request.Command))
            {
                createParams.Cmd = request.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            }
            if (!string.IsNullOrWhiteSpace(request.WorkingDir))
            {
                createParams.WorkingDir = request.WorkingDir;
            }

            var created = await _client.Containers.CreateContainerAsync(createParams, cancellationToken);
            var containerId = created.ID;
            deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] [STEP 3/4] Container created successfully: {containerId[..12]}");

            // 8. Auto-Start Container
            if (request.AutoStart)
            {
                deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] [STEP 4/4] Starting container {containerId[..12]}...");
                await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
                deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Container is active and running.");
            }
            else
            {
                deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Container created in stopped/created state (AutoStart=false).");
            }

            return new DeployContainerResult
            {
                Success = true,
                ContainerId = containerId,
                ContainerName = containerName ?? containerId[..12],
                Message = $"Container '{containerName ?? containerId[..12]}' deployed successfully.",
                Logs = deployLogs
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy container with image {Image}", rawImage);
            deployLogs.Add($"[{DateTime.UtcNow:HH:mm:ss}] [FATAL ERROR] Deployment failed: {ex.Message}");

            return new DeployContainerResult
            {
                Success = false,
                ContainerName = containerName ?? "unknown",
                Message = ex.Message,
                Logs = deployLogs
            };
        }
    }


    public async Task<IReadOnlyList<string>> GetLogsAsync(string containerId, int tailLines = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var inspect = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
            var isTty = inspect?.Config?.Tty ?? false;

            using var stream = await _client.Containers.GetContainerLogsAsync(
                containerId,
                tty: isTty,
                new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Tail = tailLines.ToString(),
                    Timestamps = true
                },
                cancellationToken);

            List<string> combined;
            if (isTty)
            {
                using var outputStream = new MemoryStream();
                await stream.CopyOutputToAsync(Stream.Null, outputStream, Stream.Null, cancellationToken);
                outputStream.Position = 0;
                using var reader = new StreamReader(outputStream);
                var raw = await reader.ReadToEndAsync(cancellationToken);
                combined = raw
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToList();
            }
            else
            {
                var (stdout, stderr) = await stream.ReadOutputToEndAsync(cancellationToken);
                combined = (stdout + "\n" + stderr)
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l))
                    .ToList();
            }

            return combined;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read logs for container {ContainerId}", containerId);
            return new[] { $"[Error reading logs: {ex.Message}]" };
        }
    }

    /// <summary>
    /// Browses container filesystem at the specified path using ExecCommandAsync running "ls -la <path>"
    /// and parses output for name, size, type, mode, and symlinks.
    /// </summary>
    public async Task<IReadOnlyList<ContainerFileSystemItem>> GetContainerFilesAsync(
        string containerId,
        string path = "/",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) path = "/";
        path = path.Trim();
        if (!path.StartsWith('/')) path = "/" + path;

        try
        {
            string escapedPath = path.Replace("\"", "\\\"");
            var execResult = await ExecCommandAsync(
                containerId,
                new ContainerExecRequest
                {
                    Command = $"ls -la \"{escapedPath}\"",
                    Shell = "/bin/sh"
                },
                cancellationToken);

            if (!execResult.Success && !string.IsNullOrWhiteSpace(execResult.Stderr))
            {
                _logger.LogWarning("ls -la failed for {ContainerId} at {Path}: {Error}", containerId, path, execResult.Stderr);
                return Array.Empty<ContainerFileSystemItem>();
            }

            return ParseLsOutput(execResult.Stdout, path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to browse container filesystem via Exec for {ContainerId} at {Path}", containerId, path);
            return Array.Empty<ContainerFileSystemItem>();
        }
    }

    /// <summary>
    /// Parses Unix ls -la command output into structured ContainerFileSystemItem list.
    /// </summary>
    public static List<ContainerFileSystemItem> ParseLsOutput(string stdout, string parentPath)
    {
        var items = new List<ContainerFileSystemItem>();
        if (string.IsNullOrWhiteSpace(stdout)) return items;

        string normParent = (parentPath ?? "/").Trim();
        if (!normParent.StartsWith('/')) normParent = "/" + normParent;
        if (normParent.Length > 1 && normParent.EndsWith('/')) normParent = normParent.TrimEnd('/');

        var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("total ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 8)
            {
                continue;
            }

            string mode = tokens[0];
            if (mode.Length < 10)
            {
                continue;
            }

            char typeChar = mode[0];
            bool isDir = typeChar == 'd';
            string type = typeChar switch
            {
                'd' => "directory",
                'l' => "symlink",
                'c' => "char",
                'b' => "block",
                'p' => "fifo",
                's' => "socket",
                _ => "file"
            };

            long size = 0;
            long.TryParse(tokens[4], out size);

            string fullNameStr = string.Join(" ", tokens.Skip(8));
            if (string.IsNullOrEmpty(fullNameStr))
            {
                continue;
            }

            string name = fullNameStr;
            string? linkTarget = null;
            if (type == "symlink" && fullNameStr.Contains(" -> "))
            {
                var parts = fullNameStr.Split(new[] { " -> " }, 2, StringSplitOptions.None);
                name = parts[0].Trim();
                linkTarget = parts[1].Trim();
            }

            if (name == "." || name == "..")
            {
                continue;
            }

            string fullPath = normParent == "/" ? $"/{name}" : $"{normParent}/{name}";

            DateTime? modifiedTime = null;
            try
            {
                string dateStr = $"{tokens[5]} {tokens[6]} {tokens[7]}";
                if (DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt))
                {
                    modifiedTime = dt;
                }
            }
            catch
            {
                // Ignore date parsing failure
            }

            items.Add(new ContainerFileSystemItem
            {
                Name = name,
                Path = fullPath,
                IsDirectory = isDir,
                Size = isDir ? 0 : size,
                Type = type,
                Mode = mode,
                ModifiedTime = modifiedTime,
                LinkTarget = linkTarget
            });
        }

        return items
            .OrderByDescending(x => x.IsDirectory)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Retrieves file content and metadata for viewing a specific file using GetArchiveFromContainerAsync.
    /// </summary>
    public async Task<ContainerFileContentResult> GetFileContentAsync(
        string containerId,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerId) || string.IsNullOrWhiteSpace(path))
        {
            return new ContainerFileContentResult
            {
                Success = false,
                ErrorMessage = "ContainerId and path are required."
            };
        }

        path = path.Trim();
        if (!path.StartsWith('/')) path = "/" + path;
        string fileName = Path.GetFileName(path);

        try
        {
            var response = await _client.Containers.GetArchiveFromContainerAsync(
                containerId,
                new GetArchiveFromContainerParameters { Path = path },
                statOnly: false,
                cancellationToken);

            if (response.Stream == null)
            {
                return new ContainerFileContentResult
                {
                    Success = false,
                    ContainerId = containerId,
                    Path = path,
                    Name = fileName,
                    ErrorMessage = "File stream returned null from Docker engine."
                };
            }

            using var stream = response.Stream;
            using var tarReader = new TarReader(stream);

            TarEntry? entry = tarReader.GetNextEntry();
            if (entry == null)
            {
                return new ContainerFileContentResult
                {
                    Success = false,
                    ContainerId = containerId,
                    Path = path,
                    Name = fileName,
                    ErrorMessage = "No entry found in container archive."
                };
            }

            if (entry.EntryType == TarEntryType.Directory)
            {
                return new ContainerFileContentResult
                {
                    Success = false,
                    ContainerId = containerId,
                    Path = path,
                    Name = fileName,
                    ErrorMessage = "Target path is a directory, not a file."
                };
            }

            if ((entry.EntryType == TarEntryType.SymbolicLink || entry.EntryType == TarEntryType.HardLink) && !string.IsNullOrEmpty(entry.LinkName))
            {
                string linkTarget = entry.LinkName;
                string resolved = linkTarget.StartsWith('/')
                    ? linkTarget
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "/", linkTarget)).Replace('\\', '/');
                return await GetFileContentAsync(containerId, resolved, cancellationToken);
            }

            const int maxPreviewSize = 2 * 1024 * 1024;
            using var ms = new MemoryStream();
            if (entry.DataStream != null)
            {
                byte[] buffer = new byte[8192];
                int totalRead = 0;
                int read;
                while (totalRead < maxPreviewSize && (read = await entry.DataStream.ReadAsync(buffer, 0, Math.Min(buffer.Length, maxPreviewSize - totalRead), cancellationToken)) > 0)
                {
                    ms.Write(buffer, 0, read);
                    totalRead += read;
                }
            }

            byte[] fileBytes = ms.ToArray();
            bool isText = IsTextContent(fileBytes);
            string? textContent = null;

            if (isText)
            {
                textContent = System.Text.Encoding.UTF8.GetString(fileBytes);
            }

            return new ContainerFileContentResult
            {
                Success = true,
                ContainerId = containerId,
                Path = path,
                Name = fileName,
                Size = entry.Length,
                IsText = isText,
                ContentText = textContent
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file archive for {ContainerId} at {Path}", containerId, path);
            return new ContainerFileContentResult
            {
                Success = false,
                ContainerId = containerId,
                Path = path,
                Name = fileName,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Extracts a specific file stream from the container tar archive for direct client download.
    /// </summary>
    public async Task<(Stream? Stream, string FileName, long Size)> GetFileArchiveStreamAsync(
        string containerId,
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(containerId) || string.IsNullOrWhiteSpace(path))
        {
            return (null, string.Empty, 0);
        }

        path = path.Trim();
        if (!path.StartsWith('/')) path = "/" + path;
        string fileName = Path.GetFileName(path);

        try
        {
            var response = await _client.Containers.GetArchiveFromContainerAsync(
                containerId,
                new GetArchiveFromContainerParameters { Path = path },
                statOnly: false,
                cancellationToken);

            if (response.Stream == null)
            {
                return (null, fileName, 0);
            }

            using var stream = response.Stream;
            using var tarReader = new TarReader(stream);

            TarEntry? entry = tarReader.GetNextEntry();
            if (entry == null || entry.EntryType == TarEntryType.Directory)
            {
                return (null, fileName, 0);
            }

            if ((entry.EntryType == TarEntryType.SymbolicLink || entry.EntryType == TarEntryType.HardLink) && !string.IsNullOrEmpty(entry.LinkName))
            {
                string linkTarget = entry.LinkName;
                string resolved = linkTarget.StartsWith('/')
                    ? linkTarget
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "/", linkTarget)).Replace('\\', '/');
                return await GetFileArchiveStreamAsync(containerId, resolved, cancellationToken);
            }

            if (entry.DataStream == null)
            {
                return (null, fileName, 0);
            }

            var ms = new MemoryStream();
            await entry.DataStream.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            return (ms, fileName, entry.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file archive for {ContainerId} at {Path}", containerId, path);
            return (null, fileName, 0);
        }
    }

    private static bool IsTextContent(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0) return true;
        int checkLength = Math.Min(bytes.Length, 1024);
        int controlChars = 0;

        for (int i = 0; i < checkLength; i++)
        {
            byte b = bytes[i];
            if (b == 0) return false;
            if (b < 7 || (b > 13 && b < 32))
            {
                controlChars++;
            }
        }

        return (controlChars / (double)checkLength) < 0.1;
    }


    private static string ConvertModeToString(System.IO.UnixFileMode mode, bool isDir)
    {
        int modeInt = (int)mode & 0xFFF;
        return (isDir ? "d" : "-") + Convert.ToString(modeInt, 8).PadLeft(3, '0');
    }

    /// <summary>
    /// Executes a shell command inside a container using Docker.DotNet Exec APIs.
    /// </summary>
    public async Task<ContainerExecResult> ExecCommandAsync(
        string containerId,
        ContainerExecRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return new ContainerExecResult
            {
                Success = false,
                ExitCode = -1,
                ErrorMessage = "Container ID is required."
            };
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return new ContainerExecResult
            {
                Success = false,
                ExitCode = -1,
                ErrorMessage = "Command string cannot be empty."
            };
        }

        try
        {
            // Inspect container first to verify it is running
            var inspect = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
            if (inspect == null)
            {
                return new ContainerExecResult
                {
                    Success = false,
                    ExitCode = -1,
                    ErrorMessage = $"Container '{containerId}' not found."
                };
            }

            if (inspect.State?.Running != true)
            {
                return new ContainerExecResult
                {
                    Success = false,
                    ExitCode = -1,
                    ErrorMessage = $"Container is in state '{inspect.State?.Status ?? "not running"}'. Commands can only be executed in running containers."
                };
            }

            string shell = string.IsNullOrWhiteSpace(request.Shell) ? "/bin/sh" : request.Shell.Trim();
            IList<string> cmd;
            if (shell.Equals("raw", StringComparison.OrdinalIgnoreCase) || shell.Equals("direct", StringComparison.OrdinalIgnoreCase))
            {
                cmd = ParseCommandLine(request.Command);
            }
            else
            {
                cmd = new[] { shell, "-c", request.Command };
            }

            var execParams = new ContainerExecCreateParameters
            {
                AttachStdout = true,
                AttachStderr = true,
                AttachStdin = false,
                Tty = false,
                Cmd = cmd,
                WorkingDir = string.IsNullOrWhiteSpace(request.WorkingDir) ? null : request.WorkingDir.Trim(),
                User = string.IsNullOrWhiteSpace(request.User) ? null : request.User.Trim(),
                Privileged = request.Privileged
            };

            var execResponse = await _client.Exec.ExecCreateContainerAsync(containerId, execParams, cancellationToken);
            if (execResponse == null || string.IsNullOrEmpty(execResponse.ID))
            {
                return new ContainerExecResult
                {
                    Success = false,
                    ExitCode = -1,
                    ErrorMessage = "Failed to create exec instance in Docker engine."
                };
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

            using var stream = await _client.Exec.StartAndAttachContainerExecAsync(execResponse.ID, tty: false, timeoutCts.Token);
            var (stdout, stderr) = await stream.ReadOutputToEndAsync(timeoutCts.Token);
            sw.Stop();

            var inspectExec = await _client.Exec.InspectContainerExecAsync(execResponse.ID, cancellationToken);
            int exitCode = inspectExec != null ? (int)inspectExec.ExitCode : 0;

            return new ContainerExecResult
            {
                Success = exitCode == 0,
                ExitCode = exitCode,
                Stdout = stdout ?? string.Empty,
                Stderr = stderr ?? string.Empty,
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new ContainerExecResult
            {
                Success = false,
                ExitCode = 124,
                Stderr = "Execution timed out after 60 seconds.",
                ErrorMessage = "Command execution timed out.",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Failed to execute command '{Command}' in container {ContainerId}", request.Command, containerId);
            return new ContainerExecResult
            {
                Success = false,
                ExitCode = -1,
                Stderr = ex.Message,
                ErrorMessage = $"Docker daemon execution failed: {ex.Message}",
                DurationMs = sw.ElapsedMilliseconds
            };
        }
    }

    private static List<string> ParseCommandLine(string cmd)
    {
        var args = new List<string>();
        if (string.IsNullOrWhiteSpace(cmd)) return args;

        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        char quoteChar = '\0';

        for (int i = 0; i < cmd.Length; i++)
        {
            char ch = cmd[i];
            if (ch == '\\' && i + 1 < cmd.Length && inQuotes && quoteChar == '"')
            {
                current.Append(cmd[++i]);
            }
            else if ((ch == '"' || ch == '\'') && (!inQuotes || quoteChar == ch))
            {
                inQuotes = !inQuotes;
                quoteChar = inQuotes ? ch : '\0';
            }
            else if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args;
    }
}
