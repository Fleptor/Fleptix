namespace Fleptix.Observer.Pages;

using System.Runtime.InteropServices;
using Fleptix.Observer.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class SettingsModel : PageModel
{
    private readonly ContainerServiceManager _manager;
    private readonly IWebHostEnvironment _env;

    public string DaemonEndpoint { get; private set; } = string.Empty;
    public bool IsConnectedToDocker => _manager.IsConnectedToDocker;
    public bool IsDemoMode => _manager.ForceDemoMode || !_manager.IsConnectedToDocker;
    public string EnvironmentName => _env.EnvironmentName;
    public string EngineDescription => _manager.IsConnectedToDocker ? "Docker Engine (Native Pipe/Socket)" : "Simulated In-Memory Engine (Live Demo Mode)";
    public int TelemetryIntervalMs { get; private set; } = 1200;
    public string LicenseTier => "Fleptix Open-Core (Free / Developer)";

    public SettingsModel(
        ContainerServiceManager manager, 
        IWebHostEnvironment env)
    {
        _manager = manager;
        _env = env;
    }

    public void OnGet()
    {
        DaemonEndpoint = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";
    }
}
