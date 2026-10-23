using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

public class LauncherArgsTests
{
    [Fact]
    public void ReadArgsFile_Missing_ReturnsEmpty()
    {
        var result = LauncherArgs.ReadArgsFile("does-not-exist.args");
        Assert.Empty(result);
    }

    [Fact]
    public void ReadArgsFile_ParsesCommentsAndFlags()
    {
        var path = Path.Combine(Path.GetTempPath(), "t.args");
        File.WriteAllLines(path, new[] { "# comment", "", "--host 127.0.0.1", "-c 4096" });
        try
        {
            var result = LauncherArgs.ReadArgsFile(path);
            Assert.Equal(new[] { "--host", "127.0.0.1", "-c", "4096" }, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetLastValue_LaterWins()
    {
        var args = new List<string> { "--host", "0.0.0.0", "--host", "127.0.0.1" };
        Assert.Equal("127.0.0.1", LauncherArgs.GetLastValue(args, "--host"));
    }

    [Fact]
    public void GetLastValue_TrailingFlagDoesNotEatNext()
    {
        var args = new List<string> { "--host", "--port", "8080" };
        Assert.Null(LauncherArgs.GetLastValue(args, "--host"));
        Assert.Equal("8080", LauncherArgs.GetLastValue(args, "--port"));
    }

    [Fact]
    public void FilterBrowserFlag_RemovesNoBrowser()
    {
        var args = new List<string> { "-c", "10", "--no-browser", "-t", "4" };
        var filtered = LauncherArgs.FilterBrowserFlag(args);
        Assert.DoesNotContain("--no-browser", filtered);
        Assert.Equal(4, filtered.Count);
    }

    [Fact]
    public void HasNoBrowser_DetectsFlag()
    {
        var args = new List<string> { "--no-browser" };
        Assert.True(LauncherArgs.HasNoBrowser(args));
    }

    [Fact]
    public void Merge_AppliesDefaults()
    {
        var merged = LauncherArgs.Merge(AppContext.BaseDirectory, "model.gguf", Array.Empty<string>());
        Assert.Contains("-ngl", merged);
    }
}