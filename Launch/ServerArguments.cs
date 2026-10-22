namespace GgufLauncher.Launch;

/// <summary>
/// Builds the llama.cpp command line from the resolved model and the merged
/// settings. The goal is that a plain double-click produces a working server
/// without the user having to remember any flags.
/// </summary>
public static class ServerArguments
{
    /// <summary>Builds the launch plan for a resolved model.</summary>
    public static LaunchPlan Build(ResolvedModel model, LaunchSettings settings, LlamaServer server)
    {
        List<string> args = [];
        List<string> notes = [];

        // The remarks of the tuner come first, because they explain the numbers that
        // the rest of the command line is made of.
        notes.AddRange(settings.TuningNotes);

        if (server.UseServeSubcommand)
        {
            args.Add("serve");
        }

        args.Add("-m");
        args.Add(model.Path);

        AddMmproj(args, notes, model, settings);
        AddDraft(args, notes, model, settings);
        AddHardware(args, notes, model, settings);
        AddContext(args, notes, model, settings);
        AddNetwork(args, settings);

        args.AddRange(settings.ExtraArgs);

        return new LaunchPlan
        {
            ExecutablePath = server.ExecutablePath,
            Arguments = args,
            Host = settings.Host,
            Port = settings.Port,
            Url = BuildUrl(settings.Host, settings.Port),
            WorkingDirectory = Path.GetDirectoryName(model.Path) ?? Environment.CurrentDirectory,
            Notes = notes,
        };
    }

    private static void AddMmproj(List<string> args, List<string> notes, ResolvedModel model, LaunchSettings settings)
    {
        string? mmproj = settings.Mmproj ?? model.Mmproj;
        if (mmproj is null)
        {
            if (model.Gguf.IsMultimodal && !model.IsProjector)
            {
                notes.Add("multimodal model detected but no mmproj-*.gguf was found next to it");
            }

            return;
        }

        if (!File.Exists(mmproj))
        {
            notes.Add($"mmproj not found, skipping: {mmproj}");
            return;
        }

        // The tuner leaves the projector out when it does not fit into the free video
        // memory, because the server would die with an out of memory error instead.
        if (settings.Tuning is { UseProjector: false })
        {
            notes.Add($"auto-tune: --mmproj was left out for {Path.GetFileName(mmproj)}, it does not fit in the free video memory");
            return;
        }

        args.Add("--mmproj");
        args.Add(mmproj);
    }

    private static void AddDraft(List<string> args, List<string> notes, ResolvedModel model, LaunchSettings settings)
    {
        string? draft = settings.SpecDraftModel ?? model.DraftModel;
        string? specType = settings.SpecType ?? model.DraftSpecType;

        if (draft is null || specType is null)
        {
            return;
        }

        if (!File.Exists(draft))
        {
            notes.Add($"draft model not found, skipping: {draft}");
            return;
        }

        // The draft model is a luxury, so it is the first thing that goes when the
        // video memory is tight.
        if (settings.Tuning is { UseDraftModel: false })
        {
            notes.Add($"auto-tune: the draft model {Path.GetFileName(draft)} was left out, it does not fit in the free video memory");
            return;
        }

        args.Add("--spec-draft-model");
        args.Add(draft);
        args.Add("--spec-type");
        args.Add(specType);
        args.Add("--spec-draft-n-max");
        args.Add(settings.SpecDraftNMax.ToString());

        AddDraftSampling(args, notes, settings);
    }

    /// <summary>
    /// Keeps the draft sampling step on the CPU unless it was switched on. The
    /// Vulkan backend aborts the whole server on the first request when it samples
    /// the draft on the GPU, and that op only touches a handful of logits, so the
    /// CPU is both the safe and the cheap place for it.
    /// </summary>
    private static void AddDraftSampling(List<string> args, List<string> notes, LaunchSettings settings)
    {
        if (settings.DraftBackendSampling)
        {
            return;
        }

        args.Add("--no-spec-draft-backend-sampling");
        notes.Add("draft sampling runs on the CPU (the GPU backend aborts the server on the Vulkan driver)");
    }

