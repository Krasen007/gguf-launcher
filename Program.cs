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

        string modelPath = args[0];

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

        // Sibling files: mmproj and speculative draft
        string lowerName = Path.GetFileName(modelFullPath).ToLowerInvariant();
        if (!lowerName.StartsWith("mmproj") && !IsDraft(lowerName))
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
                    Console.WriteLine($"[launcher] draft: {Path.GetFileName(draft)} ({specType})");
                }
            }
        }

        // Merge all args sources
        List<string> extra = args.Length > 1 ? [.. args[1..]] : [];
        List<string> merged = LauncherArgs.Merge(exeDir, modelFullPath, extra);

        // Add host/port
        string host = LauncherArgs.GetLastValue(merged, "--host") ?? "0.0.0.0";
        string port = LauncherArgs.GetLastValue(merged, "--port") ?? "8080";

        // Map wildcard bind addresses to loopback for browser
        if (host == "0.0.0.0" || host == "::" || string.IsNullOrEmpty(host))
        {
            host = "127.0.0.1";
        }

        finalArgs.Add("--host");
        finalArgs.Add(host);
        finalArgs.Add("--port");
        finalArgs.Add(port);
        finalArgs.Add("--alias");
        finalArgs.Add(modelStem);

        // Add merged args (excluding host/port which we set explicitly)
        foreach (string arg in merged)
        {
            if (arg != "--host" && arg != "--port" && arg != "--alias" && arg != "-m")
            {
                finalArgs.Add(arg);
            }
        }

        // Get --no-browser before filtering
        bool noBrowser = LauncherArgs.HasNoBrowser(merged);

        string url = $"http://{host}:{port}/";

        // Print command
        Console.WriteLine($"[launcher] {server} {string.Join(" ", finalArgs)}");
        Console.WriteLine();

        // Open browser if requested
        if (!noBrowser)
        {
            _ = OpenWhenReadyAsync(url);
        }

        // Run llama-server directly, passing through Ctrl+C
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = server,
                    Arguments = string.Join(" ", finalArgs.Select(a => $"\"{a}\"")),
                    WorkingDirectory = modelDir,
                    UseShellExecute = false,
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
        foreach (string file in Directory.GetFiles(dir, "*.gguf"))
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

        foreach (string file in Directory.GetFiles(dir, "*.gguf"))
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
                    catch
                    {
                        // Ignore browser launch failures
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
        Console.WriteLine("       gguf-launcher.exe --register | --unregister");
        Console.WriteLine();
        Console.WriteLine("Argument precedence (later wins):");
        Console.WriteLine("  1. built-in defaults");
        Console.WriteLine("  2. gguf-launcher.args (next to this exe)");
        Console.WriteLine("  3. <model-name>.args (next to the .gguf)");
        Console.WriteLine("  4. command-line extras");
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