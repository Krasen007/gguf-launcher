namespace GgufLauncher.Launch;

/// <summary>
/// Finds the llama.cpp server binary. Lookup order is deliberate: an explicit
/// path wins, then the config, then PATH, then the winget package folder
/// (whose versioned subfolder changes on every llama.cpp upgrade, so it is
/// never hardcoded).
/// </summary>
public static class LlamaServerLocator
{
    private const string WingetPackageRoot = @"Microsoft\WinGet\Packages";
    private const string WingetPackagePrefix = "ggml.llamacpp";

    /// <summary>Returns the resolved server, or null when nothing could be found.</summary>
    public static LlamaServer? Locate(string? explicitPath)
    {
        LlamaServer? fromPath = ResolveExplicit(explicitPath);
        if (fromPath is not null)
        {
            return fromPath;
        }

        LlamaServer? onPath = FindOnPath("llama-server.exe");
        if (onPath is not null)
        {
            return onPath;
        }

        LlamaServer? fromWinget = FindInWingetPackage();
        if (fromWinget is not null)
        {
            return fromWinget;
        }

        LlamaServer? unified = FindOnPath("llama.exe");
        return unified is null ? null : unified with { UseServeSubcommand = true };
    }

    private static LlamaServer? ResolveExplicit(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        string expanded = Environment.ExpandEnvironmentVariables(candidate.Trim());
        if (Directory.Exists(expanded))
        {
            expanded = Path.Combine(expanded, "llama-server.exe");
        }

        if (File.Exists(expanded) is false)
        {
            return null;
        }

        string full = Path.GetFullPath(expanded);
        bool unified = Path.GetFileName(full).Equals("llama.exe", StringComparison.OrdinalIgnoreCase);
        return new LlamaServer(full, unified);
    }

    private static LlamaServer? FindOnPath(string fileName)
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
        {
            return null;
        }

        foreach (string dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(dir.Trim('"'), fileName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return new LlamaServer(Path.GetFullPath(candidate), false);
            }
        }

        return null;
    }

    private static LlamaServer? FindInWingetPackage()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
        {
            return null;
        }

        string packagesRoot = Path.Combine(localAppData, WingetPackageRoot);
        if (Directory.Exists(packagesRoot) is false)
        {
            return null;
        }

        string[] candidates = Directory.GetFiles(packagesRoot, "llama-server.exe", SearchOption.AllDirectories);
        if (candidates.Length == 0)
        {
            return null;
        }

        // Several package versions can coexist; the newest file wins.
        string[] wingetCandidates = candidates
            .Where(p => p.Contains(WingetPackagePrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        string newest = wingetCandidates.Length > 0
            ? wingetCandidates.OrderByDescending(p => File.GetLastWriteTimeUtc(p)).First()
            : candidates[0];

        return new LlamaServer(newest, false);
    }
}