    /// <summary>
    /// Adds the flags that describe how the model runs on this machine. They are
    /// split into small helpers so each group stays readable on its own.
    /// </summary>
    private static void AddHardware(List<string> args, List<string> notes, ResolvedModel model, LaunchSettings settings)
    {
        AddOffload(args, model, settings);
        AddAttention(args, settings);
        AddParallelAndExperts(args, notes, model, settings);
        AddCacheTypes(args, notes, settings);
        AddBatchSizes(args, settings);
        AddInterface(args, model, settings);
    }

    /// <summary>
    /// Adds the GPU offload and the thread count. The number of layers is written out
    /// whenever it is known, because a missing flag lets llama.cpp offload everything
    /// it can and die with an out of memory error when the model is too big.
    /// </summary>
    private static void AddOffload(List<string> args, ResolvedModel model, LaunchSettings settings)
    {
        if (settings.GpuLayers is { } layers)
        {
            args.Add("-ngl");
            args.Add(ResolveGpuLayers(model, settings, layers).ToString());
        }

        if (settings.Threads > 0)
        {
            args.Add("-t");
            args.Add(settings.Threads.ToString());
        }
    }

    /// <summary>
    /// Turns the layer count of the plan into the number llama.cpp expects. The planner
    /// says -1 for "every layer", and llama.cpp wants a concrete number, because its own
    /// fitting pass gives up as soon as this flag was set by hand.
    /// </summary>
    /// <param name="model">The model that is about to be served.</param>
    /// <param name="settings">The settings of the launch.</param>
    /// <param name="layers">The layer count of the plan.</param>
    /// <returns>The number of layers to hand to llama.cpp.</returns>
    private static int ResolveGpuLayers(ResolvedModel model, LaunchSettings settings, int layers)
    {
        if (layers != -1 || settings.Tuning is null)
        {
            return layers;
        }

        return BlockCount(model) ?? -2;
    }

    /// <summary>Reads the number of transformer blocks of the model from its metadata.</summary>
    /// <param name="model">The model that is about to be served.</param>
    /// <returns>The number of blocks, or null when the file does not say.</returns>
    private static int? BlockCount(ResolvedModel model)
    {
        if (model.Gguf.Architecture is not { } architecture)
        {
            return null;
        }

        if (model.Gguf.GetUInt64(architecture + ".block_count") is not { } blocks || blocks == 0 || blocks > int.MaxValue)
        {
            return null;
        }

        return (int)blocks;
    }

    /// <summary>
    /// Adds the attention switch and the chat template switch. The template has to
    /// be stated explicitly because llama.cpp cannot always infer it from the model.
    /// </summary>
    private static void AddAttention(List<string> args, LaunchSettings settings)
    {
        args.Add("-fa");
        args.Add(settings.FlashAttention ? "on" : "off");
        args.Add(settings.Jinja ? "--jinja" : "--no-jinja");
    }

    /// <summary>
    /// Adds the parallel slot count and the mixture-of-experts switch. When the
    /// experts do not fit in VRAM the launcher points the user at the cpuMoe option.
    /// </summary>
    private static void AddParallelAndExperts(List<string> args, List<string> notes, ResolvedModel model, LaunchSettings settings)
    {
        if (settings.ParallelSlots > 0)
        {
            args.Add("-np");
            args.Add(settings.ParallelSlots.ToString());
        }

        if (settings.CpuMoe)
        {
            args.Add("--cpu-moe");
        }
        else if (model.Gguf.ExpertCount is { } total && model.Gguf.ExpertUsedCount is { } active && total > active)
        {
            notes.Add($"MoE model ({active}/{total} experts active) - set \"cpuMoe\": true if it does not fit in VRAM");
        }
    }

