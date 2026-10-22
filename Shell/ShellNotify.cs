using System.Runtime.InteropServices;

namespace GgufLauncher.Shell;

/// <summary>
/// Thin wrapper around the <c>shell32.dll</c> entry point that tells Explorer's shell
/// "something in the file-association world changed".
/// </summary>
/// <remarks>
/// Explorer caches icons and the "Open with" menu. Without a notification the new
/// association only shows up after the shell is restarted, which is unacceptable for a
/// launcher the user just registered.
/// </remarks>
public static class ShellNotify
{
    /// <summary>Event code for "a file association has changed".</summary>
    private const uint ShcneAssocChanged = 0x08000000;

    /// <summary>Flag: the item arguments are PIDLs (none are passed here).</summary>
    private const uint ShcnfIdList = 0x0000;

    /// <summary>Flag: flush the notification to all shell windows immediately.</summary>
    private const uint ShcnfFlush = 0x1000;

    /// <summary>
    /// Notifies the shell that file associations changed so the icon cache and the
    /// "Open with" list are refreshed without restarting Explorer.
    /// </summary>
    /// <remarks>
    /// The call is best effort: on a system where <c>shell32.dll</c> cannot be loaded the
    /// registration itself is still valid, so the failure is swallowed instead of being
    /// propagated to the caller.
    /// </remarks>
    public static void NotifyAssociationsChanged()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList | ShcnfFlush, IntPtr.Zero, IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            // The shell is not available (non-Windows host); nothing to refresh.
        }
        catch (EntryPointNotFoundException)
        {
            // An older shell32.dll without the entry point; the registry state is still correct.
        }
    }

    /// <summary>
    /// Shell API that broadcasts a change to the file system or the association tables.
    /// </summary>
    /// <param name="wEventId">The change that occurred, for example <c>SHCNE_ASSOCCHANGED</c>.</param>
    /// <param name="uFlags">Options that refine the delivery of the notification.</param>
    /// <param name="dwItem1">First item affected by the change; unused for association changes.</param>
    /// <param name="dwItem2">Second item affected by the change; unused for association changes.</param>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
