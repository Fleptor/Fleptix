namespace Fleptix.Observer.Pages;

using System.Runtime.InteropServices;
using Fleptix.Core.Interfaces;
using Fleptix.Observer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class SettingsModel : PageModel
{
    private readonly IContainerService _containerService;
    private readonly IWebHostEnvironment _env;
    private readonly ILicenseService _licenseService;

    public string DaemonEndpoint => _containerService.DockerUri.ToString();
    public bool IsConnectedToDocker => _containerService.IsConnectedToDocker;
    public string? LastConnectionError => _containerService.LastConnectionError;
    public string EnvironmentName => _env.EnvironmentName;
    public string EngineDescription => _containerService.IsConnectedToDocker 
        ? "Docker Engine (Native Pipe/Socket)" 
        : "Disconnected (Docker Engine Unreachable)";
    public int TelemetryIntervalMs { get; private set; } = 1200;
    public bool IsTimeMachineEnabled => _licenseService.IsTimeMachineEnabled();
    public LicenseTier LicenseTier => _licenseService.GetLicenseTier();
    public string LicenseTierName => _licenseService.GetTierDisplayName();
    public int MaxAllowedNodes => _licenseService.GetMaxAllowedNodes();
    public Fleptix.Core.Models.LicenseInfo LicenseInfo { get; private set; } = new();

    public SettingsModel(
        IContainerService containerService, 
        IWebHostEnvironment env,
        ILicenseService licenseService)
    {
        _containerService = containerService;
        _env = env;
        _licenseService = licenseService;
    }

    public async Task OnGetAsync(CancellationToken cancellationToken = default)
    {
        LicenseInfo = _licenseService.GetLicenseInfo();

        if (!_containerService.IsConnectedToDocker)
        {
            await _containerService.CheckConnectivityAsync(cancellationToken);
        }
    }
}
