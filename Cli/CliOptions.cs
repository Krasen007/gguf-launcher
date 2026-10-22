namespace GgufLauncher.Cli;

/// <summary>Parsed command line.</summary>
public sealed class CliOptions
{
    /// <summary>Operation the launcher has to perform.</summary>
    public CliMode Mode { get; init; } = CliMode.Launch;

    /// <summary>Model file to serve; null when the launcher was not asked to start anything.</summary>
    public string? ModelPath { get; init; }

    /// <summary>Explicit path of the configuration file, overriding the search.</summary>
    public string? ConfigPath { get; init; }

    /// <summary>Explicit path of <c>llama-server.exe</c>, overriding the search.</summary>
    public string? LlamaServerPath { get; init; }

    /// <summary>Port to listen on.</summary>
    public int? Port { get; init; }

    /// <summary>Interface the server binds to.</summary>
    public string? Host { get; init; }

    /// <summary>Context size to request; null lets the launcher derive one from the model.</summary>
    public int? ContextSize { get; init; }

    /// <summary>Number of CPU threads for the server.</summary>
    public int? Threads { get; init; }

    /// <summary>Number of layers to offload to the graphics card.</summary>
    public int? GpuLayers { get; init; }

    /// <summary>Whether the launcher tunes the flags for the free video memory; null keeps the configured value.</summary>
    public bool? AutoTune { get; init; }

    /// <summary>Video memory to keep free for everything else, in MiB.</summary>
    public int? VramMarginMiB { get; init; }

    /// <summary>Quantisation of both parts of the KV cache, for example <c>q8_0</c>.</summary>
    public string? CacheType { get; init; }

    /// <summary>Logical batch size.</summary>
    public int? BatchSize { get; init; }

    /// <summary>Physical batch size, the one that sizes the compute buffer.</summary>
    public int? UbatchSize { get; init; }

    /// <summary>Whether the browser is opened once the server is ready.</summary>
    public bool? OpenBrowser { get; init; }

    /// <summary>Whether the built-in web interface is switched off.</summary>
    public bool? NoWebUi { get; init; }

    /// <summary>Whether the expert weights stay in system memory.</summary>
    public bool? CpuMoe { get; init; }

    /// <summary>Multimodal projector to use instead of the one found next to the model.</summary>
    public string? Mmproj { get; init; }

    /// <summary>Draft model to use for speculative decoding.</summary>
    public string? SpecDraftModel { get; init; }

    /// <summary>Speculative decoding strategy, for example <c>draft-dspark</c>.</summary>
    public string? SpecType { get; init; }

    /// <summary>Whether the launcher only prints the command line instead of running it.</summary>
    public bool DryRun { get; init; }

    /// <summary>Whether the launcher keeps its output to a minimum.</summary>
    public bool Quiet { get; init; }

    /// <summary>Parses the argument list. Throws <see cref="ArgumentException"/> on bad input.</summary>
    public static CliOptions Parse(string[] args)
    {
        return CliOptionParser.Parse(args);
    }
}
