using System.Diagnostics;
using System.Net.Http;

namespace GgufLauncher.Launch;

/// <summary>
/// Runs <c>llama-server</c> as a child process, waits for it to become healthy,
/// and opens the built-in web UI.
/// </summary>
public sealed class ServerHost
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan HealthInterval = TimeSpan.FromMilliseconds(500);

    private readonly LaunchPlan _plan;
    private bool _becameHealthy;

    /// <summary>
    /// Creates a host for an already resolved plan, which carries the executable, the
    /// argument list and the endpoint the server is expected to listen on.
    /// </summary>
    /// <param name="plan">The resolved command line and endpoint to run.</param>
    public ServerHost(LaunchPlan plan)
    {
        _plan = plan;
    }

    /// <summary>Set to false to suppress the automatic browser launch.</summary>
    public bool OpenBrowserRequested { get; set; } = true;

    /// <summary>
    /// Starts the server and blocks until it exits. Returns the child's exit code.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = CreateStartInfo(),
            EnableRaisingEvents = true,
        };

        Console.CancelKeyPress += CreateCancelHandler(process);

        if (TryStartProcess(process) is false)
        {
            return 1;
        }

        using CancellationTokenSource healthCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task health = WaitForHealthAsync(healthCts.Token);
        Task exited = process.WaitForExitAsync();

        Task finished = await Task.WhenAny(health, exited).ConfigureAwait(false);

        if (finished == exited)
        {
            healthCts.Cancel();
            WriteServerStopped(process.ExitCode);
            ReportEarlyExit();
            return process.ExitCode;
        }

        await exited.ConfigureAwait(false);
        healthCts.Cancel();
        WriteServerStopped(process.ExitCode);
        ReportCrashAfterReady();
        return process.ExitCode;
    }

    /// <summary>
    /// Builds the command line for the child process. Output is deliberately left
    /// unredirected so the server's own logs stream live and keep the colours
    /// llama.cpp intends.
    /// </summary>
    private ProcessStartInfo CreateStartInfo()
    {
        ProcessStartInfo startInfo = new(_plan.ExecutablePath)
        {
            WorkingDirectory = _plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        foreach (string argument in _plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>
    /// Builds the Ctrl+C handler. It kills the whole process tree because llama.cpp
    /// can leave helper processes behind that would otherwise keep the port busy.
    /// </summary>
    private static ConsoleCancelEventHandler CreateCancelHandler(Process process)
    {
        return (object? sender, ConsoleCancelEventArgs e) =>
        {
            e.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("[launcher] stopping...");

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                // The server already exited.
            }
        };
    }

    /// <summary>
    /// Starts the child process and reports why it failed, because a double-click
    /// gives the user nothing but a console that closed again.
    /// </summary>
    private static bool TryStartProcess(Process process)
    {
        try
        {
            if (process.Start())
            {
                return true;
            }

            Console.Error.WriteLine("[launcher] failed to start llama-server.");
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"[launcher] failed to start llama-server: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Announces that the server is gone, using the same blank-line rhythm as the
    /// readiness banner so the console stays readable. The exit code is included
    /// because a backend crash shows up as a Windows access-violation code that says
    /// nothing on its own.
    /// </summary>
    private static void WriteServerStopped(int exitCode)
    {
        Console.WriteLine();
        Console.WriteLine($"[launcher] the server exited (exit code {exitCode}).");
    }

    /// <summary>
    /// Explains a server that was ready and then died. That is what a backend abort
    /// looks like from the outside: the launcher is fine, llama.cpp crashed while it
    /// was serving a request.
    /// </summary>
    private void ReportCrashAfterReady()
    {
        if (_becameHealthy is false)
        {
            return;
        }

        Console.WriteLine("[launcher] the server was ready and then died, so the crash is inside llama.cpp, not the launcher.");
        Console.WriteLine("[launcher] the reason is in the server output above, for example a GGML_ASSERT line.");
    }

    /// <summary>
    /// Explains the most common reasons for a server that dies before it serves anything,
    /// because a double-click gives the user no other clue.
    /// </summary>
    private void ReportEarlyExit()
    {
        if (_becameHealthy)
        {
            return;
        }

        Console.WriteLine("[launcher] the server stopped before it became ready.");
        Console.WriteLine("[launcher] usual causes: the port was taken by another program,");
        Console.WriteLine("[launcher] the graphics card ran out of memory, the model file could not be read,");
        Console.WriteLine("[launcher] or a backend crashed, which shows up as a GGML_ASSERT line above.");
    }

    private async Task WaitForHealthAsync(CancellationToken cancellationToken)
    {
        using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };
        string healthUrl = $"http://{LoopbackHost(_plan.Host)}:{_plan.Port}/health";
        DateTime deadline = DateTime.UtcNow + HealthTimeout;

        while (DateTime.UtcNow < deadline && cancellationToken.IsCancellationRequested is false)
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(healthUrl, cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    OnHealthy();
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Not listening yet.
            }

            try
            {
                await Task.Delay(HealthInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private void OnHealthy()
    {
        _becameHealthy = true;

        Console.WriteLine();
        Console.WriteLine($"[launcher] server is ready at {_plan.Url}");
        Console.WriteLine("[launcher] press Ctrl+C to stop");

        if (OpenBrowserRequested)
        {
            OpenBrowser(_plan.Url);
        }
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"[launcher] could not open a browser: {ex.Message}");
        }
    }

    private static string LoopbackHost(string host)
    {
        return host switch
        {
            "0.0.0.0" or "::" or "" => "127.0.0.1",
            _ => host,
        };
    }
}
