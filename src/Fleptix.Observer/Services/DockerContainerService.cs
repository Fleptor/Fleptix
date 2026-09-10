namespace Fleptix.Observer.Services;

using System.Diagnostics;
using System.Formats.Tar;
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
