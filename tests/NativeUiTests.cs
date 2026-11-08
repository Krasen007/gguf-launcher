using GgufLauncher;
using Xunit;

namespace GgufLauncher.Tests;

/// <summary>
/// The native quick-pick window replaces the browser UI while keeping the same headless
/// recents, preview, save, launch, and routing behavior.
/// </summary>
public class NativeUiTests
{
    [Fact]
    public void GetMode_NoArguments_OpensUi()
    {
        Assert.Equal(AppMode.Ui, Program.GetMode([]));
    }

    [Fact]
    public void GetMode_Help_DoesNotOpenUi()
    {
        Assert.Equal(AppMode.Help, Program.GetMode(["-h"]));
        Assert.Equal(AppMode.Help, Program.GetMode(["--help"]));
    }

    [Fact]
    public void GetMode_ModelPath_LaunchesHeadless()
    {
        Assert.Equal(AppMode.Launch, Program.GetMode([@"C:\models\a.gguf"]));
        Assert.Equal(AppMode.Launch, Program.GetMode(["-m", @"C:\models\a.gguf"]));
    }

    [Fact]
    public void RecentStore_KeepsNewestFirstDedupedAndCapped()
    {
        using TempRoot root = new();
        string storePath = Path.Combine(root.Path, "state", "recent-models.json");
        RecentModelStore store = new(storePath);

        for (int i = 0; i < 12; i++)
        {
            store.Record(Path.Combine(root.Path, $"model-{i}.gguf"), DateTimeOffset.UnixEpoch.AddMinutes(i));
        }

        IReadOnlyList<RecentModel> models = store.Load();
        Assert.Equal(RecentModelStore.MaxCount, models.Count);
        Assert.Equal(Path.GetFullPath(Path.Combine(root.Path, "model-11.gguf")), models[0].Path);
        Assert.DoesNotContain(models, m => m.Path.EndsWith("model-0.gguf", StringComparison.OrdinalIgnoreCase));

        store.Record(Path.Combine(root.Path, "model-5.gguf"), DateTimeOffset.UnixEpoch.AddMinutes(99));

        models = new RecentModelStore(storePath).Load();
        Assert.Equal(RecentModelStore.MaxCount, models.Count);
        Assert.Equal(Path.GetFullPath(Path.Combine(root.Path, "model-5.gguf")), models[0].Path);
        Assert.Single(models, m => m.Path.EndsWith("model-5.gguf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RecentStore_CorruptOrMissingFile_LoadsEmpty()
    {
        using TempRoot root = new();
        string storePath = Path.Combine(root.Path, "recent-models.json");

        Assert.Empty(new RecentModelStore(storePath).Load());

        File.WriteAllText(storePath, "{ this is not json");
        Assert.Empty(new RecentModelStore(storePath).Load());
    }

    [Fact]
    public void UiModel_LoadModels_ReturnsRecentEntriesWithTheirState()
    {
        using Fixture fixture = Fixture.Create();
        File.WriteAllText(LauncherArgs.PerModelArgsPath(fixture.ModelPath), "-c 4096\n");

        string missingPath = Path.Combine(fixture.ModelDir, "gone.gguf");
        fixture.Store.Record(missingPath, DateTimeOffset.UtcNow.AddMinutes(1));

        IReadOnlyList<LauncherModelEntry> models = fixture.UiModel.LoadModels();

        Assert.Equal(2, models.Count);
        Assert.Equal(Path.GetFullPath(missingPath), models[0].Path);
        Assert.False(models[0].Exists);
        Assert.False(models[0].HasArgs);

        LauncherModelEntry entry = models[1];
        Assert.Equal(fixture.ModelPath, entry.Path);
        Assert.Equal("model.gguf", entry.Name);
        Assert.Equal(fixture.ModelDir, entry.Directory);
        Assert.True(entry.Exists);
        Assert.True(entry.HasArgs);
        Assert.NotNull(entry.LastRunUtc);
    }

    [Fact]
    public void UiModel_LoadArgs_ReturnsGeneratedPreviewWhenFileMissing()
    {
        using Fixture fixture = Fixture.Create();

        LauncherArgsContent args = fixture.UiModel.LoadArgs(fixture.ModelPath);

        Assert.False(args.Exists);
        Assert.Equal(LauncherArgs.PerModelArgsPath(fixture.ModelPath), args.ArgsPath);
        Assert.Equal(
            LauncherArgs.RenderArgsFile(LaunchPlanner.Create(fixture.ExeDir, fixture.ModelPath, [], null).FinalArgs),
            args.Content);
        Assert.Contains("-c 98192", args.Content);
        Assert.Contains("--host 0.0.0.0", args.Content);
        Assert.Contains("--port 8080", args.Content);
        Assert.DoesNotContain("-m ", args.Content);
    }

    [Fact]
    public void UiModel_LoadArgs_PreviewMatchesTheFileTheLauncherWouldCreate()
    {
        using Fixture fixture = Fixture.Create();

        LauncherArgsContent preview = fixture.UiModel.LoadArgs(fixture.ModelPath);
        string? created = LauncherArgs.CreateEffectiveArgsFileIfMissing(
            fixture.ModelPath,
            LaunchPlanner.Create(fixture.ExeDir, fixture.ModelPath, [], null).FinalArgs);

        Assert.NotNull(created);
        Assert.Equal(preview.Content, File.ReadAllText(created!));
    }

    [Fact]
    public void UiModel_LoadArgs_ReturnsExistingFileContent()
    {
        using Fixture fixture = Fixture.Create();
        string argsPath = LauncherArgs.PerModelArgsPath(fixture.ModelPath);
        string edited = "# tuned\n-c 4096\n";
        File.WriteAllText(argsPath, edited);

        LauncherArgsContent args = fixture.UiModel.LoadArgs(fixture.ModelPath);

        Assert.True(args.Exists);
        Assert.Equal(argsPath, args.ArgsPath);
        Assert.Equal(edited, args.Content);
    }

    [Fact]
    public void UiModel_LoadArgs_RejectsModelOutsideTheRecentList()
    {
        using Fixture fixture = Fixture.Create();
        string otherModel = Path.Combine(fixture.ModelDir, "other.gguf");
        File.WriteAllText(otherModel, "");

        Assert.Throws<InvalidOperationException>(() => fixture.UiModel.LoadArgs(otherModel));
    }

    [Fact]
    public void UiModel_SaveArgs_WritesPerModelArgsFile()
    {
        using Fixture fixture = Fixture.Create();
        string content = "# edited from the window\n-c 2048\n--jinja\n";

        fixture.UiModel.SaveArgs(fixture.ModelPath, content);

        Assert.Equal(content, File.ReadAllText(LauncherArgs.PerModelArgsPath(fixture.ModelPath)));
    }

    [Fact]
    public void UiModel_SaveArgs_RejectsModelOutsideTheRecentList()
    {
        using Fixture fixture = Fixture.Create();
        string otherModel = Path.Combine(fixture.ModelDir, "other.gguf");
        File.WriteAllText(otherModel, "");

        Assert.Throws<InvalidOperationException>(() => fixture.UiModel.SaveArgs(otherModel, "-c 1\n"));
        Assert.False(File.Exists(LauncherArgs.PerModelArgsPath(otherModel)));
    }

    [Fact]
    public void UiModel_Launch_InvokesTheLauncherForRecordedModel()
    {
        using Fixture fixture = Fixture.Create();

        fixture.UiModel.Launch(fixture.ModelPath);

        Assert.Equal(new[] { fixture.ModelPath }, fixture.Launched);
    }

    [Fact]
    public void UiModel_Launch_RejectsMissingModelFile()
    {
        using Fixture fixture = Fixture.Create();
        File.Delete(fixture.ModelPath);

        Assert.Throws<InvalidOperationException>(() => fixture.UiModel.Launch(fixture.ModelPath));
        Assert.Empty(fixture.Launched);
    }

    [Fact]
    public void UiModel_Launch_RejectsModelOutsideTheRecentList()
    {
        using Fixture fixture = Fixture.Create();
        string otherModel = Path.Combine(fixture.ModelDir, "other.gguf");
        File.WriteAllText(otherModel, "");

        Assert.Throws<InvalidOperationException>(() => fixture.UiModel.Launch(otherModel));
        Assert.Empty(fixture.Launched);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempRoot _root;

        private Fixture(
            TempRoot root,
            string exeDir,
            string modelDir,
            string modelPath,
            RecentModelStore store,
            List<string> launched)
        {
            _root = root;
            ExeDir = exeDir;
            ModelDir = modelDir;
            ModelPath = modelPath;
            Store = store;
            Launched = launched;
        }

        public string ExeDir { get; }
        public string ModelDir { get; }
        public string ModelPath { get; }
        public RecentModelStore Store { get; }
        public List<string> Launched { get; }
        public LauncherUiModel UiModel => new(ExeDir, Store, Launched.Add);

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
            store.Record(modelPath);

            return new Fixture(root, exeDir, modelDir, modelPath, store, []);
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gguf-launcher-tests", Guid.NewGuid().ToString("N"));
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
