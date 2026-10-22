namespace GgufLauncher.Launch;

/// <summary>A resolved llama.cpp server executable and how it must be invoked.</summary>
/// <param name="ExecutablePath">Absolute path to the executable.</param>
/// <param name="UseServeSubcommand">
/// True for the unified <c>llama.exe</c> front-end, which needs the <c>serve</c>
/// subcommand. False for the dedicated <c>llama-server.exe</c>.
/// </param>
public sealed record LlamaServer(string ExecutablePath, bool UseServeSubcommand)
{
    /// <summary>File name of the executable, used for the console banner and log lines.</summary>
    public string FileName => Path.GetFileName(ExecutablePath);

    /// <summary>
    /// How the server is named to the user: the unified front-end needs its
    /// <c>serve</c> subcommand spelled out, the dedicated server does not.
    /// </summary>
    public string Display => UseServeSubcommand ? $"{FileName} serve" : FileName;
}
