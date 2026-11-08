namespace GgufLauncher;

/// <summary>
/// Launcher diagnostics. A double-clicked exe has no visible console, so every warning goes
/// through one place; a file sink can be added here later without touching the call sites.
/// </summary>
public static class Log
{
    public static void Warn(string message)
    {
        Console.Error.WriteLine($"[launcher] {message}");
    }
}
