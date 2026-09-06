namespace Fleptix.Observer.Services;

using Fleptix.Core.Interfaces;
using Fleptix.Observer.Hubs;
using Microsoft.AspNetCore.SignalR;

/// <summary>
/// Periodically samples container metrics and pushes updates to connected clients via SignalR.
/// </summary>
public class TelemetryBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<ContainerHub> _hubContext;
    private readonly ILogger<TelemetryBackgroundService> _logger;
    private readonly TimeSpan _samplingInterval = TimeSpan.FromMilliseconds(1200);

    public TelemetryBackgroundService(
        IServiceProvider serviceProvider,
        IHubContext<ContainerHub> hubContext,
        ILogger<TelemetryBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Fleptix Telemetry background publisher started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var containerService = scope.ServiceProvider.GetRequiredService<IContainerService>();

                var containers = await containerService.GetContainersAsync(includeAll: true, stoppingToken);
                var metrics = await containerService.GetAllCurrentMetricsAsync(stoppingToken);

                // Calculate cluster aggregated stats
                double totalCpu = 0.0;
                ulong totalMemoryBytes = 0;
                ulong totalMemoryLimit = 0;
                int runningCount = 0;
                int pausedCount = 0;
                int stoppedCount = 0;

                foreach (var c in containers)
                {
                    if (c.State == "running") runningCount++;
                    else if (c.State == "paused") pausedCount++;
                    else stoppedCount++;

                    if (metrics.TryGetValue(c.Id, out var m))
                    {
                        totalCpu += m.CpuPercentage;
                        totalMemoryBytes += m.MemoryUsageBytes;
                        totalMemoryLimit += m.MemoryLimitBytes;

                        // Broadcast to container specific room
                        await _hubContext.Clients.Group($"container_{c.Id}")
                            .SendAsync("ReceiveContainerMetrics", m, stoppingToken);
                    }
                }

                var clusterPayload = new
                {
                    Timestamp = DateTime.UtcNow,
                    TotalContainers = containers.Count,
                    RunningCount = runningCount,
                    PausedCount = pausedCount,
                    StoppedCount = stoppedCount,
                    TotalCpuPercentage = Math.Round(totalCpu, 2),
                    TotalMemoryUsageMb = Math.Round(totalMemoryBytes / (1024.0 * 1024.0), 2),
                    TotalMemoryLimitMb = Math.Round(totalMemoryLimit / (1024.0 * 1024.0), 2),
                    Containers = containers,
                    Metrics = metrics,
                    IsConnectedToDocker = containerService.IsConnectedToDocker
                };

                // Broadcast cluster overview to all connected dashboard clients
                await _hubContext.Clients.All.SendAsync("ReceiveClusterMetrics", clusterPayload, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error pushing telemetry via SignalR");
            }

            await Task.Delay(_samplingInterval, stoppingToken);
        }

        _logger.LogInformation("Fleptix Telemetry background publisher stopped.");
    }
}
