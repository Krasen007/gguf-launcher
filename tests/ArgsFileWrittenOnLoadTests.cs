using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

/// <summary>
/// Each time the app loads a model it must create the .args file next to the model,
/// in the same folder, with the arguments that were used.
/// </summary>
public class ArgsFileWrittenOnLoadTests
{
    [Fact]
    public void WriteEffectiveArgsFile_WritesNextToModel_WithoutModelPath()
    {
        string modelDir = NewTempDir();
        try
        {
            string modelPath = Path.Combine(modelDir, "llama-3.2-3b.gguf");
            File.WriteAllText(modelPath, "");

            List<string> finalArgs =
            [
                "-m", modelPath,
                "-c", "2048",
                "--jinja",
                "--temp", "-1",
            ];

            string written = LauncherArgs.WriteEffectiveArgsFile(modelPath, finalArgs);

            Assert.Equal(Path.Combine(modelDir, "llama-3.2-3b.args"), written);
            Assert.True(File.Exists(written));
            Assert.Equal(
                new[] { "-c 2048", "--jinja", "--temp -1" },
                NonCommentLines(written));
        }
        finally
        {
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public void WriteEffectiveArgsFile_IsStableWhenModelIsLaunchedAgain()
    {
        string exeDir = NewTempDir();
        string modelDir = NewTempDir();
        try
        {
            string modelPath = Path.Combine(modelDir, "model.gguf");
            File.WriteAllText(modelPath, "");
            string[] extra = ["-c", "4096"];

            string first = Path.Combine(modelDir, "model.args");
            LauncherArgs.WriteEffectiveArgsFile(modelPath, LaunchArgs(exeDir, modelPath, extra));
            string firstContent = File.ReadAllText(first);

            // The generated file is also an input on the next launch; the recorded
            // arguments must not grow or change because of that.
            LauncherArgs.WriteEffectiveArgsFile(modelPath, LaunchArgs(exeDir, modelPath, extra));
            string secondContent = File.ReadAllText(first);

            Assert.Equal(firstContent, secondContent);
            Assert.Equal(1, NonCommentLines(first).Count(l => l == "-c 4096"));
            Assert.DoesNotContain("-c 98192", NonCommentLines(first));
        }
        finally
        {
            Directory.Delete(exeDir, true);
            Directory.Delete(modelDir, true);
        }
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

    private static List<string> LaunchArgs(string exeDir, string modelPath, IReadOnlyList<string> extra)
    {
        List<string> finalArgs = ["-m", modelPath];
        finalArgs.AddRange(LauncherArgs.Normalize(LauncherArgs.Merge(exeDir, modelPath, extra)));
        return finalArgs;
    }

    private static List<string> NonCommentLines(string path)
    {
        return File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "gguf-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
