using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

/// <summary>
/// The quick-pick window can load a model that is not in the recent list yet; on that
/// first run the editor shows the generated default arguments.
/// </summary>
public class LoadModelButtonTests
{
    [Fact]
    public void LoadModel_RecordsAModelThatWasNotInTheRecentList()
    {
        using Fixture fixture = Fixture.Create();

        LauncherModelEntry entry = fixture.UiModel.LoadModel(fixture.ModelPath);

        Assert.Equal(fixture.ModelPath, entry.Path);
        Assert.Equal("model.gguf", entry.Name);
        Assert.True(entry.Exists);
        Assert.False(entry.HasArgs);
        Assert.True(fixture.Store.Contains(fixture.ModelPath));
        Assert.Single(fixture.UiModel.LoadModels(), m => m.Path == fixture.ModelPath);
    }

    [Fact]
    public void LoadModel_FirstRunShowsTheBuiltInDefaultsWithoutWritingTheArgsFile()
    {
        using Fixture fixture = Fixture.Create();
        fixture.UiModel.LoadModel(fixture.ModelPath);

        LauncherArgsContent args = fixture.UiModel.LoadArgs(fixture.ModelPath);

        Assert.False(args.Exists);
        Assert.False(File.Exists(args.ArgsPath));
        Assert.Contains("-c 98192", args.Content);
        Assert.Contains("-b 512", args.Content);
        Assert.Contains("--host 0.0.0.0", args.Content);
        Assert.Contains("--port 8080", args.Content);
    }

    [Fact]
    public void LoadModel_PutsTheChosenModelAtTheFrontOfTheRecentList()
    {
        using Fixture fixture = Fixture.Create();
        string other = Path.Combine(fixture.ModelDir, "other.gguf");
        File.WriteAllText(other, "");
        fixture.Store.Record(other);

        fixture.UiModel.LoadModel(fixture.ModelPath);

        Assert.Equal(fixture.ModelPath, fixture.UiModel.LoadModels()[0].Path);
        Assert.Equal(2, fixture.UiModel.LoadModels().Count);
    }

    [Fact]
    public void LoadModel_RejectsAMissingFileWithoutRecordingIt()
    {
        using Fixture fixture = Fixture.Create();
        string missing = Path.Combine(fixture.ModelDir, "missing.gguf");

        Assert.Throws<InvalidOperationException>(() => fixture.UiModel.LoadModel(missing));
        Assert.Empty(fixture.Store.Load());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempRoot _root;

        private Fixture(
            TempRoot root,
            string exeDir,
            string modelDir,
            string modelPath,
            RecentModelStore store)
        {
            _root = root;
            ExeDir = exeDir;
            ModelDir = modelDir;
            ModelPath = modelPath;
            Store = store;
        }

        public string ExeDir { get; }
        public string ModelDir { get; }
        public string ModelPath { get; }
        public RecentModelStore Store { get; }
        public LauncherUiModel UiModel => new(ExeDir, Store, _ => { });

        public static Fixture Create()
        {
            TempRoot root = new();
            string exeDir = Path.Combine(root.Path, "exe");
            string modelDir = Path.Combine(root.Path, "models");
            Directory.CreateDirectory(exeDir);
            Directory.CreateDirectory(modelDir);

            string modelPath = Path.Combine(modelDir, "model.gguf");
            File.WriteAllText(modelPath, "");

            RecentModelStore store = new(Path.Combine(root.Path, "state", "recent-models.json"));
            return new Fixture(root, exeDir, modelDir, modelPath, store);
        }

        public void Dispose()
        {
            _root.Dispose();
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "gguf-launcher-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
