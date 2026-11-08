using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

public class ReviewFixesOrderingTests
{
    [Fact]
    public void Normalize_GroupsRepeatedLayerRangesAndPreservesTheirValuesAcrossSources()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string exeDir = Path.Combine(root, "exe");
        string modelDir = Path.Combine(root, "models");
        Directory.CreateDirectory(exeDir);
        Directory.CreateDirectory(modelDir);
        string modelPath = Path.Combine(modelDir, "model.gguf");
        File.WriteAllText(modelPath, "");
        File.WriteAllText(
            Path.Combine(exeDir, "gguf-launcher.args"),
            "-ot global=CPU --override-tensor-draft global_draft=CPU --control-vector-layer-range 1 4");
        File.WriteAllText(
            Path.Combine(modelDir, "model.args"),
            "--override-tensor model=GPU --override-tensor-draft model_draft=GPU --control-vector-layer-range 5 8");

        try
        {
            List<string> normalized = LauncherArgs.Normalize(LauncherArgs.Merge(
                exeDir,
                modelPath,
                [
                    "-ot", "cli=CUDA",
                    "--override-tensor-draft", "cli_draft=CUDA",
                    "--control-vector-layer-range", "9", "12",
                ]));

            Assert.Equal(2, normalized.Count(arg => arg == "-ot"));
            Assert.Equal(1, normalized.Count(arg => arg == "--override-tensor"));
            Assert.Equal(3, normalized.Count(arg => arg == "--override-tensor-draft"));
            AssertContainsSequence(
                normalized,
                [
                    "--control-vector-layer-range", "1", "4",
                    "--control-vector-layer-range", "5", "8",
                    "--control-vector-layer-range", "9", "12",
                ]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertContainsSequence(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        for (int start = 0; start <= actual.Count - expected.Count; start++)
        {
            if (actual.Skip(start).Take(expected.Count).SequenceEqual(expected))
            {
                return;
            }
        }

        Assert.Fail($"Expected sequence [{string.Join(", ", expected)}] was not found.");
    }
}
