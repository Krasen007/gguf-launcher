using System.Globalization;

namespace GgufLauncher.AutoTune;

/// <summary>
/// Computes the launch parameters that fit a model into the VRAM of a GPU.
/// </summary>
/// <remarks>
/// The estimator is a pure function. It never touches the file system, never starts a
/// process and reads nothing but the processor count of the machine. The constants it uses
/// are calibrated against measurements of llama.cpp, see the README next to this file.
/// </remarks>
public static class VramEstimator
{
    /// <summary>One MiB in bytes.</summary>
    private const double MiB = 1024d * 1024d;

    /// <summary>
    /// Share of the free VRAM the estimator plans for. The rest is left to the driver, the
    /// desktop and the Vulkan runtime.
    /// </summary>
    private const double SafetyFactor = 0.92;

    /// <summary>The smallest context the estimator plans for, the same floor llama.cpp uses.</summary>
    private const int MinimumContextSize = 4096;

    /// <summary>Context sizes are rounded down to a multiple of this, like llama.cpp does.</summary>
    private const int ContextStep = 256;

    /// <summary>The VRAM the compute buffers need on top of the logits tensor, in MiB.</summary>
    private const double ComputeBufferMiB = 64;

    /// <summary>Bytes per element of the logits tensor, which is always f32.</summary>
    private const double LogitsBytesPerElement = 4d;

    /// <summary>Layer count used when the model does not announce one.</summary>
    private const ulong FallbackBlockCount = 32;

    /// <summary>Vocabulary size used when the model does not announce one.</summary>
    private const ulong FallbackVocabularyCount = 262_144;

    /// <summary>Head width used when the model announces neither a head width nor an embedding.</summary>
    private const ulong FallbackHeadDim = 256;

    /// <summary>Bytes per weight assumed for a quantization the estimator does not know.</summary>
    private const double UnknownQuantBytesPerElement = 0.5625d;

    /// <summary>Bytes per element assumed for an unknown KV cache type, the f16 default of llama.cpp.</summary>
    private const double UnknownCacheBytesPerElement = 2d;

    /// <summary>The largest number of notes the result carries.</summary>
    private const int MaxNotes = 8;

    /// <summary>The longest note the result carries.</summary>
    private const int MaxNoteLength = 119;

    /// <summary>Computes launch parameters that fit the given GPU.</summary>
    /// <param name="facts">What is known about the model.</param>
    /// <param name="gpu">The GPU the model should run on.</param>
    /// <param name="options">The user overrides and the margin to keep free.</param>
    /// <param name="projectorBytes">The size of the multimodal projector, when there is one.</param>
    /// <param name="draftBytes">The size of the draft model, when there is one.</param>
    /// <returns>The plan and the reasons behind it.</returns>
    public static AutoTuneResult Plan(
        ModelFacts facts,
        GpuProfile gpu,
        AutoTuneOptions options,
        long? projectorBytes,
        long? draftBytes)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(gpu);
        ArgumentNullException.ThrowIfNull(options);

        List<(int Priority, string Note)> notes = [];

        // 1. The budget. The margin keeps room for the driver and the desktop, and the
        //    safety factor leaves another 8 percent for the Vulkan runtime.
        int usableMiB = (int)(gpu.BudgetMiB(options.MarginMiB) * SafetyFactor);

        // 2. The facts the header did not carry are replaced by conservative guesses, so
        //    that a missing key makes the estimate larger and never smaller.
        ulong blockCount = facts.BlockCount > 0 ? facts.BlockCount : FallbackBlockCount;
        if (facts.BlockCount == 0)
        {
            AddNote(notes, 0, "the model does not report its layer count, so the KV cache is estimated for 32 layers");
        }

        ulong vocabularyCount = facts.VocabularyCount > 0 ? facts.VocabularyCount : FallbackVocabularyCount;
        if (facts.VocabularyCount == 0)
        {
            AddNote(notes, 0, "the model does not report its vocabulary size, so the compute buffer is estimated for 262144 tokens");
        }

        ulong headCountKv = facts.HeadCountKv > 0 ? facts.HeadCountKv : Math.Max(1UL, facts.HeadCount);
        ulong headDim = facts.HeadDim > 0
            ? facts.HeadDim
            : facts.EmbeddingLength > 0 ? facts.EmbeddingLength : FallbackHeadDim;

