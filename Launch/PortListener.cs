namespace GgufLauncher.Launch;

/// <summary>Identifies whatever is currently listening on a TCP port.</summary>
/// <param name="Port">The port that was queried.</param>
/// <param name="ProcessId">Owning process id, or null when it could not be determined.</param>
/// <param name="ProcessName">Owning process name, or null when it could not be determined.</param>
public sealed record PortListener(int Port, int? ProcessId, string? ProcessName)
{
    /// <summary>
    /// Renders the owner of the port as a console-ready sentence, so the launcher can tell
    /// the user who already holds the port before it falls back to a different one.
    /// </summary>
    /// <returns>A sentence such as <c>port 8080 is already in use by pid 1234 (pwsh)</c>.</returns>
    public string Describe()
    {
        string owner = ProcessId is { } pid
            ? $"pid {pid}{(ProcessName is null ? string.Empty : $" ({ProcessName})")}"
            : "an unknown process";

        return $"port {Port} is already in use by {owner}";
    }
}
