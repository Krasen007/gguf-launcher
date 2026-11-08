using System.Globalization;

namespace GgufLauncher;

/// <summary>
/// One row in the native quick-pick list.
/// </summary>
public sealed record LauncherModelEntry(
    string Path,
    string Name,
    string Directory,
    DateTimeOffset? LastRunUtc,
    bool Exists,
    bool HasArgs);

/// <summary>
/// The args editor content for one model. When the per-model file does not exist yet the
/// content is the same generated preview the launcher would write on first launch.
/// </summary>
public sealed record LauncherArgsContent(
    string ModelPath,
    string ArgsPath,
    bool Exists,
    string Content);

/// <summary>
/// Headless state and actions behind the native quick-pick window. Keeping this separate from
/// the form lets the recents, preview, validation, save, and launch behavior be tested without
/// creating a window.
/// </summary>
public sealed class LauncherUiModel
{
    private readonly string _exeDir;
    private readonly RecentModelStore _store;
    private readonly Action<string> _launch;

    public LauncherUiModel(string exeDir, RecentModelStore store, Action<string> launch)
    {
        _exeDir = exeDir;
        _store = store;
        _launch = launch;
    }

    public IReadOnlyList<LauncherModelEntry> LoadModels()
    {
        List<LauncherModelEntry> models = [];
        foreach (RecentModel model in _store.Load())
        {
            models.Add(new LauncherModelEntry(
                model.Path,
                Path.GetFileName(model.Path),
                Path.GetDirectoryName(model.Path) ?? "",
                ParseTimestamp(model.LastRunUtc),
                File.Exists(model.Path),
                File.Exists(LauncherArgs.PerModelArgsPath(model.Path))));
        }

        return models;
    }

    public LauncherArgsContent LoadArgs(string modelPath)
    {
        string recordedPath = ResolveRecorded(modelPath);
        string argsPath = LauncherArgs.PerModelArgsPath(recordedPath);
        bool exists = File.Exists(argsPath);

        string content = exists
            ? File.ReadAllText(argsPath)
            : LauncherArgs.RenderArgsFile(LaunchPlanner.Create(_exeDir, recordedPath, [], null).FinalArgs);

        return new LauncherArgsContent(recordedPath, argsPath, exists, content);
    }

    public void SaveArgs(string modelPath, string content)
    {
        string recordedPath = ResolveRecorded(modelPath);
        File.WriteAllText(LauncherArgs.PerModelArgsPath(recordedPath), content ?? "");
    }

    public void Launch(string modelPath)
    {
        string recordedPath = ResolveRecorded(modelPath);
        if (!File.Exists(recordedPath))
        {
            throw new InvalidOperationException("model file not found");
        }

        _launch(recordedPath);
    }

    private string ResolveRecorded(string modelPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(modelPath);
            return _store.Load()
                .FirstOrDefault(m => string.Equals(m.Path, fullPath, StringComparison.OrdinalIgnoreCase))
                ?.Path
                ?? throw new InvalidOperationException("model is not in the recent list");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException("model is not in the recent list", ex);
        }
    }

    private static DateTimeOffset? ParseTimestamp(string value)
    {
        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset timestamp))
        {
            return timestamp;
        }

        return null;
    }
}
