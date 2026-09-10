namespace Fleptix.Observer.Services;

using Fleptix.Core.Interfaces;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Service that determines feature availability, tier classification, and licensing entitlements,
/// reading from FLEPTIX_LICENSE_KEY, configuration, or evaluation environment variables.
/// </summary>
public class LicenseService : ILicenseService
{
    private readonly IConfiguration _configuration;

    public LicenseService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// Checks if Time Machine features are enabled via an active commercial/personal license or evaluation flag.
    /// </summary>
    public bool IsTimeMachineEnabled()
    {
        var tier = GetLicenseTier();
        return tier is LicenseTier.Personal or LicenseTier.Commercial or LicenseTier.Evaluation;
    }

    /// <summary>
    /// Resolves the current license tier from the configured license key or environment flags.
    /// Supports distinct prefixes: FLEPTIX-PRO- (Commercial) vs FLEPTIX-PERS- (Personal).
    /// </summary>
    public LicenseTier GetLicenseTier()
    {
        var licenseKey = Environment.GetEnvironmentVariable("FLEPTIX_LICENSE_KEY")
            ?? _configuration["TimeMachine:LicenseKey"]
            ?? _configuration["Fleptix:LicenseKey"];

        if (!string.IsNullOrWhiteSpace(licenseKey))
        {
            licenseKey = licenseKey.Trim();

            // Commercial / Team Tier (Prefix: FLEPTIX-PRO-)
            if (licenseKey.StartsWith("FLEPTIX-PRO-", StringComparison.OrdinalIgnoreCase))
            {
                return LicenseTier.Commercial;
            }

            // Personal / Homelab Tier (Prefix: FLEPTIX-PERS-)
            if (licenseKey.StartsWith("FLEPTIX-PERS-", StringComparison.OrdinalIgnoreCase))
            {
                return LicenseTier.Personal;
            }

            // Generic valid license string fallback
            return LicenseTier.Personal;
        }

        // Development / Evaluation override
        var envVar = Environment.GetEnvironmentVariable("TIMEMACHINE_ENABLED");
        if (!string.IsNullOrWhiteSpace(envVar) && bool.TryParse(envVar, out var envEnabled) && envEnabled)
        {
            return LicenseTier.Evaluation;
        }

        if (_configuration.GetValue<bool>("TimeMachine:Enabled"))
        {
            return LicenseTier.Evaluation;
        }

        return LicenseTier.Community;
    }

    /// <summary>
    /// Gets the human-readable display name for the active tier.
    /// </summary>
    public string GetTierDisplayName() => GetLicenseTier() switch
    {
        LicenseTier.Commercial => "Time Machine Pro (Commercial Edition)",
        LicenseTier.Personal => "Time Machine (Personal / Homelab)",
        LicenseTier.Evaluation => "Time Machine (Evaluation Mode)",
        _ => "Fleptix Observer (Community Open-Core)"
    };

    /// <summary>
    /// Gets the maximum allowed concurrent Docker nodes for the active tier.
    /// </summary>
    public int GetMaxAllowedNodes() => GetLicenseTier() switch
    {
        LicenseTier.Commercial => 3,
        LicenseTier.Personal => 1,
        LicenseTier.Evaluation => 1,
        _ => 1
    };
}
