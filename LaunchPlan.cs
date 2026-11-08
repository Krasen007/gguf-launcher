namespace GgufLauncher;

/// <summary>
/// The resolved launch for one model: the complete llama-server argument list, the browser
/// URL, and the facts the caller needs to print logs or write the per-model args file.
/// </summary>
public sealed record LaunchPlan(
    string ModelFullPath,
    string ModelDir,
    bool HasPerModelArgs,
    IReadOnlyList<string> FinalArgs,
    bool NoBrowser,
    string Url);

/// <summary>
/// Builds the effective llama-server argument list for a model. This is the single source of
/// truth used both by the headless launcher and the quick-pick UI preview, so what the UI
/// shows is exactly what a launch would run.
/// </summary>
public static class LaunchPlanner
{
    public static LaunchPlan Create(
        string exeDir,
        string modelPath,
        IReadOnlyList<string> extraArgs,
        Action<string>? log = null)
    {
        string modelFullPath = Path.GetFullPath(modelPath);
        string modelDir = Path.GetDirectoryName(modelFullPath) ?? ".";

        // Build args: model path first, then find siblings, then merge args
        List<string> finalArgs = ["-m", modelFullPath];

        // An existing per-model args file is the complete argument set: do not add
        // companion-file flags on top of it, and do not add the baked-in defaults.
        string perModelArgsPath = LauncherArgs.PerModelArgsPath(modelFullPath);
        bool hasPerModelArgs = File.Exists(perModelArgsPath);
        if (hasPerModelArgs)
        {
            log?.Invoke($"[launcher] args file: {perModelArgsPath}");
        }

        // Sibling files: mmproj and speculative draft
        string lowerName = Path.GetFileName(modelFullPath).ToLowerInvariant();
        if (!hasPerModelArgs && !lowerName.StartsWith("mmproj") && !IsDraft(lowerName))
        {
            string? mmproj = FindSibling(modelDir, "mmproj", "4x");
            if (mmproj is not null)
            {
                finalArgs.Add("--mmproj");
                finalArgs.Add(mmproj);
                log?.Invoke($"[launcher] mmproj: {Path.GetFileName(mmproj)}");
            }

            string? draft = FindDraft(modelDir);
            if (draft is not null)
            {
                string specType = InferSpecType(draft);
                if (!string.IsNullOrEmpty(specType))
                {
                    finalArgs.Add("--spec-draft-model");
                    finalArgs.Add(draft);
                    finalArgs.Add("--spec-type");
                    finalArgs.Add(specType);
                    finalArgs.Add("--spec-draft-p-min");
                    finalArgs.Add("0.00");
                    finalArgs.Add("--spec-draft-n-min");
                    finalArgs.Add("0");
                    finalArgs.Add("--spec-draft-n-max");
                    finalArgs.Add("3");
                    finalArgs.Add("--cache-type-k-draft");
                    finalArgs.Add("q8_0");
                    finalArgs.Add("--cache-type-v-draft");
                    finalArgs.Add("q8_0");
                    finalArgs.Add("-ngld");
                    finalArgs.Add("99");
                    // Keep draft sampling on CPU - Vulkan backend aborts when sampling on GPU
                    finalArgs.Add("--no-spec-draft-backend-sampling");
                    log?.Invoke($"[launcher] draft: {Path.GetFileName(draft)} ({specType})");
                }
            }
        }

        // Merge all args sources
        List<string> merged = LauncherArgs.Merge(exeDir, modelFullPath, extraArgs);

        (bool noBrowser, List<string> serverArgs) = LauncherArgs.SplitLauncherFlags(merged);

        finalArgs.AddRange(LauncherArgs.ServerDefaultArgs(hasPerModelArgs));

        for (int i = 0; i < serverArgs.Count; i++)
        {
            string arg = serverArgs[i];
            // -m is already added with the model path.
            if (arg == "-m")
            {
                if (i + 1 < serverArgs.Count && !serverArgs[i + 1].StartsWith('-'))
                {
                    i++;
                }
                continue;
            }
            finalArgs.Add(arg);
        }

        finalArgs = LauncherArgs.Normalize(finalArgs);

        (string browserHost, string port) = LauncherArgs.BrowserEndpoint(finalArgs);
        string url = $"http://{browserHost}:{port}/";

        return new LaunchPlan(modelFullPath, modelDir, hasPerModelArgs, finalArgs, noBrowser, url);
    }

    private static bool IsDraft(string lowerName)
    {
        return lowerName.EndsWith(".gguf") &&
            (lowerName.StartsWith("dspark-") || lowerName.Contains("draft") || lowerName.Contains("-mtp"));
    }

    private static string? FindSibling(string dir, string prefix, string prefer)
    {
        if (!Directory.Exists(dir)) return null;

        string? first = null;
        foreach (string file in Directory.GetFiles(dir, "*.gguf").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(file).ToLowerInvariant();
            if (!name.Contains(prefix)) continue;

            if (!string.IsNullOrEmpty(prefer) && name.Contains(prefer))
            {
                return file;
            }

            first ??= file;
        }

        return first;
    }

    private static string? FindDraft(string dir)
    {
        if (!Directory.Exists(dir)) return null;

        foreach (string file in Directory.GetFiles(dir, "*.gguf").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string stem = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (stem.Contains("dspark") || stem.Contains("draft") || stem.Contains("-mtp"))
            {
                // Only return if we can infer a spec type
                if (!string.IsNullOrEmpty(InferSpecType(file)))
                {
                    return file;
                }
            }
        }

        return null;
    }

    private static string InferSpecType(string draftPath)
    {
        string stem = Path.GetFileNameWithoutExtension(draftPath).ToLowerInvariant();
        if (stem.Contains("dspark")) return "draft-dspark";
        if (stem.Contains("-mtp")) return "draft-mtp";
        return "";
    }
}