    /// <summary>
    /// Adds the KV cache quantisation types. They are optional because the llama.cpp
    /// defaults are the safe choice for most models.
    /// </summary>
    private static void AddCacheTypes(List<string> args, List<string> notes, LaunchSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CacheTypeK))
        {
            args.Add("-ctk");
            args.Add(settings.CacheTypeK);
        }

        if (!string.IsNullOrWhiteSpace(settings.CacheTypeV))
        {
            args.Add("-ctv");
            args.Add(settings.CacheTypeV);
        }

        if (IsQuantised(settings.CacheTypeV) && !settings.FlashAttention)
        {
            notes.Add("a quantised value cache needs flash attention, which is switched off");
        }
    }

    /// <summary>Checks whether a cache type stores less than the full precision.</summary>
    /// <param name="cacheType">The cache type of the value cache.</param>
    /// <returns><see langword="true"/> when the cache is quantised.</returns>
    private static bool IsQuantised(string? cacheType)
    {
        if (string.IsNullOrWhiteSpace(cacheType))
        {
            return false;
        }

        return !string.Equals(cacheType, "f16", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(cacheType, "f32", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Adds the batch sizes. The physical one sizes the compute buffer, so it is the
    /// one that decides how much video memory the server needs.
    /// </summary>
    private static void AddBatchSizes(List<string> args, LaunchSettings settings)
    {
        if (settings.BatchSize > 0)
        {
            args.Add("-b");
            args.Add(settings.BatchSize.ToString());
        }

        if (settings.MicroBatchSize > 0)
        {
            args.Add("-ub");
            args.Add(settings.MicroBatchSize.ToString());
        }
    }

    /// <summary>
    /// Adds the web UI switch and the model alias. The alias comes from the file
    /// name so the user recognises the model in the server list.
    /// </summary>
    private static void AddInterface(List<string> args, ResolvedModel model, LaunchSettings settings)
    {
        if (settings.NoWebUi)
        {
            args.Add("--no-webui");
        }

        string alias = Path.GetFileNameWithoutExtension(model.Path);
        if (!string.IsNullOrWhiteSpace(alias))
        {
            args.Add("--alias");
            args.Add(alias);
        }
    }

    private static void AddContext(List<string> args, List<string> notes, ResolvedModel model, LaunchSettings settings)
    {
        if (settings.ContextSize is > 0)
        {
            args.Add("-c");
            args.Add(settings.ContextSize.Value.ToString());

            // A context the user asked for is used as it is, but the tuner knows how
            // much of it actually fits into the free video memory.
            if (settings.Tuning is { } plan && settings.ContextSize.Value > plan.ContextSize)
            {
                notes.Add($"auto-tune: the requested context {settings.ContextSize.Value} is larger than the {plan.ContextSize} that fits in the free video memory, the server may run out of memory");
            }

            return;
        }

        if (model.Gguf.ContextLength is not { } native)
        {
            notes.Add("the model does not advertise a context length, so llama.cpp picks one");
            return;
        }

        // The context a model was trained with is often much larger than the GPU can hold
        // once a projector and a draft model share the memory, so the launcher starts from
        // the smaller of the two instead of failing with an out of memory error.
        int ceiling = settings.MaxContextSize > 0 ? settings.MaxContextSize : (int)native;
        int context = (int)Math.Min(native, (ulong)ceiling);

        args.Add("-c");
        args.Add(context.ToString());

        if ((ulong)context < native)
        {
            notes.Add($"context {context} (the model allows {native}, capped at {ceiling} - use --ctx to change it)");
        }
        else
        {
            notes.Add($"context {context} (from the model)");
        }
    }

    private static void AddNetwork(List<string> args, LaunchSettings settings)
    {
        args.Add("--host");
        args.Add(settings.Host);
        args.Add("--port");
        args.Add(settings.Port.ToString());
    }

    /// <summary>Builds a browsable URL, mapping wildcard bind addresses back to loopback.</summary>
    public static string BuildUrl(string host, int port)
    {
        string browsable = host switch
        {
            "0.0.0.0" or "::" or "" => "127.0.0.1",
            _ => host,
        };

        return $"http://{browsable}:{port}/";
    }
}
