namespace Fleptix.Observer.Pages;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class ContainersModel : PageModel
{
    private readonly IContainerService _containerService;

    public IReadOnlyList<ContainerSummary> Containers { get; private set; } = [];
    public IReadOnlyDictionary<string, ContainerMetrics> InitialMetrics { get; private set; } = new Dictionary<string, ContainerMetrics>();

    public bool IsConnectedToDocker => _containerService.IsConnectedToDocker;
    public string DaemonEndpoint => _containerService.DockerUri.ToString();
    public string? LastConnectionError => _containerService.LastConnectionError;

    public ContainersModel(IContainerService containerService)
    {
        _containerService = containerService;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken = default)
    {
        Containers = await _containerService.GetContainersAsync(includeAll: true, cancellationToken);
        InitialMetrics = await _containerService.GetAllCurrentMetricsAsync(cancellationToken);
    }
}
