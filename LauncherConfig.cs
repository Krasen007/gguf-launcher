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

        // Filter out --no-browser from final args (used as a flag only)
        return FilterBrowserFlag(args);
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