namespace Fleptix.Observer.Pages;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Fleptix.Observer.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class DeployModel : PageModel
{
    private readonly IContainerService _containerService;
    private readonly ContainerServiceManager _manager;

    public bool IsConnectedToDocker => _containerService.IsConnectedToDocker;
    public bool IsDemoMode => _manager.ForceDemoMode || !_manager.IsConnectedToDocker;

    [BindProperty]
    public DeployContainerRequest DeployRequest { get; set; } = new();

    public DeployContainerResult? Result { get; set; }

    public DeployModel(
        IContainerService containerService,
        ContainerServiceManager manager)
    {
        _containerService = containerService;
        _manager = manager;
    }

    public void OnGet()
    {
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
