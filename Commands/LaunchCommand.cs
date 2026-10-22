using GgufLauncher.Cli;
using GgufLauncher.Configuration;
using GgufLauncher.Launch;

namespace GgufLauncher.Commands;

/// <summary>Starts a llama.cpp server for a model file.</summary>
public static class LaunchCommand
{
    /// <summary>Exit code used when the chosen file is not a servable model.</summary>
    public const int NotServableExitCode = 3;

    /// <summary>Exit code used when no llama.cpp server binary can be found.</summary>
    public const int ServerMissingExitCode = 4;

    /// <summary>Exit code used when no TCP port is available.</summary>
    public const int NoFreePortExitCode = 5;

    /// <summary>Runs the launch and returns the process exit code.</summary>
    public static async Task<int> RunAsync(CliOptions options, CancellationToken cancellationToken)
    {
        if (options.ModelPath is null)
        {
            Console.Error.WriteLine("[launcher] no model file was given.");
            return 2;
        }

        string modelPath = options.ModelPath;
        LauncherConfig config = LauncherConfig.Load(options.ConfigPath);
        PrintWarnings(config);

        ResolvedModel? model = ResolveServableModel(modelPath, config);
        if (model is null)
        {
            return NotServableExitCode;
        }

        LlamaServer? server = LocateServer(options, config);
        if (server is null)
        {
            return ServerMissingExitCode;
        }

        ModelOverride? overrides = config.FindOverride(model.Path);
        LaunchSettings settings = LaunchSettings.Resolve(config, overrides, options);
        if (!TryReservePort(settings, options, out LaunchSettings resolved))
        {
            return NoFreePortExitCode;
        }

        // The tuner fills in the numbers that fit the model into the free video
        // memory, and leaves the ones the user asked for alone.
        LaunchSettings tuned = AutoTuner.Apply(resolved, model, AutoTuner.BuildOptions(config, overrides, options));

        LaunchPlan plan = ServerArguments.Build(model, tuned, server);
        PrintBanner(model, plan, server, config);

        return await CompleteLaunchAsync(plan, tuned, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reports the problems found while reading the configuration, so a broken or
    /// unreadable config file is visible instead of being silently ignored.
    /// </summary>
    private static void PrintWarnings(LauncherConfig config)
    {
        foreach (string warning in config.Warnings)
        {
            Console.Error.WriteLine($"[launcher] {warning}");
        }
    }

    /// <summary>
    /// Resolves the model file and rejects the ones that cannot be served, such as a
    /// projector sidecar or a draft model. Returns null after the reason was written
    /// to the console, because every rejection ends in the same exit code.
    /// </summary>
    private static ResolvedModel? ResolveServableModel(string modelPath, LauncherConfig config)
    {
        ResolvedModel model;
        try
        {
            model = ModelResolver.Resolve(modelPath, config);
        }
        catch (Exception ex) when (ex is Gguf.GgufException or IOException)
        {
            Console.Error.WriteLine($"[launcher] cannot use '{modelPath}': {ex.Message}");
            return null;
        }

        return IsServable(model) ? model : null;
    }

    /// <summary>
    /// Finds the llama.cpp server binary. When it is missing the console explains how
    /// to install it, because a double click in Explorer gives the user no other clue.
    /// </summary>
    private static LlamaServer? LocateServer(CliOptions options, LauncherConfig config)
    {
        LlamaServer? server = LlamaServerLocator.Locate(options.LlamaServerPath ?? config.LlamaServerPath);
        if (server is null)
        {
            Console.Error.WriteLine("[launcher] llama-server.exe was not found.");
            Console.Error.WriteLine("[launcher] install llama.cpp (winget install ggml.llamacpp) or pass --llama-server <path>.");
        }

        return server;
    }

    /// <summary>
    /// Completes the launch: a dry run only prints the command line, a real launch
    /// starts the server and waits for it, passing the exit code of the server through.
    /// </summary>
    private static async Task<int> CompleteLaunchAsync(LaunchPlan plan, LaunchSettings settings, CliOptions options, CancellationToken cancellationToken)
    {
        if (options.DryRun)
        {
            PrintCommandLine(plan);
            return 0;
        }

        ServerHost host = new(plan) { OpenBrowserRequested = settings.OpenBrowser };
        return await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsServable(ResolvedModel model)
    {
        if (model.IsProjector)
        {
            Console.Error.WriteLine($"[launcher] '{Path.GetFileName(model.Path)}' is a multimodal projector, not a model.");
            Console.Error.WriteLine("[launcher] double-click the model file instead - the launcher will find the projector beside it.");
            return false;
        }

        if (model.IsDraftOnly)
        {
            Console.Error.WriteLine($"[launcher] '{Path.GetFileName(model.Path)}' is a draft model, not a servable model.");
            Console.Error.WriteLine("[launcher] double-click the main model file instead.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Keeps the requested port when it is free, otherwise moves to the next free
    /// port so a double-click never fails because something else is listening.
    /// </summary>
    private static bool TryReservePort(LaunchSettings settings, CliOptions options, out LaunchSettings resolved)
    {
        resolved = settings;

        if (PortAllocator.IsAvailable(settings.Host, settings.Port))
        {
            return true;
        }

        PortListener? listener = PortAllocator.Query(settings.Port);
        string reason = listener is null ? $"port {settings.Port} is in use" : listener.Describe();
        Console.Error.WriteLine($"[launcher] {reason}.");

        int free = PortAllocator.FindFree(settings.Host, settings.Port);
        if (free < 0)
        {
            Console.Error.WriteLine("[launcher] no free port found near the requested one.");
            return false;
        }

        if (!options.Quiet)
        {
            Console.Error.WriteLine($"[launcher] using port {free} instead.");
        }

        resolved = settings with { Port = free };
        return true;
    }

    private static void PrintBanner(ResolvedModel model, LaunchPlan plan, LlamaServer server, LauncherConfig config)
    {
        Console.WriteLine($"[launcher] {model.DisplayName}");
        Console.WriteLine($"[launcher] {model.Summary}");
        Console.WriteLine($"[launcher] server: {server.Display} ({server.ExecutablePath})");
        if (config.SourcePath is not null)
        {
            Console.WriteLine($"[launcher] config: {config.SourcePath}");
        }

        foreach (string note in plan.Notes)
        {
            Console.WriteLine($"[launcher] {note}");
        }
    }

    private static void PrintCommandLine(LaunchPlan plan)
    {
        Console.WriteLine();
        Console.WriteLine($"\"{plan.ExecutablePath}\" {string.Join(' ', plan.Arguments)}");
        Console.WriteLine();
        Console.WriteLine($"[launcher] url: {plan.Url}");
    }
}
