namespace Fleptix.Core.Models;

/// <summary>
/// Request parameters for executing a command inside a target container.
/// </summary>
public class ContainerExecRequest
{
    /// <summary>
    /// The command line string to execute.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// The shell binary to invoke the command with (e.g. "/bin/sh", "/bin/bash", "/bin/ash", or "raw").
    /// </summary>
    public string Shell { get; set; } = "/bin/sh";

    /// <summary>
    /// Working directory for the process inside the container.
    /// </summary>
    public string? WorkingDir { get; set; }

    /// <summary>
    /// Username or UID (and optionally groupname/GID) to run the process as.
    /// </summary>
    public string? User { get; set; }

    /// <summary>
    /// Runs the exec process with extended privileges.
    /// </summary>
    public bool Privileged { get; set; }
}
