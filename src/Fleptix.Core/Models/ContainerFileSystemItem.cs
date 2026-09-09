namespace Fleptix.Core.Models;

/// <summary>
/// Represents a file, directory, or link item within a container's filesystem.
/// </summary>
public class ContainerFileSystemItem
{
    /// <summary>
    /// File or directory name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path within the container.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// True if the item is a directory.
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// File size in bytes. 0 for directories.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Classification type: directory, file, symlink, block, char, fifo, etc.
    /// </summary>
    public string Type { get; set; } = "file";

    /// <summary>
    /// Last modification timestamp if available.
    /// </summary>
    public DateTimeOffset? ModifiedTime { get; set; }

    /// <summary>
    /// Target path if this item is a symbolic link.
    /// </summary>
    public string? LinkTarget { get; set; }

    /// <summary>
    /// Unix file mode / permissions if available.
    /// </summary>
    public string? Mode { get; set; }
}
