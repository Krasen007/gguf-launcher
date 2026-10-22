using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace GgufLauncher.Launch;

/// <summary>Finds a usable TCP port for the server.</summary>
public static class PortAllocator
{
    /// <summary>True when a socket can be bound on the given host and port.</summary>
    public static bool IsAvailable(string host, int port)
    {
        TcpListener? listener = null;
        try
        {
            IPAddress address = host switch
            {
                "0.0.0.0" => IPAddress.Any,
                "::" => IPAddress.IPv6Any,
                _ => IPAddress.TryParse(host, out IPAddress? parsed) ? parsed : IPAddress.Loopback,
            };

            listener = new TcpListener(address, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }

    /// <summary>Describes the current owner of a port, or null when the port is free.</summary>
    public static PortListener? Query(int port)
    {
        foreach ((int listeningPort, int pid, string? name) in Listeners())
        {
            if (listeningPort == port)
            {
                return new PortListener(port, pid, name);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the preferred port when it is free, otherwise the next free port
    /// within the search range. Returns -1 when nothing in the range is free.
    /// </summary>
    public static int FindFree(string host, int preferred, int searchRange = 20)
    {
        for (int offset = 0; offset <= searchRange; offset++)
        {
            int candidate = preferred + offset;
            if (candidate is > 0 and <= 65535 && IsAvailable(host, candidate))
            {
                return candidate;
            }
        }

        return -1;
    }

    /// <summary>Enumerates listening TCP endpoints as (port, pid, process name) triples.</summary>
    private static List<(int Port, int Pid, string? Name)> Listeners()
    {
        List<(int, int, string?)> result = [];
        try
        {
            string? output = ReadNetstatOutput();
            if (output is null)
            {
                return result;
            }

            foreach (string line in output.Split('\n'))
            {
                if (TryParseListenerLine(line, out int port, out int pid))
                {
                    result.Add((port, pid, ResolveName(pid)));
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return result;
        }

        return result;
    }

    /// <summary>
    /// Runs <c>netstat -ano -p TCP</c> and returns its raw output, because Windows only
    /// exposes the owning process id of a busy port through that command.
    /// Returns null when the command cannot be started or produces no output.
    /// </summary>
    private static string? ReadNetstatOutput()
    {
        ProcessStartInfo startInfo = new("netstat")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-ano");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add("TCP");

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return output;
    }

    /// <summary>
    /// Reads one netstat row and reports the port and pid it describes. Rows that are not
    /// listening entries, or whose port or pid is not a number, are skipped so that
    /// headers and non-TCP traffic never turn into a bogus owner.
    /// </summary>
    private static bool TryParseListenerLine(string line, out int port, out int pid)
    {
        port = 0;
        pid = 0;

        string[] columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length < 5 || !columns[^2].Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(ParseLocalPort(columns[1]), out port))
        {
            return false;
        }

        if (!int.TryParse(columns[^1], out pid))
        {
            return false;
        }

        return true;
    }

    /// <summary>Extracts the port from a netstat local address such as <c>127.0.0.1:8099</c> or <c>[::]:8099</c>.</summary>
    private static string ParseLocalPort(string localAddress)
    {
        int colon = localAddress.LastIndexOf(':');
        return colon >= 0 ? localAddress[(colon + 1)..] : localAddress;
    }

    private static string? ResolveName(int pid)
    {
        try
        {
            using Process? process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
