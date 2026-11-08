using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows.Forms;

[assembly: SupportedOSPlatform("Windows")]

namespace GgufLauncher;

/// <summary>
/// Entry point - simplified launcher matching the Go version's behavior.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        UseUtf8Console();

        switch (GetMode(args))
        {
            case AppMode.Ui:
                return RunUi();

            case AppMode.Help:
                PrintHelp();
                return 0;

            case AppMode.Register:
                FileAssociation.Register();
                return 0;

            case AppMode.Unregister:
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
        RecordRecentModel(modelFullPath);

        // Find llama-server
        string exeDir = AppContext.BaseDirectory;
        string? server = LlamaServerLocator.Find(exeDir);
        if (server is null)
        {
            Console.Error.WriteLine("[launcher] llama-server.exe not found.");
            Console.Error.WriteLine("[launcher] put it next to this exe, on PATH, or set LLAMA_SERVER.");
            return 1;
        }

        LaunchPlan plan = LaunchPlanner.Create(exeDir, modelFullPath, rest, Console.WriteLine);
        List<string> finalArgs = [.. plan.FinalArgs];
        string modelDir = plan.ModelDir;
        bool noBrowser = plan.NoBrowser;
        string url = plan.Url;

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

    public static AppMode GetMode(string[] args)
    {
        if (args.Length == 0)
        {
            return AppMode.Ui;
        }

        return args[0] switch
        {
            "-h" or "--help" => AppMode.Help,
            "--register" => AppMode.Register,
            "--unregister" => AppMode.Unregister,
            _ => AppMode.Launch,
        };
    }

    private static int RunUi()
    {
        // A double-clicked console exe starts with a console window. Detach it before showing
        // the native window; when the launcher was started from a terminal, that terminal stays.
        FreeConsole();

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            LauncherUiModel model = new(
                AppContext.BaseDirectory,
                RecentModelStore.Default,
                LaunchModelInNewProcess);
            Application.Run(new LauncherForm(model));
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not start the UI: {ex.Message}",
                "GGUF Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private static void RecordRecentModel(string modelFullPath)
    {
        try
        {
            RecentModelStore.Default.Record(modelFullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[launcher] could not update recent models: {ex.Message}");
        }
    }

    private static void LaunchModelInNewProcess(string modelPath)
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            throw new InvalidOperationException("launcher executable path is unavailable");
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"\"{modelPath}\"",
            UseShellExecute = true,
        });
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
        Console.WriteLine("gguf-launcher.exe                  (opens the native quick-pick window)");
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

public enum AppMode
{
    Ui,
    Help,
    Launch,
    Register,
    Unregister,
}