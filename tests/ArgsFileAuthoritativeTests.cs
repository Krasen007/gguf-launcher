using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

public class ArgsFileAuthoritativeTests
{
    [Fact]
    public void PerModelArgsPath_IsNextToModel()
    {
        string modelPath = Path.Combine("F:", "models", "model.gguf");

        Assert.Equal(
            Path.Combine("F:", "models", "model.args"),
            LauncherArgs.PerModelArgsPath(modelPath));
    }

    [Fact]
    public void Merge_WithPerModelArgs_SkipsBakedInDefaults()
    {
        string exeDir = NewTempDir();
        string modelDir = NewTempDir();
        try
        {
            string modelPath = Path.Combine(modelDir, "model.gguf");
            File.WriteAllText(modelPath, "");
            File.WriteAllText(Path.Combine(modelDir, "model.args"), "-c 1234\n");

            List<string> merged = LauncherArgs.Merge(exeDir, modelPath, []);

            Assert.Equal("1234", LauncherArgs.GetLastValue(merged, "-c"));
            Assert.DoesNotContain("-ngl", merged);
            Assert.DoesNotContain("-b", merged);
            Assert.DoesNotContain("--jinja", merged);
        }
        finally
        {
            Directory.Delete(exeDir, true);
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public void Merge_WithPerModelArgs_AppliesCommandLineOnTop()
    {
        string exeDir = NewTempDir();
        string modelDir = NewTempDir();
        try
        {
            string modelPath = Path.Combine(modelDir, "model.gguf");
            File.WriteAllText(modelPath, "");
            File.WriteAllText(Path.Combine(modelDir, "model.args"), "--temp 0.1\n");

            List<string> merged = LauncherArgs.Merge(exeDir, modelPath, ["--temp", "0.9"]);

            Assert.Equal("0.9", LauncherArgs.GetLastValue(merged, "--temp"));
        }
        finally
        {
            Directory.Delete(exeDir, true);
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public void Merge_WithPerModelArgs_StillAppliesGlobalArgsBeforeIt()
    {
        string exeDir = NewTempDir();
        string modelDir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(exeDir, "gguf-launcher.args"), "--host 127.0.0.1\n-c 4096\n");
            string modelPath = Path.Combine(modelDir, "model.gguf");
            File.WriteAllText(modelPath, "");
            File.WriteAllText(Path.Combine(modelDir, "model.args"), "-c 1234\n");

            List<string> merged = LauncherArgs.Merge(exeDir, modelPath, []);

            Assert.Equal("1234", LauncherArgs.GetLastValue(merged, "-c"));
            Assert.Equal("127.0.0.1", LauncherArgs.GetLastValue(merged, "--host"));
        }
        finally
        {
            Directory.Delete(exeDir, true);
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public void Merge_WithoutPerModelArgs_AddsBakedInDefaults()
    {
        string exeDir = NewTempDir();
        string modelDir = NewTempDir();
        try
        {
            string modelPath = Path.Combine(modelDir, "model.gguf");
            File.WriteAllText(modelPath, "");

            List<string> merged = LauncherArgs.Merge(exeDir, modelPath, []);

            Assert.Equal("99", LauncherArgs.GetLastValue(merged, "-ngl"));
            Assert.Contains("--jinja", merged);
        }
        finally
        {
            Directory.Delete(exeDir, true);
            Directory.Delete(modelDir, true);
        }
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gguf-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
