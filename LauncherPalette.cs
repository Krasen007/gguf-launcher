using System.Drawing;

namespace GgufLauncher;

/// <summary>
/// Colors and fonts shared by the quick-pick window. Fonts are created on demand so the form
/// keeps owning and disposing them.
/// </summary>
internal static class LauncherPalette
{
    public static readonly Color HeaderBack = Color.FromArgb(247, 248, 250);
    public static readonly Color SidebarBack = Color.FromArgb(250, 250, 251);
    public static readonly Color MutedColor = Color.FromArgb(103, 110, 119);
    public static readonly Color TextColor = Color.FromArgb(28, 32, 36);
    public static readonly Color AccentColor = Color.FromArgb(31, 111, 235);
    public static readonly Color SuccessColor = Color.FromArgb(30, 126, 78);
    public static readonly Color DangerColor = Color.FromArgb(190, 45, 52);
    public static readonly Color BadgeBack = Color.FromArgb(232, 242, 255);
    public static readonly Color SelectedDetailColor = Color.FromArgb(220, 233, 250);
    public static readonly Color SelectedAccentColor = Color.FromArgb(138, 180, 248);
    public static readonly Color SelectedStateColor = Color.FromArgb(255, 226, 226);

    public static Font FormFont() => new("Segoe UI", 9F);
    public static Font BoldFont() => new("Segoe UI", 9.5F, FontStyle.Bold);
    public static Font SmallFont() => new("Segoe UI", 8.25F);
    public static Font MonoFont() => new("Consolas", 8.25F);
    public static Font TitleFont() => new("Segoe UI", 13.5F, FontStyle.Bold);
    public static Font ModelTitleFont() => new("Segoe UI", 14F, FontStyle.Bold);
    public static Font EditorFont() => new("Consolas", 9.5F);
}
