namespace Fleptix.Observer.Services;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;

/// <summary>
/// High-fidelity simulated container engine for offline demos, pitch presentations, and testing.
/// Simulates realistic workloads, logs, and container lifecycle transitions.
/// </summary>
public class DemoContainerService : IContainerService
{
    public bool IsConnectedToDocker => false;

    private class SimulatedContainer
    {
        public ContainerSummary Summary { get; set; } = default!;
        public ContainerDetails Details { get; set; } = default!;
        public List<string> Logs { get; } = new();
        public double BaseCpu { get; set; }
        public ulong BaseMemoryBytes { get; set; }
        public ulong MemoryLimitBytes { get; set; }
        public double PhaseOffset { get; set; }
    }

    private readonly ConcurrentDictionary<string, SimulatedContainer> _containers = new();
    private readonly Random _random = new();

    public DemoContainerService()
    {
        SeedDemoContainers();
    }

    private void SeedDemoContainers()
    {
        var now = DateTime.UtcNow;

        var demoList = new[]
        {
            new
            {
                Id = "c1a84f901234abcd5678ef01",
                Name = "fleptix-gateway",
                Image = "nginx:alpine-slim",
                State = "running",
                Status = "Up 3 hours",
                Ports = new[] { "80:80/tcp", "443:443/tcp" },
                BaseCpu = 4.2,
                BaseMemMb = 48.0,
                LimitMb = 512.0,
                Env = new Dictionary<string, string>
                {
                    ["NGINX_PORT"] = "80",
                    ["PROXY_PASS"] = "http://fleptix-api:5000",
                    ["WORKER_PROCESSES"] = "auto"
                },
                Mounts = new[] { "/etc/nginx/nginx.conf:/etc/nginx/nginx.conf:ro", "/var/log/nginx:/var/log/nginx:rw" },
                Logs = new[]
                {
                    "[notice] 1#1: using the \"epoll\" event method",
                    "[notice] 1#1: nginx/1.25.3 started",
                    "[info] 1#1: 192.168.1.102 GET /api/v1/health HTTP/1.1 200 OK",
                    "[info] 1#1: 192.168.1.105 GET /dashboard HTTP/1.1 200 OK",
                    "[info] 1#1: 192.168.1.110 POST /api/v1/auth HTTP/1.1 200 OK",
                    "[info] 1#1: 192.168.1.112 GET /assets/app.css HTTP/1.1 304 Not Modified"
                }
            },
            new
            {
                Id = "c2b95e012345bcde6789f012",
                Name = "fleptix-core-api",
                Image = "fleptix/backend:v1.2.0",
                State = "running",
                Status = "Up 3 hours",
                Ports = new[] { "5000:5000/tcp" },
                BaseCpu = 14.8,
                BaseMemMb = 230.0,
                LimitMb = 1024.0,
                Env = new Dictionary<string, string>
                {
                    ["ASPNETCORE_ENVIRONMENT"] = "Production",
                    ["ConnectionStrings__Database"] = "Host=fleptix-postgres;Database=fleptix;Username=postgres",
                    ["ConnectionStrings__Redis"] = "fleptix-redis:6379",
                    ["DOTNET_RUNNING_IN_CONTAINER"] = "true"
                },
                Mounts = new[] { "/app/data:/data:rw", "/app/snapshots:/snapshots:rw" },
                Logs = new[]
                {
                    "info: Microsoft.Hosting.Lifetime[14] Now listening on: http://[::]:5000",
                    "info: Fleptix.Core.Server[0] Application started. Press Ctrl+C to shut down.",
                    "info: Fleptix.Observer[201] SignalR Hub initialized on /hubs/containers",
                    "info: Fleptix.Observer[202] Telemetry stream registered with 500ms jitter buffer",
                    "info: Fleptix.Core.Pipeline[100] Processed 142 telemetry frames from local Docker daemon",
                    "info: Fleptix.Core.Pipeline[100] Real-time metric snapshot dispatched to 4 active clients"
                }
            },
            new
            {
                Id = "c3c06f123456cdef7890a123",
                Name = "fleptix-redis-cache",
                Image = "redis:7.2-alpine",
                State = "running",
                Status = "Up 4 hours",
                Ports = new[] { "6379:6379/tcp" },
                BaseCpu = 3.1,
                BaseMemMb = 64.0,
                LimitMb = 512.0,
                Env = new Dictionary<string, string>
                {
                    ["REDIS_MAXMEMORY"] = "400mb",
                    ["REDIS_MAXMEMORY_POLICY"] = "allkeys-lru"
                },
                Mounts = new[] { "/data/redis:/data:rw" },
                Logs = new[]
                {
                    "1:M 01 Sep 2026 12:00:00.000 * Running mode=standalone, port=6379.",
                    "1:M 01 Sep 2026 12:00:00.001 # Server initialized",
                    "1:M 01 Sep 2026 12:00:00.002 * Ready to accept connections tcp",
                    "1:M 01 Sep 2026 12:15:30.120 * DB 0: 1,420 keys (0 volatile) in 2048 slots",
                    "1:M 01 Sep 2026 13:00:00.000 * 100 changes in 300 seconds. Saving...",
                    "1:M 01 Sep 2026 13:00:00.045 * Background saving terminated with success"
                }
            },
            new
            {
                Id = "c4d17a234567defa8901b234",
                Name = "fleptix-postgres-db",
                Image = "postgres:16.2-bullseye",
                State = "running",
                Status = "Up 5 hours",
                Ports = new[] { "5432:5432/tcp" },
                BaseCpu = 8.5,
                BaseMemMb = 310.0,
                LimitMb = 2048.0,
                Env = new Dictionary<string, string>
                {
                    ["POSTGRES_DB"] = "fleptix_telemetry",
                    ["POSTGRES_USER"] = "postgres",
                    ["PGDATA"] = "/var/lib/postgresql/data/pgdata"
                },
                Mounts = new[] { "postgres_volume:/var/lib/postgresql/data:rw" },
                Logs = new[]
                {
                    "2026-09-01 10:00:00.010 UTC [1] LOG:  starting PostgreSQL 16.2 on x86_64-pc-linux-gnu",
                    "2026-09-01 10:00:00.012 UTC [1] LOG:  listening on IPv4 address \"0.0.0.0\", port 5432",
                    "2026-09-01 10:00:00.050 UTC [1] LOG:  database system was properly shut down",
                    "2026-09-01 10:00:00.055 UTC [1] LOG:  database system is ready to accept connections",
                    "2026-09-01 12:45:00.100 UTC [48] LOG:  checkpoint starting: time",
                    "2026-09-01 12:45:02.340 UTC [48] LOG:  checkpoint complete: wrote 420 buffers (2.6%)"
                }
            },
            new
            {
                Id = "c5e28b345678efab9012c345",
                Name = "fleptix-worker-node",
                Image = "fleptix/worker:v1.2.0",
                State = "paused",
                Status = "Paused",
                Ports = Array.Empty<string>(),
                BaseCpu = 0.0,
                BaseMemMb = 140.0,
                LimitMb = 1024.0,
                Env = new Dictionary<string, string>
                {
                    ["WORKER_CONCURRENCY"] = "8",
                    ["QUEUE_NAME"] = "telemetry-aggregation",
                    ["BATCH_SIZE"] = "500"
                },
                Mounts = new[] { "/tmp/worker:/tmp:rw" },
                Logs = new[]
                {
                    "[Worker-1] Background task runner initialized",
                    "[Worker-1] Connected to Redis queue 'telemetry-aggregation'",
                    "[Worker-1] Processed batch #4182 (500 items in 34ms)",
                    "[Worker-1] Container received SIGSTOP / pause signal from operator"
                }
            }
        };

        foreach (var d in demoList)
        {
            var summary = new ContainerSummary
            {
                Id = d.Id,
                Name = d.Name,
                Image = d.Image,
                State = d.State,
                Status = d.Status,
                Created = now.AddHours(-6),
                Ports = d.Ports
            };

            var details = new ContainerDetails
            {
                Summary = summary,
                ImageHash = $"sha256:{Guid.NewGuid():N}{Guid.NewGuid():N}"[..64],
                Command = "/entrypoint.sh",
                WorkingDir = "/app",
                IPAddress = $"172.20.0.{_random.Next(2, 50)}",
                NetworkMode = "fleptix_default",
                EnvironmentVariables = d.Env,
                Mounts = d.Mounts,
                PortBindings = d.Ports,
                Labels = new Dictionary<string, string>
                {
                    ["com.fleptix.managed"] = "true",
                    ["com.fleptix.version"] = "1.0",
                    ["environment"] = "demo"
                },
                RestartPolicy = "unless-stopped",
                StartedAt = now.AddHours(-3)
            };

            var container = new SimulatedContainer
            {
                Summary = summary,
                Details = details,
                BaseCpu = d.BaseCpu,
                BaseMemoryBytes = (ulong)(d.BaseMemMb * 1024 * 1024),
                MemoryLimitBytes = (ulong)(d.LimitMb * 1024 * 1024),
                PhaseOffset = _random.NextDouble() * Math.PI * 2
            };

            container.Logs.AddRange(d.Logs);
            _containers[d.Id] = container;
        }
    }

