using System.Globalization;
using GgufLauncher.AutoTune;
using GgufLauncher.Cli;
using GgufLauncher.Configuration;

namespace GgufLauncher.Launch;

/// <summary>
/// Bridges the settings of the launcher and the VRAM estimator. It merges the three
/// sources of the auto-tune settings, asks the estimator for a plan and writes the
/// result back into the settings, without ever overwriting a number the user asked
/// for by hand.
/// </summary>
public static class AutoTuner
{
    /// <summary>The prefix every remark of the tuner carries.</summary>
    private const string NotePrefix = "auto-tune: ";

    /// <summary>The number of bytes in one MiB.</summary>
    private const double BytesPerMiB = 1024d * 1024d;

    /// <summary>
    /// Merges the auto-tune settings of the command line, the per-model override and
    /// the global configuration. The command line wins, then the per-model override,
    /// then the global configuration.
    /// </summary>
    /// <param name="config">The global configuration.</param>
    /// <param name="overrides">The overrides of the model that is about to be served.</param>
    /// <param name="cli">The options of the command line.</param>
    /// <returns>The merged settings of the tuner.</returns>
    public static AutoTuneOptions BuildOptions(LauncherConfig config, ModelOverride? overrides, CliOptions cli)
    {
        LauncherConfig.AutoTuneConfig global = config.AutoTune;
        LauncherConfig.AutoTuneConfig? local = overrides?.AutoTune;

        // A thread count of zero in the global configuration means "let llama.cpp decide",
        // so the auto-tune section gets a chance to name one instead.
        int? globalThreads = config.Threads > 0 ? config.Threads : null;

        return new AutoTuneOptions
        {
            Enabled = cli.AutoTune ?? local?.EnabledRaw ?? global.Enabled,
            MarginMiB = cli.VramMarginMiB ?? local?.MarginMiBRaw ?? global.MarginMiB,
            ContextSize = cli.ContextSize
                ?? overrides?.ContextSize
                ?? local?.ContextSize
                ?? config.ContextSize
                ?? global.ContextSize,
            GpuLayers = cli.GpuLayers
                ?? overrides?.GpuLayers
                ?? local?.GpuLayers
                ?? config.GpuLayers
                ?? global.GpuLayers,
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
            BatchSize = cli.BatchSize ?? local?.BatchSize ?? global.BatchSize,
            MicroBatchSize = cli.UbatchSize ?? local?.MicroBatchSize ?? global.MicroBatchSize,
            Threads = cli.Threads
                ?? overrides?.Threads
                ?? local?.Threads
                ?? globalThreads
                ?? global.Threads,
            MaxContextSize = Math.Min(config.MaxContextSize, local?.MaxContextSizeRaw ?? global.MaxContextSize),
            AllowProjectorSkip = local?.AllowProjectorSkipRaw ?? global.AllowProjectorSkip,
            AllowDraftModelSkip = local?.AllowDraftModelSkipRaw ?? global.AllowDraftModelSkip,
        };
    }

