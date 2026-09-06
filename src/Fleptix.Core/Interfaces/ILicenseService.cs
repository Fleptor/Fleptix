namespace Fleptix.Core.Interfaces;

/// <summary>
/// Service contract for license capabilities and feature enablement.
/// </summary>
public interface ILicenseService
{
    /// <summary>
    /// Determines whether the Time Machine feature is enabled.
    /// </summary>
    bool IsTimeMachineEnabled();
}
