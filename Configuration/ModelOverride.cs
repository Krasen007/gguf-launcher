namespace GgufLauncher.Configuration;

/// <summary>
/// Per-model overrides layered on top of <see cref="LauncherConfig"/>.
/// A model is matched by full path, file name, or file name without extension,
/// in that order, so a short key like "MiniCPM-V-4_7-1B-F16" is enough.
/// </summary>
public sealed class ModelOverride
{
    /// <summary>Port for this model.</summary>
    public int? Port { get; set; }

    /// <summary>Interface the server binds to for this model.</summary>
    public string? Host { get; set; }

    /// <summary>Context size for this model; used as given, without the global cap.</summary>
    public int? ContextSize { get; set; }

    /// <summary>CPU threads for this model.</summary>
    public int? Threads { get; set; }

    /// <summary>Layers offloaded to the graphics card for this model.</summary>
    public int? GpuLayers { get; set; }

    /// <summary>
    /// Settings of the automatic tuner for this model. They are layered on top of the
    /// global <see cref="LauncherConfig.AutoTune"/> section, so a key that is left out
    /// keeps the global value.
    /// </summary>
    public LauncherConfig.AutoTuneConfig? AutoTune { get; set; }

    /// <summary>Whether flash attention is used for this model.</summary>
    public bool? FlashAttention { get; set; }

    /// <summary>Whether the Jinja chat template is used for this model.</summary>
    public bool? Jinja { get; set; }

    /// <summary>Number of server slots for this model.</summary>
    public int? ParallelSlots { get; set; }

    /// <summary>Whether the browser is opened for this model.</summary>
    public bool? OpenBrowser { get; set; }

    /// <summary>Whether the built-in web interface is switched off for this model.</summary>
    public bool? NoWebUi { get; set; }

    /// <summary>Whether the expert weights stay in system memory for this model.</summary>
    public bool? CpuMoe { get; set; }

    /// <summary>Number of tokens drafted per speculative step for this model.</summary>
    public int? SpecDraftNMax { get; set; }

    /// <summary>
    /// Whether llama.cpp may sample the draft model on the GPU backend for this
    /// model. Off by default, because the Vulkan backend aborts the server when it
    /// does.
    /// </summary>
    public bool? DraftBackendSampling { get; set; }

    /// <summary>KV cache data type for K for this model.</summary>
    public string? CacheTypeK { get; set; }

    /// <summary>KV cache data type for V for this model.</summary>
    public string? CacheTypeV { get; set; }

    /// <summary>Multimodal projector for this model.</summary>
    public string? Mmproj { get; set; }

    /// <summary>Draft model for speculative decoding with this model.</summary>
    public string? SpecDraftModel { get; set; }

    /// <summary>Speculative decoding strategy for this model.</summary>
    public string? SpecType { get; set; }

    /// <summary>Raw llama.cpp flags for this model; replaces the global list.</summary>
    public List<string>? ExtraArgs { get; set; }
}