        if (facts.HeadCountKv == 0 || facts.HeadDim == 0)
        {
            AddNote(notes, 0, "the model does not report its attention shape, so the KV cache is estimated conservatively");
        }

        // 3. The size class decides the defaults, the options override them.
        ModelSizeClass sizeClass = facts.SizeClass;
        int candidateContext = options.ContextSize is > 0 ? options.ContextSize.Value : PresetContext(sizeClass);
        string presetCacheType = PresetCacheType(sizeClass);
        int presetMicroBatch = PresetMicroBatch(sizeClass);
        int presetBatch = PresetBatch(sizeClass);

        // 4. The context never exceeds what the model was trained for or what the user allows.
        if (facts.NativeContextLength > 0)
        {
            candidateContext = (int)Math.Min((ulong)candidateContext, facts.NativeContextLength);
        }

        if (options.MaxContextSize is > 0)
        {
            candidateContext = Math.Min(candidateContext, options.MaxContextSize.Value);
        }

        candidateContext = Math.Max(candidateContext, MinimumContextSize);
        candidateContext = candidateContext / ContextStep * ContextStep;

        // 5. The remaining launch parameters.
        string cacheTypeK = NormalizeCacheType(options.CacheTypeK ?? presetCacheType);
        string cacheTypeV = NormalizeCacheType(options.CacheTypeV ?? presetCacheType);
        double cacheBytesPerElement = Math.Max(CacheBytesPerElement(cacheTypeK), CacheBytesPerElement(cacheTypeV));
        int microBatch = options.MicroBatchSize is > 0 ? options.MicroBatchSize.Value : presetMicroBatch;
        int batch = options.BatchSize is > 0 ? options.BatchSize.Value : presetBatch;
        int threads = options.Threads is > 0 ? options.Threads.Value : Math.Max(2, Environment.ProcessorCount / 2);

        // 6. What the VRAM is spent on.
        double weightsMiB = ComputeWeightsMiB(facts);
        double computeMiB = ComputeComputeMiB(vocabularyCount, microBatch);
        double projectorSizeMiB = projectorBytes is > 0 ? projectorBytes.Value / MiB : 0;
        double draftSizeMiB = draftBytes is > 0 ? draftBytes.Value / MiB : 0;
        double projectorMiB = projectorSizeMiB;
        double draftMiB = draftSizeMiB;
        bool useProjector = projectorMiB > 0;
        bool useDraftModel = draftMiB > 0;

        // 7. The context that still fits. The KV cache of the sliding window layers is only
        //    counted when the model announces its window, which is the same rule llama.cpp
        //    uses to lay out the cache.
        double fixedMiB = weightsMiB + computeMiB + projectorMiB + draftMiB;
        int context = FitContext(candidateContext, usableMiB, fixedMiB, size => KvMiB(facts, blockCount, headCountKv, headDim, cacheBytesPerElement, size, conservative: false));

        int gpuLayers;
        if (fixedMiB + KvMiB(facts, blockCount, headCountKv, headDim, cacheBytesPerElement, context, conservative: false) > usableMiB)
        {
            // 8. Even the smallest context does not fit, so the model has to be split. The KV
            //    cache is charged for every block here, because llama.cpp keeps the cache of
            //    all blocks on the GPU even when only some layers are offloaded.
            double kvAtMinimumMiB = KvMiB(facts, blockCount, headCountKv, headDim, cacheBytesPerElement, MinimumContextSize, conservative: true);
            gpuLayers = ComputeGpuLayers(usableMiB, weightsMiB, computeMiB, projectorMiB, draftMiB, kvAtMinimumMiB, blockCount);

            if (gpuLayers < 0 && projectorMiB > 0 && options.AllowProjectorSkip)
            {
                projectorMiB = 0;
                useProjector = false;
                gpuLayers = ComputeGpuLayers(usableMiB, weightsMiB, computeMiB, projectorMiB, draftMiB, kvAtMinimumMiB, blockCount);
            }

            if (gpuLayers < 0 && draftMiB > 0 && options.AllowDraftModelSkip)
            {
                draftMiB = 0;
                useDraftModel = false;
                gpuLayers = ComputeGpuLayers(usableMiB, weightsMiB, computeMiB, projectorMiB, draftMiB, kvAtMinimumMiB, blockCount);
            }

            gpuLayers = Math.Clamp(gpuLayers, 0, (int)Math.Max(0, blockCount - 1));
            context = MinimumContextSize;
        }
        else if (options.GpuLayers is { } requestedLayers)
        {
            // 9. The user fixed the number of layers, so it wins. The context shrinks until
            //    the requested offload fits.
            gpuLayers = requestedLayers;
            long layersOnGpu = requestedLayers < 0 ? (long)blockCount : Math.Min(requestedLayers, (int)blockCount);
            double layerWeightsMiB = weightsMiB * layersOnGpu / Math.Max(1, (double)blockCount);
            double requestedFixedMiB = layerWeightsMiB + computeMiB + projectorMiB + draftMiB;

            context = FitContext(candidateContext, usableMiB, requestedFixedMiB, size => KvMiB(facts, blockCount, headCountKv, headDim, cacheBytesPerElement, size, conservative: false));

            if (requestedFixedMiB + KvMiB(facts, blockCount, headCountKv, headDim, cacheBytesPerElement, context, conservative: false) > usableMiB)
            {
                AddNote(notes, 0, $"the requested {requestedLayers} GPU layers do not fit in {usableMiB} MiB, the server may fail to start");
            }
        }
        else
        {
            // 10. Everything fits, so everything is offloaded.
            gpuLayers = -1;
        }

