namespace Fleptix.Observer.Controllers;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/license")]
public class LicenseApiController : ControllerBase
{
    private readonly ILicenseService _licenseService;
    private readonly ILogger<LicenseApiController> _logger;

    public LicenseApiController(
        ILicenseService licenseService,
        ILogger<LicenseApiController> logger)
    {
        _licenseService = licenseService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current active license status, tier name, and entitlements.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var info = _licenseService.GetLicenseInfo();
        return Ok(info);
    }

    /// <summary>
    /// Activates a Lemon Squeezy license key in-place.
    /// </summary>
    [HttpPost("activate")]
    public async Task<IActionResult> Activate(
        [FromBody] LicenseActivationRequest request, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.LicenseKey))
        {
            return BadRequest(new LicenseActivationResult
            {
                Success = false,
                Message = "License key is required."
            });
        }

        var result = await _licenseService.ActivateLicenseAsync(request.LicenseKey, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Deactivates the current license key and reverts to Community edition.
    /// </summary>
    [HttpPost("deactivate")]
    public async Task<IActionResult> Deactivate(CancellationToken cancellationToken)
    {
        var result = await _licenseService.DeactivateLicenseAsync(cancellationToken);
        return Ok(result);
    }
}
