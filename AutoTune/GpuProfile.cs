using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace GgufLauncher.AutoTune;

/// <summary>Detected GPU and its VRAM budget.</summary>
/// <param name="Name">The product name of the GPU, for example <c>NVIDIA GeForce RTX 3070</c>.</param>
/// <param name="TotalMiB">The installed VRAM in MiB.</param>
/// <param name="FreeMiB">The VRAM that is free right now, in MiB.</param>
public sealed record GpuProfile(string Name, int TotalMiB, int FreeMiB)
{
    /// <summary>How long <c>nvidia-smi</c> may run before the detection is given up on.</summary>
    private const int DetectionTimeoutMilliseconds = 5000;

    /// <summary>VRAM usable for weights plus KV cache plus compute buffers.</summary>
    /// <param name="marginMiB">The VRAM that is kept free for the driver and the desktop.</param>
    /// <returns>The budget in MiB, never below zero.</returns>
    public int BudgetMiB(int marginMiB) => Math.Max(0, FreeMiB - marginMiB);

    /// <summary>Detects the first NVIDIA GPU through nvidia-smi. Returns null when there is none.</summary>
    /// <remarks>
    /// The detection never throws. A missing <c>nvidia-smi</c>, a timeout, a message on
    /// standard error or output that cannot be parsed all end in <see langword="null"/>, so the
    /// caller can fall back to the CPU.
    /// </remarks>
    public static GpuProfile? Detect()
    {
        try
        {
            ProcessStartInfo startInfo = new("nvidia-smi")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--query-gpu=name,memory.total,memory.free");
            startInfo.ArgumentList.Add("--format=csv,noheader,nounits");

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            // Both pipes are drained while the process runs, so a chatty driver cannot
            // fill the buffer and block the child.
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(DetectionTimeoutMilliseconds))
            {
                KillQuietly(process);
                return null;
            }

            string standardOutput = output.GetAwaiter().GetResult();
            string standardError = error.GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(standardError))
            {
                // nvidia-smi complains when it cannot talk to the driver.
                return null;
            }

            return Parse(standardOutput);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // nvidia-smi is not installed or cannot be started.
            return null;
        }
    }

    /// <summary>
    /// Reads the first line of the <c>nvidia-smi</c> report. The line looks like
    /// <c>name, total, free</c>, and the name itself may contain commas, so the two numbers
    /// are taken from the right.
    /// </summary>
    /// <param name="output">The raw standard output of <c>nvidia-smi</c>.</param>
    /// <returns>The detected GPU, or <see langword="null"/> when the output cannot be parsed.</returns>
    private static GpuProfile? Parse(string output)
    {
        foreach (string rawLine in output.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] columns = line.Split(',');
            if (columns.Length < 3)
            {
                return null;
            }

            string name = string.Join(",", columns[..^2]).Trim();
            string totalText = columns[^2].Trim();
            string freeText = columns[^1].Trim();

            if (name.Length == 0)
            {
                return null;
            }

            if (!int.TryParse(totalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total))
            {
                return null;
            }

            if (!int.TryParse(freeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int free))
            {
                return null;
            }

            return new GpuProfile(name, total, free);
        }

        return null;
    }

    /// <summary>Stops a <c>nvidia-smi</c> that took too long, ignoring a failure to do so.</summary>
    /// <param name="process">The process to stop.</param>
    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            // The process already ended, which is just as good.
        }
    }
}