        // 11. The reasons behind the plan.
        AddNote(notes, 0, DescribeContext(context, facts, usableMiB));

        if (gpuLayers < 0)
        {
            AddNote(notes, 0, $"all {blockCount} layers are offloaded to the GPU");
        }
        else
        {
            AddNote(notes, 0, $"offloading {gpuLayers} of {blockCount} layers to the GPU; the rest runs on the CPU");
        }

        if (projectorSizeMiB > 0 && !useProjector)
        {
            AddNote(notes, 0, $"the multimodal projector needs {projectorSizeMiB:0} MiB and does not fit, so it is skipped");
        }
        else if (projectorSizeMiB > 0 && gpuLayers >= 0)
        {
            AddNote(notes, 1, $"the multimodal projector needs {projectorSizeMiB:0} MiB, so fewer layers are offloaded to the GPU");
        }

        if (draftSizeMiB > 0 && !useDraftModel)
        {
            AddNote(notes, 0, $"the draft model needs {draftSizeMiB:0} MiB and does not fit, so it is skipped");
        }
        else if (draftSizeMiB > 0 && gpuLayers >= 0)
        {
            AddNote(notes, 1, $"the draft model needs {draftSizeMiB:0} MiB, so fewer layers are offloaded to the GPU");
        }

        AddNote(notes, 1, DescribeModel(facts, gpu));
        AddNote(notes, 1, $"the weights need {weightsMiB:0} MiB of the {usableMiB} MiB budget");

        if (cacheTypeK is not ("f16" or "f32" or "bf16"))
        {
            AddNote(notes, 2, $"KV cache {cacheTypeK} needs less memory than f16 for the same context");
        }

        AddNote(notes, 2, "flash attention is on, which keeps the KV cache small");

        List<string> finalNotes = [.. notes
            .OrderBy(entry => entry.Priority)
            .Select(entry => entry.Note)
            .Take(MaxNotes)];

