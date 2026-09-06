namespace Fleptix.Observer.Services;

using Fleptix.Core.Interfaces;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Service that determines feature availability and licensing,
/// reading from configuration (TimeMachine:Enabled) and overridable via the TIMEMACHINE_ENABLED environment variable.
/// </summary>
public class LicenseService : ILicenseService
{
    private readonly IConfiguration _configuration;

    public LicenseService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Checks if Time Machine is enabled via the TIMEMACHINE_ENABLED environment variable or TimeMachine:Enabled in configuration.
    /// </summary>
    public bool IsTimeMachineEnabled()
    {
        var envVar = Environment.GetEnvironmentVariable("TIMEMACHINE_ENABLED");
        if (!string.IsNullOrWhiteSpace(envVar) && bool.TryParse(envVar, out var envEnabled))
        {
            return envEnabled;
        }

        return _configuration.GetValue<bool>("TimeMachine:Enabled");
    }
}
