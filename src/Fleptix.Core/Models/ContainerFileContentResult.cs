namespace Fleptix.Core.Models;

/// <summary>
/// Result containing file content and metadata retrieved from a container.
/// </summary>
public class ContainerFileContentResult
{
    /// <summary>
    /// Indicates whether the file was successfully retrieved from the container.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Target container ID.
    /// </summary>
    public string ContainerId { get; set; } = string.Empty;

    /// <summary>
    /// Full path of the file inside the container.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Filename without directory prefix.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Size of the file in bytes.
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// Indicates whether the file content is plain text or binary.
    /// </summary>
    public bool IsText { get; set; }

    /// <summary>
    /// Text content of the file if IsText is true.
    /// </summary>
    public string? ContentText { get; set; }

    /// <summary>
    /// Error message if retrieval failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
