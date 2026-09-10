namespace Fleptix.Observer.Services;

using Fleptix.Core.Interfaces;
using Fleptix.Core.Models;

/// <summary>
/// Null Object pattern implementation of ITimeMachineSettingsStore for the open-source Fleptix Observer edition.
/// Used when the commercial Fleptix.TimeMachine extension is not present or activated.
/// </summary>
public class NullTimeMachineSettingsStore : ITimeMachineSettingsStore
{
    private static readonly TimeMachineRetentionSettings DefaultSettings = new()
    {
        AutoSnapshotEnabled = false,
        IntervalHours = 24,
        MaxSnapshotCount = 10,
        MaxTotalStorageMB = 1024,
        NeverDeleteOldSnapshots = false
    };

    public TimeMachineRetentionSettings GetGlobalSettings() => DefaultSettings;

    public void UpdateGlobalSettings(TimeMachineRetentionSettings settings)
    {
        // No-op in open-source community edition
    }

    public ContainerAutoSnapshotOverride GetContainerOverride(string containerId) => ContainerAutoSnapshotOverride.UseGlobalDefault;

    public void SetContainerOverride(string containerId, ContainerAutoSnapshotOverride overrideState)
    {
        // No-op in open-source community edition
    }

    public IReadOnlyDictionary<string, ContainerAutoSnapshotOverride> GetAllContainerOverrides() =>
        new Dictionary<string, ContainerAutoSnapshotOverride>();

    public bool IsAutoSnapshotEnabledForContainer(string containerId) => false;
}
