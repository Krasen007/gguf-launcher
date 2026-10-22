using GgufLauncher.AutoTune;
using GgufLauncher.Cli;
using GgufLauncher.Configuration;

namespace GgufLauncher.Launch;

/// <summary>
/// The fully resolved settings for one launch, after merging the global config,
/// the per-model overrides and the command line. Later sources win.
/// </summary>
public sealed record LaunchSettings
{
    /// <summary>
    /// Interface the server binds to. The default exposes the server to the local
    /// network, while a loopback address keeps it on this machine.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// TCP port the server listens on. It is the port the launcher opens in the
    /// browser, and the port it moves away from when something else is listening.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Context window in tokens, asked for explicitly by the user. When it is null
    /// the launcher derives a safe value from the model instead of trusting the
    /// llama.cpp default, which is often far too large for the available memory.
    /// </summary>
    public int? ContextSize { get; init; }

    /// <summary>
    /// Upper bound for the automatically derived context window. Models advertise
    /// contexts far larger than a typical GPU can hold, so the derived value is
    /// capped here to keep the server from dying with an out of memory error.
    /// </summary>
    public int MaxContextSize { get; init; } = 65536;

    /// <summary>
    /// Number of CPU threads llama.cpp uses for inference. Zero lets llama.cpp
    /// pick a value from the machine instead of the user guessing it.
    /// </summary>
    public int Threads { get; init; }

    /// <summary>
    /// Logical batch size. Zero leaves the llama.cpp default in place.
    /// </summary>
    public int BatchSize { get; init; }

    /// <summary>
    /// Physical batch size, the one that sizes the compute buffer. Zero leaves the
    /// llama.cpp default in place.
    /// </summary>
    public int MicroBatchSize { get; init; }

    /// <summary>
    /// How many model layers are offloaded to the GPU. More layers mean faster
    /// inference but need more VRAM, while zero keeps the whole model on the CPU.
    /// Null means the launcher does not know yet and lets the automatic tuner decide.
    /// </summary>
    public int? GpuLayers { get; init; }

    /// <summary>
    /// Whether flash attention is enabled. It cuts KV cache memory use and speeds
    /// up long contexts, which matters most on a shared consumer GPU.
    /// </summary>
    public bool FlashAttention { get; init; }

    /// <summary>
    /// Whether the chat template is taken from the model's own metadata in Jinja
    /// form. It has to be explicit because llama.cpp cannot always infer it, and
    /// a wrong template silently breaks the model's answer formatting.
    /// </summary>
    public bool Jinja { get; init; }

    /// <summary>
    /// How many parallel request slots the server keeps open. More slots let
    /// several clients generate at the same time at the cost of KV cache memory.
    /// </summary>
    public int ParallelSlots { get; init; }

    /// <summary>
    /// Whether the launcher opens the web UI once the server reports itself
    /// healthy, so a double-click does not end at a console the user cannot use.
    /// </summary>
    public bool OpenBrowser { get; init; }

    /// <summary>
    /// Whether the built-in web UI of llama-server is switched off. It exists for
    /// setups that only talk to the HTTP API and do not want the UI served.
    /// </summary>
    public bool NoWebUi { get; init; }

    /// <summary>
    /// Whether the mixture-of-experts layers run on the CPU. It keeps VRAM free
    /// for the rest of the model when the experts alone do not fit.
    /// </summary>
    public bool CpuMoe { get; init; }

    /// <summary>
    /// How many tokens the speculative draft model may propose per step. A larger
    /// value trades memory for a higher acceptance rate of the drafted tokens.
    /// </summary>
    public int SpecDraftNMax { get; init; }

    /// <summary>
    /// Whether llama.cpp may sample the draft model on the GPU backend. The Vulkan
    /// backend aborts the whole server on the first request when it does, so the
    /// launcher keeps that step on the CPU unless it is switched on explicitly.
    /// </summary>
    public bool DraftBackendSampling { get; init; }

    /// <summary>
    /// Quantisation used for the key part of the KV cache. It trades a little
    /// cache quality for a large saving in memory on long contexts.
    /// </summary>
    public string? CacheTypeK { get; init; }

    /// <summary>
    /// Quantisation used for the value part of the KV cache. It is set apart from
    /// the key type because the value cache tolerates coarse types better.
    /// </summary>
    public string? CacheTypeV { get; init; }

    /// <summary>
    /// Explicit multimodal projector file. It overrides the mmproj file the
    /// launcher would otherwise pick up next to the model.
    /// </summary>
    public string? Mmproj { get; init; }

