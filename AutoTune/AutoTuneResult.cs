namespace GgufLauncher.AutoTune;

/// <summary>
/// The launch parameters the estimator settled on, together with the reasons for them.
/// </summary>
/// <param name="ContextSize">The context window in tokens.</param>
/// <param name="GpuLayers">
/// The number of layers to offload to the GPU. -1 means "offload everything".
/// </param>
/// <param name="CacheTypeK">The quantisation of the key part of the KV cache.</param>
/// <param name="CacheTypeV">The quantisation of the value part of the KV cache.</param>
/// <param name="BatchSize">The logical batch size.</param>
/// <param name="MicroBatchSize">The micro batch size.</param>
/// <param name="Threads">The number of CPU threads.</param>
/// <param name="UseProjector">False when the multimodal projector was left out.</param>
/// <param name="UseDraftModel">False when the draft model was left out.</param>
/// <param name="Notes">
/// Short sentences that explain the decision. The estimator always fills this list and
/// passes <see cref="Array.Empty{T}"/> when there is nothing to report, so it is never null.
/// </param>
public sealed record AutoTuneResult(
    int ContextSize,
    int GpuLayers,
    string CacheTypeK,
    string CacheTypeV,
    int BatchSize,
    int MicroBatchSize,
    int Threads,
    bool UseProjector,
    bool UseDraftModel,
    IReadOnlyList<string> Notes);
