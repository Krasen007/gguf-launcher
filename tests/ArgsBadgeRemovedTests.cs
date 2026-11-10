using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

/// <summary>
/// Covers the removal of the blue args badge rectangle from the quick-pick window.
/// </summary>
public class ArgsBadgeRemovedTests
{
    [Fact]
    public void LauncherForm_HasNoArgsBadgeField()
    {
        var field = typeof(LauncherForm).GetField("_argsBadge",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.Null(field);
    }
}
