namespace GgufLauncher;

/// <summary>
/// Simplified configuration: reads .args files instead of JSON.
/// Global: gguf-launcher.args next to the exe
/// Per-model: &lt;model-name&gt;.args next to the .gguf
/// Later flags win, command line wins over both.
/// </summary>
public static class LauncherArgs
{
    /// <summary>
    /// Merges three sources of arguments: global args, per-model args, and command line.
    /// Later sources override earlier ones.
    /// </summary>
    public static List<string> Merge(string exeDir, string modelPath, IReadOnlyList<string> extraArgs)
    {
        List<string> args = [];

        // Built-in defaults (same as Go version)
        args.AddRange(BaseArgs);

        // Global args (next to exe)
        args.AddRange(ReadArgsFile(Path.Combine(exeDir, "gguf-launcher.args")));

        // Per-model args (next to .gguf)
        string modelDir = Path.GetDirectoryName(modelPath) ?? "";
        string modelStem = Path.GetFileNameWithoutExtension(modelPath);
        args.AddRange(ReadArgsFile(Path.Combine(modelDir, modelStem + ".args")));

        // Command line extras
        args.AddRange(extraArgs);

        // NOTE: do NOT strip --no-browser here. Program.Main needs to detect it before the
        // launcher-only flag is removed from the args forwarded to llama-server.
        return args;
    }

    /// <summary>
    /// Reads a simple .args file: one or more flags per line, '#' starts a comment.
    /// </summary>
    public static List<string> ReadArgsFile(string path)
    {
        List<string> result = [];

        if (!File.Exists(path))
        {
            return result;
        }

        try
        {
            string[] lines = File.ReadAllLines(path);
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                {
                    continue;
                }

                // Handle BOM
                if (trimmed[0] == '\uFEFF')
                {
                    trimmed = trimmed[1..];
                }

                // Split on whitespace (handles multi-flag lines)
                string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                result.AddRange(parts);
            }
        }
        catch (IOException)
        {
            // Ignore unreadable args files
        }

        return result;
    }

    /// <summary>
    /// Extracts --no-browser and returns the remaining args.
    /// </summary>
    public static List<string> FilterBrowserFlag(List<string> args)
    {
        List<string> filtered = [];
        foreach (string arg in args)
        {
            if (arg == "--no-browser")
            {
                continue;
            }
            filtered.Add(arg);
        }
        return filtered;
    }

    /// <summary>
    /// Gets the last value of a flag from the args (later flags win).
    /// </summary>
    public static string? GetLastValue(List<string> args, string name)
    {
        string? value = null;
        for (int i = 0; i < args.Count; i++)
        {
            // Only consume the following token when it is not itself a flag,
            // so a trailing/malformed flag cannot eat the next argument.
            if (args[i] == name && i + 1 < args.Count && !args[i + 1].StartsWith('-'))
            {
                value = args[i + 1];
            }
        }
        return value;
    }

    /// <summary>
    /// Checks if --no-browser is present.
    /// </summary>
    public static bool HasNoBrowser(List<string> args)
    {
        return args.Contains("--no-browser");
    }

    /// <summary>
    /// Collapses repeated flags to their last occurrence, matching llama-server's
    /// "later wins" behavior. Repeatable flags (multiple LoRAs, etc.) keep every
    /// occurrence so no functionality is dropped.
    /// </summary>
    public static List<string> Normalize(IReadOnlyList<string> args)
    {
        List<(string? Flag, string? Value)> units = ParseUnits(args);

        Dictionary<string, int> lastIndex = new(StringComparer.Ordinal);
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].Flag is string flag && !RepeatableFlags.Contains(flag))
            {
                lastIndex[flag] = i;
            }
        }

        List<string> result = [];
        for (int i = 0; i < units.Count; i++)
        {
            (string? flag, string? value) = units[i];
            if (flag is null)
            {
                result.Add(value!);
                continue;
            }

            if (!RepeatableFlags.Contains(flag) && lastIndex[flag] != i)
            {
                continue;
            }

            result.Add(flag);
            if (value is not null)
            {
                result.Add(value);
            }
        }

        return result;
    }

    /// <summary>
    /// Writes the effective llama-server arguments to &lt;model-name&gt;.args next to the
    /// model, so the exact flags used for a launch are recorded (and can be tweaked).
    /// The model path (-m) is omitted: it is always the .gguf next to the file.
    /// Returns the path that was written.
    /// </summary>
    public static string WriteEffectiveArgsFile(string modelPath, IReadOnlyList<string> finalArgs)
    {
        string modelFullPath = Path.GetFullPath(modelPath);
        string modelDir = Path.GetDirectoryName(modelFullPath) ?? ".";
        string modelStem = Path.GetFileNameWithoutExtension(modelFullPath);
        string path = Path.Combine(modelDir, modelStem + ".args");

        List<string> lines =
        [
            "# Generated by gguf-launcher. Rewritten on every launch; edits here are lost.",
            "# One or more flags per line, '#' starts a comment.",
            "# The model path (-m) is omitted - it is always the .gguf next to this file.",
            "",
        ];

        foreach ((string? flag, string? value) in ParseUnits(Normalize(finalArgs)))
        {
            if (flag is null)
            {
                lines.Add(value!);
            }
            else if (flag == "-m" || flag == "--model")
            {
                continue;
            }
            else
            {
                lines.Add(value is null ? flag : $"{flag} {value}");
            }
        }

        File.WriteAllLines(path, lines);
        return path;
    }

    /// <summary>
    /// Splits a flat argument list into flag/value units. A token is treated as a flag
    /// when it starts with '-' followed by a letter or a second '-', so negative numbers
    /// stay attached to their flag as values.
    /// </summary>
    private static List<(string? Flag, string? Value)> ParseUnits(IReadOnlyList<string> args)
    {
        List<(string?, string?)> units = [];
        for (int i = 0; i < args.Count; i++)
        {
            string token = args[i];
            if (!IsFlag(token))
            {
                units.Add((null, token));
                continue;
            }

            string? value = null;
            if (i + 1 < args.Count && !IsFlag(args[i + 1]))
            {
                value = args[i + 1];
                i++;
            }

            units.Add((token, value));
        }

        return units;
    }

    private static bool IsFlag(string token)
    {
        return token.Length > 1 && token[0] == '-' && (token[1] == '-' || char.IsLetter(token[1]));
    }

    /// <summary>
    /// llama-server flags that accumulate instead of overwriting; all occurrences are
    /// kept when normalizing.
    /// </summary>
    private static readonly HashSet<string> RepeatableFlags = new(StringComparer.Ordinal)
    {
        "--lora",
        "--lora-scaled",
        "--control-vector",
        "--control-vector-scaled",
        "--override-kv",
        "--logit-bias",
    };

    /// <summary>
    /// Built-in defaults - matching the Go version's baseArgs.
    /// </summary>
    private static readonly string[] BaseArgs =
    [
        "-c", "98192",
        "-b", "512", "-ub", "512",
        "-t", "6",
        "-np", "1",
        "-ngl", "99",
        "-fa", "on",
        "--jinja",
        "--cache-type-k", "q4_0", "--cache-type-v", "q4_0",
        "--temp", "0.6", "--top-p", "0.95", "--top-k", "20", "--min-p", "0.0",
        "--presence-penalty", "0.0", "--repeat-penalty", "1.0",
    ];
}