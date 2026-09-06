namespace Fleptix.Observer.Services;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
    public bool IsConnectedToDocker => _isConnectedToDocker;

    private readonly DockerClient _client;
    private readonly ILogger<DockerContainerService> _logger;

    public DockerContainerService(ILogger<DockerContainerService> logger)
    {
        _logger = logger;

        Uri dockerUri = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new Uri("npipe://./pipe/docker_engine")
            : new Uri("unix:///var/run/docker.sock");

        _client = new DockerClientConfiguration(dockerUri).CreateClient();
    }

    /// <summary>
    /// Non-blocking asynchronous connectivity probe with a strict 300ms timeout.
    /// </summary>
    public async Task<bool> CheckConnectivityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(300));

            await _client.System.PingAsync(cts.Token);
            _isConnectedToDocker = true;
            return true;
        }
        catch
        {
            _isConnectedToDocker = false;
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

            return containers.Select(c => new ContainerSummary
            {
                Id = c.ID,
                Name = c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID[..12],
                Image = c.Image,
                State = c.State?.ToLowerInvariant() ?? "unknown",
                Status = c.Status,
                Created = c.Created,
                Ports = c.Ports?.Select(p => $"{p.PublicPort}:{p.PrivatePort}/{p.Type}").ToList() ?? []
            }).ToList();
        }
        catch (Exception ex)
        {
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

            var summary = new ContainerSummary
            {
                Id = inspect.ID,
                Name = inspect.Name?.TrimStart('/') ?? inspect.ID[..12],
                Image = inspect.Config?.Image ?? "unknown",
                State = inspect.State?.Status?.ToLowerInvariant() ?? "unknown",
                Status = inspect.State?.Status ?? "unknown",
                Created = inspect.Created,
                Ports = inspect.NetworkSettings?.Ports?.Select(p => $"{p.Key} -> {string.Join(",", p.Value?.Select(v => $"{v.HostIP}:{v.HostPort}") ?? Array.Empty<string>())}").ToList() ?? []
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
                Labels = inspect.Config?.Labels != null ? new Dictionary<string, string>(inspect.Config.Labels) : new Dictionary<string, string>(),
                RestartPolicy = inspect.HostConfig?.RestartPolicy != null ? inspect.HostConfig.RestartPolicy.Name.ToString() : "no",
                StartedAt = DateTime.TryParse(inspect.State?.StartedAt, out var s) ? s : null,
                FinishedAt = DateTime.TryParse(inspect.State?.FinishedAt, out var f) ? f : null
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

        return new ContainerMetrics
        {
            ContainerId = containerId,
            Timestamp = DateTime.UtcNow,
            CpuPercentage = cpuPercent,
            MemoryUsageBytes = memUsage,
            MemoryLimitBytes = memLimit,
            NetworkRxBytes = rx,
            NetworkTxBytes = tx
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
            var stream = await _client.Containers.GetContainerLogsAsync(
                containerId,
                tty: false,
                new ContainerLogsParameters
                {
                    ShowStdout = true,
                    ShowStderr = true,
                    Tail = tailLines.ToString(),
                    Timestamps = true
                },
                cancellationToken);

            var (stdout, stderr) = await stream.ReadOutputToEndAsync(cancellationToken);
            var combined = (stdout + "\n" + stderr)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            return combined;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read logs for container {ContainerId}", containerId);
            return new[] { $"[Error reading logs: {ex.Message}]" };
        }
    }
}
