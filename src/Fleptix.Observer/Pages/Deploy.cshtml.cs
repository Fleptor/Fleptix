namespace Fleptix.Observer.Pages;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Fleptix.Observer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class DeployModel : PageModel
{
    private readonly IContainerService _containerService;

    public bool IsConnectedToDocker => _containerService.IsConnectedToDocker;
    public string DaemonEndpoint => _containerService.DockerUri.ToString();
    public string? LastConnectionError => _containerService.LastConnectionError;

    [BindProperty]
    public DeployContainerRequest DeployRequest { get; set; } = new();

    public DeployContainerResult? Result { get; set; }

    public DeployModel(IContainerService containerService)
    {
        _containerService = containerService;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken = default)
    {
        if (!_containerService.IsConnectedToDocker)
        {
            await _containerService.CheckConnectivityAsync(cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(DeployRequest.Image))
        {
            ModelState.AddModelError("DeployRequest.Image", "Image repository/tag is required.");
            return Page();
        }

        Result = await _containerService.DeployContainerAsync(DeployRequest, cancellationToken);
        return Page();
    }

}
