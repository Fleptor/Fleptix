namespace Fleptix.Observer.Pages;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class IndexModel : PageModel
{
    private readonly IContainerService _containerService;

    public IReadOnlyList<ContainerSummary> Containers { get; private set; } = [];
    public IReadOnlyDictionary<string, ContainerMetrics> InitialMetrics { get; private set; } = new Dictionary<string, ContainerMetrics>();
    public DockerStorageInfo StorageInfo { get; private set; } = new();
    public HostSystemInfo HostInfo { get; private set; } = new();
    public bool IsConnectedToDocker => _containerService.IsConnectedToDocker;
    public string DaemonEndpoint => _containerService.DockerUri.ToString();
    public string? LastConnectionError => _containerService.LastConnectionError;

    public IndexModel(IContainerService containerService)
    {
        _containerService = containerService;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken = default)
    {
        Containers = await _containerService.GetContainersAsync(includeAll: true, cancellationToken);
        InitialMetrics = await _containerService.GetAllCurrentMetricsAsync(cancellationToken);
        StorageInfo = await _containerService.GetStorageInfoAsync(cancellationToken);
        HostInfo = await _containerService.GetHostSystemInfoAsync(cancellationToken);
    }
}