    public Task<IReadOnlyList<ContainerSummary>> GetContainersAsync(bool includeAll = true, CancellationToken cancellationToken = default)
    {
        var list = _containers.Values.Select(c => c.Summary).ToList();
        return Task.FromResult<IReadOnlyList<ContainerSummary>>(list);
    }

    public Task<ContainerDetails?> GetContainerDetailsAsync(string containerId, CancellationToken cancellationToken = default)
    {
        if (_containers.TryGetValue(containerId, out var container))
        {
            return Task.FromResult<ContainerDetails?>(container.Details);
        }
        return Task.FromResult<ContainerDetails?>(null);
    }

    public Task<IReadOnlyDictionary<string, ContainerMetrics>> GetAllCurrentMetricsAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, ContainerMetrics>();
        var time = DateTime.UtcNow.Ticks / 10_000_000.0;

        foreach (var (id, container) in _containers)
        {
            result[id] = CalculateMetrics(container, time);
        }

        return Task.FromResult<IReadOnlyDictionary<string, ContainerMetrics>>(result);
    }

    public Task<ContainerMetrics?> GetMetricsAsync(string containerId, CancellationToken cancellationToken = default)
    {
        if (_containers.TryGetValue(containerId, out var container))
        {
            var time = DateTime.UtcNow.Ticks / 10_000_000.0;
            return Task.FromResult<ContainerMetrics?>(CalculateMetrics(container, time));
        }
        return Task.FromResult<ContainerMetrics?>(null);
    }

    private ContainerMetrics CalculateMetrics(SimulatedContainer container, double timeSeconds)
    {
        if (container.Summary.State == "exited")
        {
            return new ContainerMetrics
            {
                ContainerId = container.Summary.Id,
                Timestamp = DateTime.UtcNow,
                CpuPercentage = 0.0,
                MemoryUsageBytes = 0,
                MemoryLimitBytes = container.MemoryLimitBytes,
                NetworkRxBytes = 0,
                NetworkTxBytes = 0
            };
        }

        if (container.Summary.State == "paused")
        {
            return new ContainerMetrics
            {
                ContainerId = container.Summary.Id,
                Timestamp = DateTime.UtcNow,
                CpuPercentage = 0.0,
                MemoryUsageBytes = container.BaseMemoryBytes,
                MemoryLimitBytes = container.MemoryLimitBytes,
                NetworkRxBytes = 1024,
                NetworkTxBytes = 512
            };
        }

        // Running state: generate organic sinusoidal fluctuation with noise
        var wave = Math.Sin(timeSeconds * 0.5 + container.PhaseOffset) * 0.35 + 0.65;
        var noise = (_random.NextDouble() - 0.5) * 2.0;
        var cpu = Math.Max(0.5, Math.Round(container.BaseCpu * wave + noise, 2));

        var memJitter = (long)((_random.NextDouble() - 0.5) * 4 * 1024 * 1024);
        var memBytes = (ulong)Math.Max(1024 * 1024, (long)container.BaseMemoryBytes + memJitter);

        return new ContainerMetrics
        {
            ContainerId = container.Summary.Id,
            Timestamp = DateTime.UtcNow,
            CpuPercentage = cpu,
            MemoryUsageBytes = memBytes,
            MemoryLimitBytes = container.MemoryLimitBytes,
            NetworkRxBytes = (ulong)(_random.Next(500, 50000)),
            NetworkTxBytes = (ulong)(_random.Next(200, 30000))
        };
    }

    public async IAsyncEnumerable<ContainerMetrics> GetMetricsStreamAsync(
        string containerId, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_containers.TryGetValue(containerId, out var container))
            {
                var time = DateTime.UtcNow.Ticks / 10_000_000.0;
                yield return CalculateMetrics(container, time);
            }

            await Task.Delay(1000, cancellationToken);
        }
    }

    public Task<ContainerActionResult> ExecuteActionAsync(string containerId, string action, CancellationToken cancellationToken = default)
    {
        if (!_containers.TryGetValue(containerId, out var container))
        {
            return Task.FromResult(new ContainerActionResult
            {
                Success = false,
                ContainerId = containerId,
                Message = $"Container {containerId} not found."
            });
        }

        string newState;
        string newStatus;
        string message;

        switch (action.ToLowerInvariant())
        {
            case "start":
                if (container.Summary.State == "running")
                {
                    return Task.FromResult(new ContainerActionResult
                    {
                        Success = false,
                        ContainerId = containerId,
                        Message = $"Container '{container.Summary.Name}' is already running."
                    });
                }
                newState = "running";
                newStatus = "Up less than a second";
                message = $"Container '{container.Summary.Name}' started successfully.";
                container.Logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Container started by operator.");
                break;

            case "stop":
                if (container.Summary.State == "exited")
                {
                    return Task.FromResult(new ContainerActionResult
                    {
                        Success = false,
                        ContainerId = containerId,
                        Message = $"Container '{container.Summary.Name}' is already stopped."
                    });
                }
                newState = "exited";
                newStatus = "Exited (0) Just now";
                message = $"Container '{container.Summary.Name}' stopped.";
                container.Logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Received SIGTERM, graceful shutdown complete.");
                break;

            case "restart":
                newState = "running";
                newStatus = "Up 1 second (Restarted)";
                message = $"Container '{container.Summary.Name}' restarted.";
                container.Logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Restart initiated. Reloading configuration...");
                container.Logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Service ready.");
                break;

            case "pause":
                if (container.Summary.State != "running")
                {
                    return Task.FromResult(new ContainerActionResult
                    {
                        Success = false,
                        ContainerId = containerId,
                        Message = $"Cannot pause container in state '{container.Summary.State}'."
                    });
                }
                newState = "paused";
                newStatus = "Paused";
                message = $"Container '{container.Summary.Name}' paused.";
                container.Logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Container process paused (SIGSTOP).");
                break;

            case "unpause":
                if (container.Summary.State != "paused")
                {
                    return Task.FromResult(new ContainerActionResult
                    {
                        Success = false,
                        ContainerId = containerId,
                        Message = $"Container '{container.Summary.Name}' is not paused."
                    });
                }
                newState = "running";
                newStatus = "Up 10 seconds (Unpaused)";
                message = $"Container '{container.Summary.Name}' unpaused.";
                container.Logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] Container process resumed (SIGCONT).");
                break;

            default:
                return Task.FromResult(new ContainerActionResult
                {
                    Success = false,
                    ContainerId = containerId,
                    Message = $"Action '{action}' is not supported."
                });
        }

        var updatedSummary = container.Summary with
        {
            State = newState,
            Status = newStatus
        };

        var updatedDetails = container.Details with
        {
            Summary = updatedSummary
        };

        container.Summary = updatedSummary;
        container.Details = updatedDetails;

        return Task.FromResult(new ContainerActionResult
        {
            Success = true,
            ContainerId = containerId,
            NewState = newState,
            Message = message
        });
    }

    public Task<DeployContainerResult> DeployContainerAsync(DeployContainerRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Image))
        {
            return Task.FromResult(new DeployContainerResult
            {
                Success = false,
                Message = "Image name is required for deployment.",
                Logs = new[] { $"[{DateTime.UtcNow:HH:mm:ss}] [ERROR] Image name cannot be empty." }
            });
        }

        var deployLogs = new List<string>();
        var timestamp = DateTime.UtcNow;
        var containerId = $"c{_random.Next(10, 99)}{Guid.NewGuid():N}"[..24];
        
        var baseImgName = request.Image.Split(':')[0].Split('/').Last();
        var containerName = string.IsNullOrWhiteSpace(request.Name) 
            ? $"demo-{baseImgName}-{_random.Next(100, 999)}" 
            : request.Name.Trim();

        deployLogs.Add($"[{timestamp:HH:mm:ss}] [STEP 1/4] Pulling image '{request.Image}' from demo registry cache...");
        deployLogs.Add($"[{timestamp.AddMilliseconds(200):HH:mm:ss}] [PULL] 5a8a61421: Pulling fs layer");
        deployLogs.Add($"[{timestamp.AddMilliseconds(400):HH:mm:ss}] [PULL] 5a8a61421: Download complete");
        deployLogs.Add($"[{timestamp.AddMilliseconds(700):HH:mm:ss}] [PULL] 5a8a61421: Extracting [====================================>] 100%");
        deployLogs.Add($"[{timestamp.AddMilliseconds(900):HH:mm:ss}] Image '{request.Image}' ready.");

        deployLogs.Add($"[{timestamp.AddMilliseconds(1000):HH:mm:ss}] [STEP 2/4] Allocating virtual container instance '{containerName}'...");
        
        if (request.PortMappings != null)
        {
            foreach (var p in request.PortMappings.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                deployLogs.Add($"[{timestamp.AddMilliseconds(1100):HH:mm:ss}] Configured port mapping: {p.Trim()}");
            }
        }

        if (request.VolumeMounts != null)
        {
            foreach (var v in request.VolumeMounts.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                deployLogs.Add($"[{timestamp.AddMilliseconds(1200):HH:mm:ss}] Configured volume bind: {v.Trim()}");
            }
        }

        deployLogs.Add($"[{timestamp.AddMilliseconds(1300):HH:mm:ss}] [STEP 3/4] Container created with ID: {containerId[..12]}");

        var isRunning = request.AutoStart;
        if (isRunning)
        {
            deployLogs.Add($"[{timestamp.AddMilliseconds(1400):HH:mm:ss}] [STEP 4/4] Starting container runtime...");
            deployLogs.Add($"[{timestamp.AddMilliseconds(1500):HH:mm:ss}] Container '{containerName}' is now active and listening.");
        }
        else
        {
            deployLogs.Add($"[{timestamp.AddMilliseconds(1400):HH:mm:ss}] Container '{containerName}' created in stopped state.");
        }

        var ports = request.PortMappings?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
        var summary = new ContainerSummary
        {
            Id = containerId,
            Name = containerName,
            Image = request.Image,
            State = isRunning ? "running" : "created",
            Status = isRunning ? "Up less than a minute" : "Created",
            Created = timestamp,
            Ports = ports
        };

        var env = request.EnvironmentVariables != null 
            ? new Dictionary<string, string>(request.EnvironmentVariables) 
            : new Dictionary<string, string>();

        var mounts = request.VolumeMounts?.Where(m => !string.IsNullOrWhiteSpace(m)).ToList() ?? new List<string>();

        var details = new ContainerDetails
        {
            Summary = summary,
            ImageHash = $"sha256:{Guid.NewGuid():N}{Guid.NewGuid():N}"[..64],
            Command = request.Command ?? "/entrypoint.sh",
            WorkingDir = request.WorkingDir ?? "/app",
            IPAddress = $"172.20.0.{_random.Next(51, 99)}",
            NetworkMode = string.IsNullOrWhiteSpace(request.NetworkMode) ? "bridge" : request.NetworkMode,
            EnvironmentVariables = env,
            Mounts = mounts,
            PortBindings = ports,
            Labels = request.Labels != null ? new Dictionary<string, string>(request.Labels) : new Dictionary<string, string>(),
            RestartPolicy = request.RestartPolicy ?? "unless-stopped",
            StartedAt = isRunning ? timestamp : null
        };

        var simulated = new SimulatedContainer
        {
            Summary = summary,
            Details = details,
            BaseCpu = 3.5 + _random.NextDouble() * 8.0,
            BaseMemoryBytes = (ulong)((request.MemoryLimitMb > 0 ? Math.Min(request.MemoryLimitMb, 256) : 128) * 1024 * 1024),
            MemoryLimitBytes = (ulong)((request.MemoryLimitMb > 0 ? request.MemoryLimitMb : 512) * 1024 * 1024),
            PhaseOffset = _random.NextDouble() * Math.PI * 2
        };

        simulated.Logs.AddRange(deployLogs);
        simulated.Logs.Add($"[{timestamp.AddMilliseconds(1600):HH:mm:ss}] [system] Service listening for incoming connections.");
        _containers[containerId] = simulated;

        return Task.FromResult(new DeployContainerResult
        {
            Success = true,
            ContainerId = containerId,
            ContainerName = containerName,
            Message = $"Container '{containerName}' deployed successfully.",
            Logs = deployLogs
        });
    }

    public Task<IReadOnlyList<string>> GetLogsAsync(string containerId, int tailLines = 100, CancellationToken cancellationToken = default)
    {
        if (_containers.TryGetValue(containerId, out var container))
        {
            lock (container.Logs)
            {
                // Add an occasional live tick log line if running
                if (container.Summary.State == "running" && _random.Next(0, 3) == 0)
                {
                    container.Logs.Add($"[{DateTime.UtcNow:HH:mm:ss}] [telemetry-worker] Heartbeat check passed (latency: {_random.Next(2, 15)}ms)");
                    if (container.Logs.Count > 500)
                    {
                        container.Logs.RemoveRange(0, 100);
                    }
                }

                var tail = container.Logs.TakeLast(tailLines).ToList();
                return Task.FromResult<IReadOnlyList<string>>(tail);
            }
        }

        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}

