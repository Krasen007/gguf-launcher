using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

public class ReviewFixesTests
{
    [Fact]
    public void RenderArgsFile_QuotesWhitespaceValuesAndReadRestoresThem()
    {
        List<string> args =
        [
            "--alias", "model with spaces",
            "--path", @"C:\models\folder with spaces\model.gguf",
            "--quoted", "value with \"quotes\"",
            "-c", "4096",
        ];

        string rendered = LauncherArgs.RenderArgsFile(args);
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".args");
        try
        {
            Assert.Contains("--alias \"model with spaces\"", rendered);
            Assert.Contains("-c 4096", rendered);
            File.WriteAllText(path, rendered);

            Assert.Equal(args, LauncherArgs.ReadArgsFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RecentModelStore_LoadSkipsNullEntriesAndRetainsPathValidationAndLimit()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            string validEntries = string.Join(
                ",",
                Enumerable.Range(0, RecentModelStore.MaxCount + 1)
                    .Select(index => $$"""{"Path":"model-{{index}}.gguf","LastRunUtc":""}"""));
            File.WriteAllText(path, "[null," + validEntries + ",null]");

            IReadOnlyList<RecentModel> models = new RecentModelStore(path).Load();

            Assert.Equal(RecentModelStore.MaxCount, models.Count);
            Assert.All(models, model => Assert.False(string.IsNullOrWhiteSpace(model.Path)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
