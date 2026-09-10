namespace Fleptix.Core.Interfaces;

using Fleptix.Core.Models;

/// <summary>
/// Contract for managing and persisting global Time Machine retention settings and per-container overrides.
/// </summary>
public interface ITimeMachineSettingsStore
{
    /// <summary>
    /// Gets the current global default retention and auto-snapshot configuration.
    /// </summary>
    TimeMachineRetentionSettings GetGlobalSettings();

    /// <summary>
    /// Updates the global default retention and auto-snapshot configuration.
    /// </summary>
    void UpdateGlobalSettings(TimeMachineRetentionSettings settings);

    /// <summary>
    /// Gets the auto-snapshot override state for a specific container.
    /// </summary>
    ContainerAutoSnapshotOverride GetContainerOverride(string containerId);

    /// <summary>
    /// Sets the auto-snapshot override state for a specific container.
    /// </summary>
    void SetContainerOverride(string containerId, ContainerAutoSnapshotOverride overrideState);

    /// <summary>
    /// Gets all configured per-container overrides.
    /// </summary>
    IReadOnlyDictionary<string, ContainerAutoSnapshotOverride> GetAllContainerOverrides();

    /// <summary>
    /// Resolves whether auto-snapshot is effectively enabled for a container,
    /// considering AlwaysOn, AlwaysOff, or falling back to the global default.
    /// </summary>
    bool IsAutoSnapshotEnabledForContainer(string containerId);
}
