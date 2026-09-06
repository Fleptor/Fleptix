namespace Fleptix.Observer.Pages;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class IndexModel : PageModel
{
    private readonly IContainerService _containerService;

    public IReadOnlyList<ContainerSummary> Containers { get; private set; } = [];
    public IReadOnlyDictionary<string, ContainerMetrics> InitialMetrics { get; private set; } = new Dictionary<string, ContainerMetrics>();
    public bool IsConnectedToDocker { get; private set; }

    public IndexModel(IContainerService containerService)
    {
        _containerService = containerService;
    }

    public async Task OnGetAsync()
    {
        IsConnectedToDocker = _containerService.IsConnectedToDocker;
        Containers = await _containerService.GetContainersAsync(includeAll: true);
        InitialMetrics = await _containerService.GetAllCurrentMetricsAsync();
    }
}
