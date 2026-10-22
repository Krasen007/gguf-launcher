namespace GgufLauncher.AutoTune;

/// <summary>
/// The knobs the user can turn. Everything that is left at null is decided by the estimator
/// from the size class of the model and the VRAM of the GPU.
/// </summary>
public sealed class AutoTuneOptions
{
    /// <summary>Whether the launcher is allowed to derive the launch parameters at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// VRAM in MiB that is kept free for the driver, the desktop and the Vulkan runtime.
    /// </summary>
    public int MarginMiB { get; set; } = 1024;

    /// <summary>Context window to ask for. Null lets the estimator decide.</summary>
    public int? ContextSize { get; set; }

    /// <summary>
    /// Number of layers to offload to the GPU. Null lets the estimator decide, and -1 means
    /// "offload everything".
    /// </summary>
    public int? GpuLayers { get; set; }

    /// <summary>Quantisation of the key part of the KV cache, for example <c>q8_0</c>.</summary>
    public string? CacheTypeK { get; set; }

    /// <summary>Quantisation of the value part of the KV cache, for example <c>q8_0</c>.</summary>
    public string? CacheTypeV { get; set; }

    /// <summary>Logical batch size. Null lets the estimator decide.</summary>
    public int? BatchSize { get; set; }

    /// <summary>Micro batch size. Null lets the estimator decide.</summary>
    public int? MicroBatchSize { get; set; }

    /// <summary>Number of CPU threads. Null lets the estimator decide.</summary>
    public int? Threads { get; set; }

    /// <summary>Ceiling for the context the estimator picks. Null or zero means no ceiling.</summary>
    public int? MaxContextSize { get; set; }

    /// <summary>Whether the estimator may leave the multimodal projector out when it does not fit.</summary>
    public bool AllowProjectorSkip { get; set; } = true;

    /// <summary>Whether the estimator may leave the draft model out when it does not fit.</summary>
    public bool AllowDraftModelSkip { get; set; } = true;
}
