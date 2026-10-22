namespace GgufLauncher;

/// <summary>
/// Finds llama-server.exe, matching the Go version's lookup order:
/// 1. LLAMA_SERVER env var
/// 2. Next to the launcher exe
/// 3. On PATH
/// 4. In the WinGet packages folder
/// </summary>
public static class LlamaServerLocator
{
    /// <summary>
    /// Finds llama-server.exe and returns its path, or null if not found.
    /// </summary>
    public static string? Find(string exeDir)
    {
        // 1. LLAMA_SERVER env var
        string? envPath = Environment.GetEnvironmentVariable("LLAMA_SERVER");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            if (File.Exists(envPath))
            {
                return Path.GetFullPath(envPath);
            }
        }

        // 2. Next to the launcher
        string local = Path.Combine(exeDir, "llama-server.exe");
        if (File.Exists(local))
        {
            return local;
        }

        // 3. On PATH
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVar))
        {
            foreach (string dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(dir.Trim('"'), "llama-server.exe");
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        // 4. WinGet packages folder
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            string packagesRoot = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(packagesRoot))
            {
                string[] matches = Directory.GetFiles(packagesRoot, "llama-server.exe", SearchOption.AllDirectories);
                if (matches.Length > 0)
                {
                    // Return the newest version
                    return matches
                        .Where(p => p.Contains("ggml.llamacpp", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
                        .FirstOrDefault()
                        ?? matches[0];
                }
            }
        }

        return null;
    }
}