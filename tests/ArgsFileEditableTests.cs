using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

/// <summary>
/// The per-model .args file is created on the first launch and then belongs to the user:
/// later launches must never overwrite it, and its edits take effect.
/// </summary>
public class ArgsFileEditableTests
{
    [Fact]
    public void CreateEffectiveArgsFileIfMissing_CreatesNextToModel_WithoutModelPath()
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

            string? created = LauncherArgs.CreateEffectiveArgsFileIfMissing(modelPath, finalArgs);

            Assert.Equal(Path.Combine(modelDir, "llama-3.2-3b.args"), created);
            Assert.Equal(
                new[] { "-c 2048", "--jinja", "--temp -1" },
                NonCommentLines(created!));
        }
        finally
        {
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public void CreateEffectiveArgsFileIfMissing_DoesNotOverwriteExistingFile()
    {
        string modelDir = NewTempDir();
        try
        {
            string modelPath = Path.Combine(modelDir, "model.gguf");
            File.WriteAllText(modelPath, "");
            string argsFile = Path.Combine(modelDir, "model.args");
            string edited = "# my tuning\n-c 1234\n";
            File.WriteAllText(argsFile, edited);

            string? created = LauncherArgs.CreateEffectiveArgsFileIfMissing(
                modelPath,
                ["-m", modelPath, "-c", "98192"]);

            Assert.Null(created);
            Assert.Equal(edited, File.ReadAllText(argsFile));
        }
        finally
        {
            Directory.Delete(modelDir, true);
        }
    }

    [Fact]
    public void EditsInArgsFile_AreUsedOnNextLaunch()
    {
        string exeDir = NewTempDir();
        string modelDir = NewTempDir();
        try
        {
            string modelPath = Path.Combine(modelDir, "model.gguf");
            File.WriteAllText(modelPath, "");

            // First launch records the effective arguments.
            LauncherArgs.CreateEffectiveArgsFileIfMissing(
                modelPath,
                ["-m", modelPath, "-c", "98192"]);

            // The user edits the generated file.
            string argsFile = Path.Combine(modelDir, "model.args");
            string content = File.ReadAllText(argsFile).Replace("-c 98192", "-c 1234");
            File.WriteAllText(argsFile, content);

            // The next launch honors the edit and leaves the file alone.
            List<string> merged = LauncherArgs.Merge(exeDir, modelPath, []);
            Assert.Equal("1234", LauncherArgs.GetLastValue(merged, "-c"));

            string? created = LauncherArgs.CreateEffectiveArgsFileIfMissing(
                modelPath,
                ["-m", modelPath, "-c", "1234"]);
            Assert.Null(created);
            Assert.Equal(content, File.ReadAllText(argsFile));
        }
        finally
        {
            Directory.Delete(exeDir, true);
            Directory.Delete(modelDir, true);
        }
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
