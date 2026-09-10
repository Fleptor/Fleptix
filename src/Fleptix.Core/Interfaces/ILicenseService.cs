namespace Fleptix.Core.Interfaces;

/// <summary>
/// Operational license tiers for Fleptix Observer and Time Machine.
/// </summary>
public enum LicenseTier
{
    /// <summary>
    /// Free open-source edition (Observer telemetry and container controls).
    /// </summary>
    Community,

    /// <summary>
    /// Time Machine personal/homelab license (1 node, non-commercial).
    /// </summary>
    Personal,

    /// <summary>
    /// Time Machine commercial pro license (up to 3 nodes, business SLA).
    /// </summary>
    Commercial,

    /// <summary>
    /// Development or evaluation mode enabled via environment variables.
    /// </summary>
    Evaluation
}

/// <summary>
/// Service contract for license capabilities and feature enablement.
/// </summary>
public interface ILicenseService
{
    /// <summary>
    /// Determines whether the Time Machine feature is enabled.
    /// </summary>
    bool IsTimeMachineEnabled();

    /// <summary>
    /// Gets the current active license tier.
    /// </summary>
    LicenseTier GetLicenseTier();

    /// <summary>
    /// Gets the human-readable display name of the current tier.
    /// </summary>
    string GetTierDisplayName();

    /// <summary>
    /// Gets the maximum allowed concurrent Docker nodes for this license tier.
    /// </summary>
    int GetMaxAllowedNodes();
}
