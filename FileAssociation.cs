using Microsoft.Win32;

namespace GgufLauncher;

/// <summary>
/// Registers .gguf file association (per-user, no admin required).
/// </summary>
public static class FileAssociation
{
    private const string ProgId = "GgufLauncher";
    private const string FileType = "GGUF model";

    /// <summary>
    /// Registers .gguf files to open with this launcher.
    /// </summary>
    public static void Register()
    {
        string exePath = Environment.ProcessPath ?? "";
        string command = $"\"{exePath}\" \"%1\"";

        try
        {
            // Register .gguf extension
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\.gguf"))
            {
                key?.SetValue("", ProgId);
            }

            // Register ProgId
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                key?.SetValue("", FileType);
            }

            // Set icon (using exe icon)
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
            {
                key?.SetValue("", $"\"{exePath}\",0");
            }

            // Set open command
            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
            {
                key?.SetValue("", command);
            }

            // Notify shell of association change
            NotifyShell();

            Console.WriteLine($"Registered .gguf -> {exePath}");
            Console.WriteLine("On Windows 11 you may still need to pick it once in Open with / Default apps.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[launcher] registry write failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Unregisters .gguf file association.
    /// </summary>
    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.gguf", true);
            if (key?.GetValue("")?.ToString() == ProgId)
            {
                Registry.CurrentUser.DeleteSubKey(@"Software\Classes\.gguf", false);
            }

            NotifyShell();
            Console.WriteLine("Unregistered .gguf file association.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[launcher] registry remove failed: {ex.Message}");
        }
    }

    private static void NotifyShell()
    {
        // SHCNE_ASSOCCHANGED (0x08000000), SHCNF_IDLIST (0)
        try
        {
            ShellNotify(0x08000000, 0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[launcher] shell notify failed: {ex.Message}");
        }
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void ShellNotify(int dwMessage, IntPtr lpVoid);
}