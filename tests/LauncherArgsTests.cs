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
    public void Merge_LaterSourcesWin()
    {
        // Global args dir
        var exeDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(exeDir);
        File.WriteAllText(Path.Combine(exeDir, "gguf-launcher.args"), "--host 0.0.0.0 -c 4096");

        // Model dir with per-model args
        var modelDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(modelDir);
        var modelPath = Path.Combine(modelDir, "model.gguf");
        File.WriteAllText(modelPath, "");
        File.WriteAllText(Path.Combine(modelDir, "model.args"), "--host 127.0.0.1 -c 8192");

        try
        {
            // CLI overrides both global and per-model
            var merged = LauncherArgs.Merge(exeDir, modelPath, new[] { "--host", "127.0.0.1", "-c", "2048" });
            Assert.Equal("2048", LauncherArgs.GetLastValue(merged, "-c"));
            Assert.Equal("127.0.0.1", LauncherArgs.GetLastValue(merged, "--host"));

            // Without CLI, per-model overrides global
            merged = LauncherArgs.Merge(exeDir, modelPath, Array.Empty<string>());
            Assert.Equal("8192", LauncherArgs.GetLastValue(merged, "-c"));
            Assert.Equal("127.0.0.1", LauncherArgs.GetLastValue(merged, "--host"));

            // Without per-model args, global applies (built-in defaults are overridden)
            var otherModel = Path.Combine(modelDir, "other.gguf");
            merged = LauncherArgs.Merge(exeDir, otherModel, Array.Empty<string>());
            Assert.Equal("4096", LauncherArgs.GetLastValue(merged, "-c"));
            Assert.Equal("0.0.0.0", LauncherArgs.GetLastValue(merged, "--host"));

            // With no args files at all, built-in defaults are present
            merged = LauncherArgs.Merge(exeDir, Path.Combine(Path.GetTempPath(), "nonexistent.gguf"), Array.Empty<string>());
            Assert.Contains("-ngl", merged);
            Assert.Equal("99", LauncherArgs.GetLastValue(merged, "-ngl"));
        }
        finally
        {
            Directory.Delete(exeDir, true);
            Directory.Delete(modelDir, true);
        }
    }

    /// <summary>
    /// --no-browser is a launcher-only flag. Merge must keep it so Program can detect it;
    /// it is stripped from the args actually forwarded to llama-server elsewhere.
    /// </summary>
    [Fact]
    public void Merge_RetainsNoBrowserForLauncherDetection()
    {
        var merged = LauncherArgs.Merge(AppContext.BaseDirectory, "model.gguf", new[] { "--no-browser" });
        Assert.Contains("--no-browser", merged);
    }

    [Fact]
    public void Normalize_KeepsLastValueOfRepeatedFlag()
    {
        List<string> normalized = LauncherArgs.Normalize(["-c", "98192", "--jinja", "-c", "4096"]);

        Assert.Equal(new[] { "--jinja", "-c", "4096" }, normalized);
    }

    [Fact]
    public void Normalize_KeepsAllOccurrencesOfRepeatableFlag()
    {
        List<string> normalized = LauncherArgs.Normalize(["--lora", "a.gguf", "--lora", "b.gguf"]);

        Assert.Equal(new[] { "--lora", "a.gguf", "--lora", "b.gguf" }, normalized);
    }

    [Fact]
    public void Normalize_DoesNotTreatNegativeNumbersAsFlags()
    {
        List<string> normalized = LauncherArgs.Normalize(["--top-k", "-1", "--temp", "-1"]);

        Assert.Equal(new[] { "--top-k", "-1", "--temp", "-1" }, normalized);
    }
}