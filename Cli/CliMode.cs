namespace GgufLauncher.Cli;

/// <summary>The operation the launcher was asked to perform.</summary>
public enum CliMode
{
    /// <summary>Start the server for a model file.</summary>
    Launch,

    /// <summary>Make <c>.gguf</c> files open with this launcher.</summary>
    Register,

    /// <summary>Remove the <c>.gguf</c> file association again.</summary>
    Unregister,

    /// <summary>Show the configuration, the server location and the current association.</summary>
    Status,

    /// <summary>Print the usage text and exit.</summary>
    Help,
}