        return new AutoTuneResult(
            context,
            gpuLayers,
            cacheTypeK,
            cacheTypeV,
            batch,
            microBatch,
            threads,
            useProjector,
            useDraftModel,
            finalNotes.Count > 0 ? finalNotes : Array.Empty<string>());
    }

    /// <summary>
    /// Estimates the VRAM the weights of the model need.
    /// </summary>
    /// <param name="facts">What is known about the model.</param>
    /// <returns>The weight size in MiB.</returns>
    private static double ComputeWeightsMiB(ModelFacts facts)
    {
        double weightsMiB = facts.TensorDataBytes / MiB;

        if (facts.HasSeparateOutputProjection && facts.VocabularyCount > 0 && facts.EmbeddingLength > 0)
        {
            // llama.cpp shares one embedding matrix between the token embedding and the
            // output projection, so one copy of it is not part of the running model.
            double bytesPerElement = WeightBytesPerElement(facts.QuantName);
            double duplicateMiB = facts.VocabularyCount * (double)facts.EmbeddingLength * bytesPerElement / MiB;
            weightsMiB = Math.Max(0, weightsMiB - duplicateMiB);
        }

        return weightsMiB;
    }

    /// <summary>
    /// Estimates the VRAM the compute buffers need.
    /// </summary>
    /// <param name="vocabularyCount">The number of entries in the vocabulary.</param>
    /// <param name="microBatch">The micro batch size.</param>
    /// <returns>The compute buffer size in MiB.</returns>
    private static double ComputeComputeMiB(ulong vocabularyCount, int microBatch)
    {
        // The logits tensor holds one f32 row per vocabulary entry and per token of the
        // micro batch: 248320 * 512 * 4 bytes is 485 MiB.
        double logitsMiB = vocabularyCount * (double)microBatch * LogitsBytesPerElement / MiB;
        return logitsMiB + ComputeBufferMiB;
    }

    /// <summary>
    /// Estimates the size of the KV cache for a given context length.
    /// </summary>
    /// <param name="facts">What is known about the model.</param>
    /// <param name="blockCount">The number of transformer blocks of the model.</param>
    /// <param name="headCountKv">The number of key and value heads.</param>
    /// <param name="headDim">The width of one key or value head.</param>
    /// <param name="bytesPerElement">The size of one element of the cache.</param>
    /// <param name="contextSize">The context length in tokens.</param>
    /// <param name="conservative">
    /// True charges the sliding window layers for the whole context, which is what happens
    /// when the model does not announce its window.
    /// </param>
    /// <returns>The size of the cache in MiB.</returns>
    private static double KvMiB(
        ModelFacts facts,
        ulong blockCount,
        ulong headCountKv,
        ulong headDim,
        double bytesPerElement,
        int contextSize,
        bool conservative)
    {
        ulong attentionLayers = AttentionLayerCount(facts, blockCount);
        ulong slidingLayers = blockCount > attentionLayers ? blockCount - attentionLayers : 0;

        // Every layer keeps a key and a value vector per token and per head.
        double bytesPerTokenPerLayer = headCountKv * (double)headDim * bytesPerElement * 2d;

        double bytes = (double)attentionLayers * contextSize * bytesPerTokenPerLayer;

        if (slidingLayers > 0)
        {
            long window = facts.SlidingWindow > 0
                ? Math.Min((long)facts.SlidingWindow, contextSize)
                : conservative ? contextSize : 0;

            bytes += (double)slidingLayers * window * bytesPerTokenPerLayer;
        }

        return bytes / MiB;
    }

    /// <summary>
    /// Counts the layers that hold the full context.
    /// </summary>
    /// <param name="facts">What is known about the model.</param>
    /// <param name="blockCount">The number of transformer blocks of the model.</param>
    /// <returns>The number of full attention layers.</returns>
    private static ulong AttentionLayerCount(ModelFacts facts, ulong blockCount)
    {
        if (facts.FullAttentionInterval > 1)
        {
            // A hybrid model keeps a full attention layer every few blocks.
            return blockCount / facts.FullAttentionInterval;
        }

        if (facts.SlidingWindow > 0)
        {
            return GlobalAttentionLayers(facts.Architecture, blockCount);
        }

        return blockCount;
    }

    /// <summary>
    /// Counts the global attention layers of a sliding window model.
    /// </summary>
    /// <param name="architecture">The architecture of the model.</param>
    /// <param name="blockCount">The number of transformer blocks of the model.</param>
    /// <returns>The number of layers that hold the full context.</returns>
    private static ulong GlobalAttentionLayers(string architecture, ulong blockCount)
    {
        string name = architecture.ToLowerInvariant();

        // llama.cpp keeps one global layer every six blocks in these models.
        if (name is "gemma3" or "gemma2")
        {
            return blockCount / 6;
        }

        if (name == "gpt-oss")
        {
            return blockCount / 4;
        }

        // Every other model keeps the full context in every layer.
        return blockCount;
    }

    /// <summary>
    /// Walks the context down until the model fits into the budget.
    /// </summary>
    /// <param name="candidateContext">The context to start from.</param>
    /// <param name="usableMiB">The VRAM the plan may use.</param>
    /// <param name="fixedMiB">The VRAM the weights, the buffers and the side models need.</param>
    /// <param name="kvMiB">The size of the KV cache for a given context length.</param>
    /// <returns>The largest context that fits, never below the minimum.</returns>
    private static int FitContext(int candidateContext, double usableMiB, double fixedMiB, Func<int, double> kvMiB)
    {
        for (int context = candidateContext; context >= MinimumContextSize; context -= ContextStep)
        {
            if (fixedMiB + kvMiB(context) <= usableMiB)
            {
                return context;
            }
        }

        return MinimumContextSize;
    }

    /// <summary>
    /// Counts the layers that fit into the budget when the model has to be split.
    /// </summary>
    /// <param name="usableMiB">The VRAM the plan may use.</param>
    /// <param name="weightsMiB">The size of the whole model.</param>
    /// <param name="computeMiB">The size of the compute buffers.</param>
    /// <param name="projectorMiB">The size of the projector.</param>
    /// <param name="draftMiB">The size of the draft model.</param>
    /// <param name="kvMiB">The size of the KV cache.</param>
    /// <param name="blockCount">The number of transformer blocks of the model.</param>
    /// <returns>
    /// The number of layers that fit, which is negative when not even a model without any
    /// layer fits.
    /// </returns>
    private static int ComputeGpuLayers(
        double usableMiB,
        double weightsMiB,
        double computeMiB,
        double projectorMiB,
        double draftMiB,
        double kvMiB,
        ulong blockCount)
    {
        // The weights are spread evenly over the blocks, which is close enough to how
        // llama.cpp splits a model.
        double perLayerMiB = weightsMiB / Math.Max(1d, (double)blockCount);
        if (perLayerMiB <= 0)
        {
            return 0;
        }

        double availableMiB = usableMiB - computeMiB - projectorMiB - draftMiB - kvMiB;
        return (int)(availableMiB / perLayerMiB);
    }

    /// <summary>Gets the context the size class asks for.</summary>
    /// <param name="sizeClass">The size class of the model.</param>
    /// <returns>The context in tokens.</returns>
    private static int PresetContext(ModelSizeClass sizeClass) => sizeClass switch
    {
        ModelSizeClass.Tiny => 32_768,
        ModelSizeClass.Small => 32_768,
        ModelSizeClass.Medium => 16_384,
        ModelSizeClass.Large => 8_192,
        ModelSizeClass.XLarge => 4_096,
        _ => 2_048,
    };

    /// <summary>Gets the KV cache quantization the size class asks for.</summary>
    /// <param name="sizeClass">The size class of the model.</param>
    /// <returns>The name of the cache type.</returns>
    private static string PresetCacheType(ModelSizeClass sizeClass) => sizeClass switch
    {
        ModelSizeClass.Tiny or ModelSizeClass.Small or ModelSizeClass.Medium or ModelSizeClass.Large => "q8_0",
        _ => "q4_0",
    };

    /// <summary>Gets the micro batch size the size class asks for.</summary>
    /// <param name="sizeClass">The size class of the model.</param>
    /// <returns>The micro batch size.</returns>
    private static int PresetMicroBatch(ModelSizeClass sizeClass) => sizeClass switch
    {
        ModelSizeClass.Tiny or ModelSizeClass.Small or ModelSizeClass.Medium => 512,
        _ => 256,
    };

    /// <summary>Gets the batch size the size class asks for.</summary>
    /// <param name="sizeClass">The size class of the model.</param>
    /// <returns>The batch size.</returns>
    private static int PresetBatch(ModelSizeClass sizeClass) => sizeClass switch
    {
        ModelSizeClass.Tiny or ModelSizeClass.Small or ModelSizeClass.Medium or ModelSizeClass.Large => 2_048,
        _ => 1_024,
    };

    /// <summary>
    /// Gets the conservative number of bytes one weight of the model occupies.
    /// </summary>
    /// <param name="quantName">The quantization of the model, for example <c>Q4_K_M</c>.</param>
    /// <returns>The bytes per weight.</returns>
    private static double WeightBytesPerElement(string? quantName) => Normalize(quantName) switch
    {
        "F32" => 4d,
        "F16" => 2d,
        "BF16" => 2d,
        "Q4_0" => 0.5625d,
        "Q4_1" => 0.625d,
        "Q5_0" => 0.6875d,
        "Q5_1" => 0.75d,
        "Q8_0" => 1.0625d,
        "Q2_K" or "Q2_K_S" => 0.328d,
        "Q3_K" or "Q3_K_S" or "Q3_K_M" or "Q3_K_L" => 0.430d,
        "Q4_K" or "Q4_K_S" or "Q4_K_M" => 0.5625d,
        "Q5_K" or "Q5_K_S" or "Q5_K_M" => 0.688d,
        "Q6_K" => 0.820d,
        "Q8_K" => 1.141d,
        "IQ2_XXS" => 0.258d,
        "IQ2_XS" => 0.289d,
        "IQ2_S" or "IQ2_M" => 0.320d,
        "IQ3_XXS" or "IQ3_XS" => 0.383d,
        "IQ3_S" or "IQ3_M" => 0.430d,
        "IQ1_S" => 0.195d,
        "IQ1_M" => 0.219d,
        "IQ4_NL" => 0.5625d,
        "IQ4_XS" => 0.531d,
        _ => UnknownQuantBytesPerElement,
    };

    /// <summary>
    /// Gets the number of bytes one element of the KV cache occupies.
    /// </summary>
    /// <param name="cacheType">The cache type, for example <c>q8_0</c>.</param>
    /// <returns>The bytes per element.</returns>
    private static double CacheBytesPerElement(string cacheType) => cacheType switch
    {
        "f32" => 4d,
        "f16" => 2d,
        "bf16" => 2d,
        "q8_0" => 1.0625d,
        "q4_0" => 0.5625d,
        "q4_1" => 0.625d,
        "q5_0" => 0.6875d,
        "q5_1" => 0.75d,
        "iq4_nl" => 0.5625d,
        "iq4_xs" => 0.531d,
        _ => UnknownCacheBytesPerElement,
    };

    /// <summary>Brings a cache type into the lower case spelling llama.cpp expects.</summary>
    /// <param name="cacheType">The cache type to normalize.</param>
    /// <returns>The normalized cache type.</returns>
    private static string NormalizeCacheType(string cacheType) => cacheType.Trim().ToLowerInvariant();

    /// <summary>Brings a quantization name into the upper case spelling used in the tables.</summary>
    /// <param name="quantName">The quantization name to normalize.</param>
    /// <returns>The normalized name, or an empty string when there is none.</returns>
    private static string Normalize(string? quantName) => quantName is null ? string.Empty : quantName.Trim().ToUpperInvariant();

    /// <summary>Describes the model and the GPU it runs on.</summary>
    /// <param name="facts">What is known about the model.</param>
    /// <param name="gpu">The GPU the model runs on.</param>
    /// <returns>A short sentence.</returns>
    private static string DescribeModel(ModelFacts facts, GpuProfile gpu)
    {
        double billions = facts.ParameterCountMillions / 1000d;
        string label = facts.SizeLabel ?? (facts.ParameterCountMillions > 0
            ? $"{billions.ToString("0.#", CultureInfo.InvariantCulture)}B"
            : "unknown size");

        if (facts.ParameterCountMillions > 0)
        {
            return $"{label} model ({billions.ToString("0.0", CultureInfo.InvariantCulture)}B parameters) on {gpu.Name} with {gpu.FreeMiB} MiB free";
        }

        return $"{label} model on {gpu.Name} with {gpu.FreeMiB} MiB free";
    }

    /// <summary>Describes the chosen context window.</summary>
    /// <param name="context">The chosen context in tokens.</param>
    /// <param name="facts">What is known about the model.</param>
    /// <param name="usableMiB">The VRAM the plan may use.</param>
    /// <returns>A short sentence.</returns>
    private static string DescribeContext(int context, ModelFacts facts, int usableMiB)
    {
        if (facts.NativeContextLength > 0 && (ulong)context < facts.NativeContextLength)
        {
            return $"context {context} tokens (the model allows {facts.NativeContextLength}, capped by the {usableMiB} MiB VRAM budget)";
        }

        if (facts.NativeContextLength > 0)
        {
            return $"context {context} tokens (the model allows {facts.NativeContextLength})";
        }

        return $"context {context} tokens (the model does not say, so the {usableMiB} MiB VRAM budget decides)";
    }

    /// <summary>
    /// Adds a note, keeping the text short enough to be shown in a console.
    /// </summary>
    /// <param name="notes">The notes collected so far.</param>
    /// <param name="priority">Lower numbers survive when there are too many notes.</param>
    /// <param name="note">The sentence to add.</param>
    private static void AddNote(List<(int Priority, string Note)> notes, int priority, string note)
    {
        notes.Add((priority, note.Length <= MaxNoteLength ? note : note[..(MaxNoteLength - 3)] + "..."));
    }
}
