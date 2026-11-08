using System.Runtime.Versioning;
using System.Text;

[assembly: SupportedOSPlatform("Windows")]

namespace GgufLauncher;

/// <summary>
/// Entry point - simplified launcher matching the Go version's behavior.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        UseUtf8Console();

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp();
            return 0;
        }

        switch (args[0])
        {
            case "--register":
                FileAssociation.Register();
                return 0;

            case "--unregister":
                FileAssociation.Unregister();
                return 0;
        }

        // Accept both `model.gguf` and `-m model.gguf` (llama-server style).
        string modelPath;
        string[] rest;
        if (args[0] is "-m" or "--model")
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("[launcher] missing model path after -m");
                PrintHelp();
                return 1;
            }
            modelPath = args[1];
            rest = args[2..];
        }
        else
        {
            modelPath = args[0];
            rest = args[1..];
        }

        // Validate model exists
        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"[launcher] model not found: {modelPath}");
            return 1;
        }

        string modelFullPath = Path.GetFullPath(modelPath);
        string modelDir = Path.GetDirectoryName(modelFullPath) ?? ".";
        string modelStem = Path.GetFileNameWithoutExtension(modelFullPath);

        // Find llama-server
        string exeDir = AppContext.BaseDirectory;
        string? server = LlamaServerLocator.Find(exeDir);
        if (server is null)
        {
            Console.Error.WriteLine("[launcher] llama-server.exe not found.");
            Console.Error.WriteLine("[launcher] put it next to this exe, on PATH, or set LLAMA_SERVER.");
            return 1;
        }

        // Build args: model path first, then find siblings, then merge args
        List<string> finalArgs = ["-m", modelFullPath];

        // An existing per-model args file is the complete argument set: do not add
        // companion-file flags on top of it, and do not add the baked-in defaults.
        string perModelArgsPath = LauncherArgs.PerModelArgsPath(modelFullPath);
        bool hasPerModelArgs = File.Exists(perModelArgsPath);
        if (hasPerModelArgs)
        {
            Console.WriteLine($"[launcher] args file: {perModelArgsPath}");
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
                Console.WriteLine($"[launcher] mmproj: {Path.GetFileName(mmproj)}");
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
                    Console.WriteLine($"[launcher] draft: {Path.GetFileName(draft)} ({specType})");
                }
            }
        }

        // Merge all args sources
        List<string> extra = [.. rest];
        List<string> merged = LauncherArgs.Merge(exeDir, modelFullPath, extra);

        // --no-browser is a launcher-only flag: detect it before stripping it from the
        // args forwarded to llama-server. Once stripped it can no longer be detected.
        bool noBrowser = LauncherArgs.HasNoBrowser(merged);
        List<string> serverArgs = LauncherArgs.FilterBrowserFlag(merged);

        // Add host/port - default to 0.0.0.0 and 8080, allow overrides from args
        string serverHost = "0.0.0.0";
        string port = "8080";

        // Check args for explicit values (later flags win)
        string? mergedHost = LauncherArgs.GetLastValue(serverArgs, "--host");
        string? mergedPort = LauncherArgs.GetLastValue(serverArgs, "--port");

        if (!string.IsNullOrEmpty(mergedHost))
            serverHost = mergedHost;
        if (!string.IsNullOrEmpty(mergedPort))
            port = mergedPort;

        // Map wildcard bind addresses to loopback for browser URL only
        string browserHost = serverHost;
        if (browserHost == "0.0.0.0" || browserHost == "::" || string.IsNullOrEmpty(browserHost))
        {
            browserHost = "127.0.0.1";
        }

        finalArgs.Add("--host");
        finalArgs.Add(serverHost);
        finalArgs.Add("--port");
        finalArgs.Add(port);
        finalArgs.Add("--alias");
        finalArgs.Add(modelStem);

        // Add merged args (but keep host/port/alias/-m if explicitly provided - they're added above)
        for (int i = 0; i < serverArgs.Count; i++)
        {
            string arg = serverArgs[i];
            // Skip duplicates - we already added these explicitly
            if (arg == "--host" || arg == "--port" || arg == "--alias" || arg == "-m")
            {
                if (i + 1 < serverArgs.Count && !serverArgs[i + 1].StartsWith('-'))
                {
                    i++; // skip the value too
                }
                continue;
            }
            finalArgs.Add(arg);
        }

        string url = $"http://{browserHost}:{port}/";

        finalArgs = LauncherArgs.Normalize(finalArgs);

        try
        {
            string? argsFile = LauncherArgs.CreateEffectiveArgsFileIfMissing(modelFullPath, finalArgs);
            if (argsFile is not null)
            {
                Console.WriteLine($"[launcher] created args file: {argsFile}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[launcher] could not write args file: {ex.Message}");
        }

        // Print command
        Console.WriteLine($"[launcher] {server} {string.Join(" ", finalArgs)}");
        Console.WriteLine();

        // Open browser if requested
        if (!noBrowser)
        {
            _ = OpenWhenReadyAsync(url);
        }

        // Run llama-server directly, forwarding Ctrl+C so the server shuts down cleanly.
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = server,
                    // Arguments = string.Join(" ", finalArgs.Select(a => $"\"{a}\"")),
                    WorkingDirectory = modelDir,
                    UseShellExecute = false,
                }
            };
            // Use ArgumentList so arguments are quoted correctly by the runtime,
            // avoiding breakage when an argument contains a literal quote character.
            foreach (string arg in finalArgs)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Process may have already exited.
                }
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"[launcher] llama-server exited: exit code {process.ExitCode}");
            }

            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[launcher] failed to start: {ex.Message}");
            return 1;
        }
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

    private static async Task OpenWhenReadyAsync(string url)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddMinutes(15);
        bool reported = false;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(url + "health");
                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        if (!reported)
                        {
                            Console.Error.WriteLine($"[launcher] could not open browser: {ex.Message}");
                            reported = true;
                        }
                    }
                    return;
                }
            }
            catch
            {
                // Not ready yet
            }

            await Task.Delay(500);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("gguf-launcher.exe model.gguf [llama-server args...]");
        Console.WriteLine("       gguf-launcher.exe -m model.gguf [llama-server args...]");
        Console.WriteLine("       gguf-launcher.exe --register | --unregister");
        Console.WriteLine();
        Console.WriteLine("Argument precedence (later wins):");
        Console.WriteLine("  1. built-in defaults");
        Console.WriteLine("  2. gguf-launcher.args (next to this exe)");
        Console.WriteLine("  3. <model-name>.args (next to the .gguf)");
        Console.WriteLine("  4. command-line extras");
        Console.WriteLine();
        Console.WriteLine("Use --no-browser to skip opening the browser.");
    }

    private static void UseUtf8Console()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // Ignore failures (redirected console)
        }
    }
}