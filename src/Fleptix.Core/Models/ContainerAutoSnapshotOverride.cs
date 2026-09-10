namespace Fleptix.Core.Models;

/// <summary>
/// Per-container override for automated Time Machine snapshots.
/// </summary>
public enum ContainerAutoSnapshotOverride
{
    /// <summary>
    /// Inherit the global default AutoSnapshotEnabled setting.
    /// </summary>
    UseGlobalDefault = 0,

    /// <summary>
    /// Always perform automated scheduled snapshots for this container, regardless of the global default.
    /// </summary>
    AlwaysOn = 1,

    /// <summary>
    /// Never perform automated scheduled snapshots for this container, regardless of the global default.
    /// </summary>
    AlwaysOff = 2
}
