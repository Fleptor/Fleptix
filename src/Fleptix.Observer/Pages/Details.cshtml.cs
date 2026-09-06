namespace Fleptix.Observer.Pages;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Fleptix.TimeMachine.Interfaces;
using Fleptix.TimeMachine.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

[IgnoreAntiforgeryToken]
public class DetailsModel : PageModel
{
    private readonly IContainerService _containerService;
    private readonly ISnapshotService _snapshotService;


    public ContainerDetails? Details { get; private set; }
    public ContainerMetrics? InitialMetrics { get; private set; }
    public IReadOnlyList<string> InitialLogs { get; private set; } = [];
    public IReadOnlyList<SnapshotResult> Snapshots { get; private set; } = [];
    public SnapshotResult? SnapshotResult { get; private set; }
    public RestoreResult? RestoreResult { get; private set; }

    public DetailsModel(
        IContainerService containerService,
        ISnapshotService snapshotService)
    {
        _containerService = containerService;
        _snapshotService = snapshotService;
    }

    public async Task<IActionResult> OnGetAsync(string? id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return RedirectToPage("/Index");
        }

        Details = await _containerService.GetContainerDetailsAsync(id, cancellationToken);
        if (Details == null)
        {
            return RedirectToPage("/Index");
        }

        InitialMetrics = await _containerService.GetMetricsAsync(id, cancellationToken);
        InitialLogs = await _containerService.GetLogsAsync(id, tailLines: 150, cancellationToken: cancellationToken);
        Snapshots = await _snapshotService.GetSnapshotsAsync(id, cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostSnapshotAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return RedirectToPage("/Index");
        }

        SnapshotResult = await _snapshotService.CreateSnapshotAsync(id, cancellationToken: cancellationToken);

        // Reload current container telemetry and metadata
        Details = await _containerService.GetContainerDetailsAsync(id, cancellationToken);
        InitialMetrics = await _containerService.GetMetricsAsync(id, cancellationToken);
        InitialLogs = await _containerService.GetLogsAsync(id, tailLines: 150, cancellationToken: cancellationToken);
        Snapshots = await _snapshotService.GetSnapshotsAsync(id, cancellationToken);

        if (SnapshotResult.Success)
        {
            TempData["SnapshotSuccess"] = SnapshotResult.Message;
        }
        else
        {
            TempData["SnapshotError"] = SnapshotResult.Message;
        }

        // Return JSON if invoked via fetch/AJAX
        if (Request.Headers.XRequestedWith == "XMLHttpRequest" ||
            Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return new JsonResult(SnapshotResult);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRestoreAsync(string id, string snapshotId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(snapshotId))
        {
            return RedirectToPage("/Details", new { id });
        }

        RestoreResult = await _snapshotService.RestoreSnapshotAsync(snapshotId, id, cancellationToken);

        // Reload current container telemetry and metadata
        Details = await _containerService.GetContainerDetailsAsync(id, cancellationToken);
        InitialMetrics = await _containerService.GetMetricsAsync(id, cancellationToken);
        InitialLogs = await _containerService.GetLogsAsync(id, tailLines: 150, cancellationToken: cancellationToken);
        Snapshots = await _snapshotService.GetSnapshotsAsync(id, cancellationToken);

        if (RestoreResult.Success)
        {
            TempData["RestoreSuccess"] = RestoreResult.Message;
        }
        else
        {
            TempData["RestoreError"] = RestoreResult.Message;
        }

        // Return JSON if invoked via fetch/AJAX
        if (Request.Headers.XRequestedWith == "XMLHttpRequest" ||
            Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return new JsonResult(RestoreResult);
        }

        return Page();
    }
}

