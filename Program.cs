using System.Runtime.Versioning;
using System.Text;
using GgufLauncher.Cli;
using GgufLauncher.Commands;

[assembly: SupportedOSPlatform("Windows")]

namespace GgufLauncher;

/// <summary>Entry point of the GGUF launcher.</summary>
public static class Program
{
    /// <summary>Runs the launcher and returns the process exit code.</summary>
    public static async Task<int> Main(string[] args)
    {
        UseUtf8Console();

        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[launcher] {ex.Message}");
            Console.Error.WriteLine();
            HelpText.Print();
            return 2;
        }

        using CancellationTokenSource shutdown = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            return options.Mode switch
            {
                CliMode.Help => Help(),
                CliMode.Register => AssociationCommand.Register(options),
                CliMode.Unregister => AssociationCommand.Unregister(options),
                CliMode.Status => AssociationCommand.Status(options),
                _ => await LaunchCommand.RunAsync(options, shutdown.Token).ConfigureAwait(false),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"[launcher] {ex.Message}");
            return 1;
        }
    }

    private static int Help()
    {
        HelpText.Print();
        return 0;
    }

    /// <summary>
    /// Switches the console to UTF-8. Without it the Windows console code page mangles the
    /// separator characters and any unicode the model metadata contains.
    /// </summary>
    private static void UseUtf8Console()
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            // A redirected console may refuse the change; the ASCII output stays readable.
        }
    }
}
