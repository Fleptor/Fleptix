namespace Fleptix.Observer.Controllers;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// API endpoints for global Fleptix configuration and Time Machine retention settings.
/// </summary>
[ApiController]
[Route("api/settings")]
public class SettingsApiController : ControllerBase
{
    private readonly ITimeMachineSettingsStore _settingsStore;
    private readonly ILicenseService _licenseService;

    public SettingsApiController(
        ITimeMachineSettingsStore settingsStore,
        ILicenseService licenseService)
    {
        _settingsStore = settingsStore;
        _licenseService = licenseService;
    }

    /// <summary>
    /// Gets the current global Time Machine retention and auto-snapshot configuration.
    /// </summary>
    [HttpGet("retention")]
    [HttpGet("timemachine/retention")]
    [HttpGet("/api/timemachine/retention")]
    public IActionResult GetRetentionSettings()
    {
        if (!_licenseService.IsTimeMachineEnabled())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Time Machine settings are not accessible in observer-only mode." });
        }

        var settings = _settingsStore.GetGlobalSettings();
        return Ok(settings);
    }

    /// <summary>
    /// Updates the global Time Machine retention and auto-snapshot configuration.
    /// </summary>
    [HttpPut("retention")]
    [HttpPut("timemachine/retention")]
    [HttpPut("/api/timemachine/retention")]
    public IActionResult UpdateRetentionSettings([FromBody] TimeMachineRetentionSettings settings)
    {
        if (!_licenseService.IsTimeMachineEnabled())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Time Machine settings are not accessible in observer-only mode." });
        }

        if (settings == null)
        {
            return BadRequest(new { message = "Settings payload is required." });
        }

        _settingsStore.UpdateGlobalSettings(settings);
        return Ok(_settingsStore.GetGlobalSettings());
    }
}