    /// <summary>
    /// Fits the model into the free video memory and returns the settings with the
    /// tuned numbers filled in. Numbers the user asked for are never touched.
    /// </summary>
    /// <param name="settings">The merged settings of the launch.</param>
    /// <param name="model">The model that is about to be served.</param>
    /// <param name="options">The merged settings of the tuner.</param>
    /// <returns>The settings of the launch, tuned where the user left a free hand.</returns>
    public static LaunchSettings Apply(LaunchSettings settings, ResolvedModel model, AutoTuneOptions options)
    {
        if (!options.Enabled)
        {
            return settings;
        }

        GpuProfile? gpu = GpuProfile.Detect();
        if (gpu is null)
        {
            return settings with
            {
                TuningNotes = [Note("no GPU was detected, so the launcher keeps its previous settings")],
            };
        }

        ModelFacts facts = ModelFacts.From(model.Gguf, model.Path);
        string? projector = settings.Mmproj ?? model.Mmproj;
        string? draft = settings.SpecDraftModel ?? model.DraftModel;

        AutoTuneResult plan;
        try
        {
            plan = VramEstimator.Plan(facts, gpu, options, FileSize(projector), FileSize(draft));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return settings with
            {
                TuningNotes = [Note($"the VRAM estimate failed ({ex.Message}), so the launcher keeps its previous settings")],
            };
        }

        List<string> notes = [];
        notes.Add(Note($"{gpu.Name} with {gpu.FreeMiB} MiB free of {gpu.TotalMiB} MiB, {options.MarginMiB} MiB kept in reserve"));
        notes.Add(Note($"{DescribeSize(facts)} model ({facts.SizeClass}), {MiB(facts.TensorDataBytes)} MiB of weights"));

        foreach (string note in plan.Notes)
        {
            notes.Add(Note(note));
        }

        notes.Add(Note($"context {plan.ContextSize} tokens, KV cache {plan.CacheTypeK}/{plan.CacheTypeV}"));
        notes.Add(Note(DescribeLayers(plan, facts)));
        notes.Add(Note($"batch {plan.BatchSize}, micro batch {plan.MicroBatchSize}, {plan.Threads} CPU threads"));

        if (!plan.UseProjector && projector is not null)
        {
            notes.Add(Note($"skipping the projector {Path.GetFileName(projector)} ({MiB(FileSize(projector) ?? 0)} MiB), it does not fit in the free video memory"));
        }

        if (!plan.UseDraftModel && draft is not null)
        {
            notes.Add(Note($"skipping the draft model {Path.GetFileName(draft)} ({MiB(FileSize(draft) ?? 0)} MiB), it does not fit in the free video memory"));
        }

        return settings with
        {
            ContextSize = options.ContextSize is null ? plan.ContextSize : settings.ContextSize,
            GpuLayers = options.GpuLayers is null ? plan.GpuLayers : settings.GpuLayers,
            CacheTypeK = string.IsNullOrWhiteSpace(options.CacheTypeK) ? plan.CacheTypeK : settings.CacheTypeK,
            CacheTypeV = string.IsNullOrWhiteSpace(options.CacheTypeV) ? plan.CacheTypeV : settings.CacheTypeV,
            Threads = options.Threads is > 0 ? settings.Threads : plan.Threads,
            BatchSize = options.BatchSize is > 0 ? settings.BatchSize : plan.BatchSize,
            MicroBatchSize = options.MicroBatchSize is > 0 ? settings.MicroBatchSize : plan.MicroBatchSize,
            Tuning = plan,
            TuningNotes = notes,
        };
    }

    /// <summary>Prefixes a remark with the marker of the tuner, without doubling it.</summary>
    /// <param name="text">The remark.</param>
    /// <returns>The remark with the prefix of the tuner in front of it.</returns>
    private static string Note(string text)
    {
        string trimmed = text.Trim();
        return trimmed.StartsWith(NotePrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : NotePrefix + trimmed;
    }

    /// <summary>Describes the size of the model in the words of the user.</summary>
    /// <param name="facts">The facts about the model.</param>
    /// <returns>The size label of the model, or a fallback when the file does not carry one.</returns>
    private static string DescribeSize(ModelFacts facts)
    {
        if (!string.IsNullOrWhiteSpace(facts.SizeLabel))
        {
            return facts.SizeLabel;
        }

        return facts.ParameterCountMillions > 0
            ? $"{facts.ParameterCountMillions / 1000d:0.#}B"
            : "unknown size";
    }

    /// <summary>Describes how many layers of the model ended up on the graphics card.</summary>
    /// <param name="plan">The plan of the estimator.</param>
    /// <param name="facts">The facts about the model.</param>
    /// <returns>A one line description of the offload.</returns>
    private static string DescribeLayers(AutoTuneResult plan, ModelFacts facts)
    {
        if (plan.GpuLayers < 0)
        {
            return facts.BlockCount > 0
                ? $"all {facts.BlockCount} layers on the GPU"
                : "all layers on the GPU";
        }

        return $"{plan.GpuLayers} of {facts.BlockCount} layers on the GPU";
    }

    /// <summary>Reads the size of a file, treating a missing file as no size at all.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The size in bytes, or <see langword="null"/> when the file is not there.</returns>
    private static long? FileSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            FileInfo info = new(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Turns a number of bytes into MiB.</summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>The number of MiB, written with two decimals.</returns>
    private static string MiB(long bytes)
    {
        return (bytes / BytesPerMiB).ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>Turns a number of bytes into MiB.</summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>The number of MiB, written with two decimals.</returns>
    private static string MiB(ulong bytes)
    {
        return (bytes / BytesPerMiB).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
