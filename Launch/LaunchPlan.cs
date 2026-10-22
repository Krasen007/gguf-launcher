namespace GgufLauncher.Launch;

/// <summary>Everything needed to start a server: the command line plus how to reach it.</summary>
public sealed record LaunchPlan
{
    /// <summary>
    /// Absolute path to llama-server.exe, the binary the launcher starts. It is
    /// resolved before the plan is built so the caller never has to search again.
    /// </summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// Full argument list, already including the "serve" subcommand when required.
    /// It is passed to the process verbatim, so its order is part of the contract.
    /// </summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>
    /// Interface the server binds to. The launcher keeps it beside the plan so the
    /// health check can probe the same address the server actually listens on.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// TCP port the server listens on. It is the port that was checked for
    /// availability, so it is guaranteed to be the one the child process binds.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Browser URL for the built-in web UI. Wildcard bind addresses are mapped
    /// back to loopback so the link always works on the machine that started it.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Directory the server process is started in. It is the folder that holds the
    /// model, so relative paths inside the model folder resolve predictably.
    /// </summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Human-readable remarks to show in the console banner, for example when the
    /// context window was capped or a companion file was missing.
    /// </summary>
    public required IReadOnlyList<string> Notes { get; init; }
}