    /// <summary>
    /// Explicit speculative decoding draft model. It overrides the draft model
    /// the launcher would otherwise find beside the model.
    /// </summary>
    public string? SpecDraftModel { get; init; }

    /// <summary>
    /// Strategy the draft model is used with. It has to match the draft model,
    /// because llama.cpp ignores a draft whose strategy it cannot apply.
    /// </summary>
    public string? SpecType { get; init; }

    /// <summary>
    /// Extra llama.cpp arguments passed through verbatim. They are appended last
    /// so that they can override anything the launcher derived on its own.
    /// </summary>
    public IReadOnlyList<string> ExtraArgs { get; init; } = [];

    /// <summary>
    /// The plan of the automatic tuner, when it ran. Downstream code can look at it to
    /// see what was decided and why.
    /// </summary>
    public AutoTuneResult? Tuning { get; init; }

    /// <summary>
    /// The remarks of the automatic tuner. They explain in the console which numbers
    /// were chosen and what they cost.
    /// </summary>
    public IReadOnlyList<string> TuningNotes { get; init; } = [];

    /// <summary>
    /// Merges the three sources of truth. The command line has the final word
    /// because it is the most immediate expression of intent.
    /// </summary>
    public static LaunchSettings Resolve(LauncherConfig config, ModelOverride? overrides, CliOptions cli)
    {
        List<string> extra = [];
        List<string>? overrideArgs = overrides?.ExtraArgs;
        if (overrideArgs is not null && overrideArgs.Count > 0)
        {
            extra.AddRange(overrideArgs);
        }
        else
        {
            extra.AddRange(config.ExtraArgs);
        }

        // The automatic tuner has its own section in the configuration, so its values
        // are merged in as well. A per-model section wins over the global one, and the
        // command line wins over both.
        LauncherConfig.AutoTuneConfig global = config.AutoTune;
        LauncherConfig.AutoTuneConfig? local = overrides?.AutoTune;

        return new LaunchSettings
        {
            Host = cli.Host ?? overrides?.Host ?? config.Host,
            Port = cli.Port ?? overrides?.Port ?? config.Port,
            ContextSize = cli.ContextSize
                ?? overrides?.ContextSize
                ?? local?.ContextSize
                ?? config.ContextSize
                ?? global.ContextSize,
            MaxContextSize = config.MaxContextSize,
            Threads = cli.Threads
                ?? overrides?.Threads
                ?? local?.Threads
                ?? (config.Threads > 0 ? config.Threads : (int?)null)
                ?? global.Threads
                ?? 0,
            BatchSize = cli.BatchSize ?? local?.BatchSize ?? global.BatchSize ?? 0,
            MicroBatchSize = cli.UbatchSize ?? local?.MicroBatchSize ?? global.MicroBatchSize ?? 0,
            GpuLayers = cli.GpuLayers
                ?? overrides?.GpuLayers
                ?? local?.GpuLayers
                ?? config.GpuLayers
                ?? global.GpuLayers,
            FlashAttention = overrides?.FlashAttention ?? config.FlashAttention,
            Jinja = overrides?.Jinja ?? config.Jinja,
            ParallelSlots = overrides?.ParallelSlots ?? config.ParallelSlots,
            OpenBrowser = cli.OpenBrowser ?? overrides?.OpenBrowser ?? config.OpenBrowser,
            NoWebUi = cli.NoWebUi ?? overrides?.NoWebUi ?? config.NoWebUi,
            CpuMoe = cli.CpuMoe ?? overrides?.CpuMoe ?? config.CpuMoe,
            SpecDraftNMax = overrides?.SpecDraftNMax ?? config.SpecDraftNMax,
            DraftBackendSampling = overrides?.DraftBackendSampling ?? config.DraftBackendSampling ?? false,
            CacheTypeK = cli.CacheType
                ?? overrides?.CacheTypeK
                ?? local?.CacheTypeK
                ?? config.CacheTypeK
                ?? global.CacheTypeK,
            CacheTypeV = cli.CacheType
                ?? overrides?.CacheTypeV
                ?? local?.CacheTypeV
                ?? config.CacheTypeV
                ?? global.CacheTypeV,
            Mmproj = cli.Mmproj ?? overrides?.Mmproj ?? config.Mmproj,
            SpecDraftModel = cli.SpecDraftModel ?? overrides?.SpecDraftModel ?? config.SpecDraftModel,
            SpecType = cli.SpecType ?? overrides?.SpecType ?? config.SpecType,
            ExtraArgs = extra,
        };
    }
}
