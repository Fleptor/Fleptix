namespace Fleptix.Core.Models;

/// <summary>
/// Execution output and process exit metadata from container command execution.
/// </summary>
public class ContainerExecResult
{
    /// <summary>
    /// Indicates whether the command execution started and exited with code 0.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Process exit status code. Typically 0 indicates success.
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Captured standard output stream.
    /// </summary>
    public string Stdout { get; set; } = string.Empty;

    /// <summary>
    /// Captured standard error stream.
    /// </summary>
    public string Stderr { get; set; } = string.Empty;

    /// <summary>
    /// Error message if the execution failed to initiate or container was in an invalid state.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Process execution duration in milliseconds.
    /// </summary>
    public long DurationMs { get; set; }
}
