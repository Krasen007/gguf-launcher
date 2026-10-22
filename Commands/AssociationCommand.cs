using GgufLauncher.Cli;
using GgufLauncher.Configuration;
using GgufLauncher.Launch;
using GgufLauncher.Shell;

namespace GgufLauncher.Commands;

/// <summary>Registers, removes and reports the per-user <c>.gguf</c> file association.</summary>
public static class AssociationCommand
{
    /// <summary>Registers the launcher so double-clicking a model starts it.</summary>
    public static int Register(CliOptions options)
    {
        FileAssociation association = Create();

        association.Register();

        Console.WriteLine($"[launcher] {FileAssociation.DefaultExtension} files now open with {association.ExecutableFileName}.");
        Console.WriteLine($"[launcher] prog id: {association.ProgId}");
        Console.WriteLine($"[launcher] command: {association.ProgIdKeyPath}\\shell\\open\\command");
        Console.WriteLine("[launcher] if Windows still opens the model elsewhere, pick the launcher once in");
        Console.WriteLine("[launcher] 'Open with' or 'Default apps' - Windows 11 does not allow apps to set that.");
        return 0;
    }

    /// <summary>Removes the association again.</summary>
    public static int Unregister(CliOptions options)
    {
        FileAssociation association = Create();

        association.Unregister();

        Console.WriteLine($"[launcher] the {FileAssociation.DefaultExtension} association was removed.");
        return 0;
    }

    /// <summary>Shows the configuration, the server location and the current association.</summary>
    public static int Status(CliOptions options)
    {
        LauncherConfig config = LauncherConfig.Load(options.ConfigPath);

        foreach (string warning in config.Warnings)
        {
            Console.Error.WriteLine($"[launcher] {warning}");
        }

        Console.WriteLine("[launcher] configuration");
        Console.WriteLine($"  source    : {config.SourcePath ?? "(defaults, no config file found)"}");
        Console.WriteLine($"  host      : {config.Host}");
        Console.WriteLine($"  port      : {config.Port}");
        Console.WriteLine($"  context   : {(config.ContextSize is > 0 ? config.ContextSize.Value.ToString() : $"model default, capped at {config.MaxContextSize}")}");
        Console.WriteLine($"  gpu layers: {(config.GpuLayers is { } layers ? layers.ToString() : "automatic, the tuner decides")}");
        Console.WriteLine($"  browser   : {(config.OpenBrowser ? "open" : "do not open")}");

        Console.WriteLine("[launcher] server");
        LlamaServer? server = LlamaServerLocator.Locate(options.LlamaServerPath ?? config.LlamaServerPath);
        Console.WriteLine(server is null
            ? "  llama-server.exe was not found"
            : $"  {server.Display} at {server.ExecutablePath}");

        Console.WriteLine("[launcher] file association");
        FileAssociationStatus status = FileAssociation.Query(FileAssociation.DefaultExtension);
        foreach (string line in status.Describe().Split(Environment.NewLine))
        {
            Console.WriteLine($"  {line}");
        }

        return 0;
    }

    /// <summary>Builds the association for the currently running launcher executable.</summary>
    private static FileAssociation Create()
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("the path of the running executable could not be determined");

        return new FileAssociation(FileAssociation.DefaultProgId, executable);
    }
}
