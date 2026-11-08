using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GgufLauncher;

/// <summary>
/// One entry in the quick-pick list: a model the launcher has opened.
/// </summary>
public sealed class RecentModel
{
    public string Path { get; set; } = "";

    /// <summary>UTC timestamp in ISO 8601 format ("O").</summary>
    public string LastRunUtc { get; set; } = "";
}

/// <summary>
/// Persists the last models opened by the launcher, most recent first. The list is capped
/// at <see cref="MaxCount"/> entries and lives in the user's local application data, so a
/// read-only install folder cannot break it.
/// </summary>
public sealed class RecentModelStore
{
    public const int MaxCount = 10;

    private readonly string _filePath;

    public RecentModelStore(string filePath)
    {
        _filePath = filePath;
    }

    public static RecentModelStore Default { get; } = new(DefaultPath());

    public string FilePath => _filePath;

    public static string DefaultPath()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local))
        {
            local = AppContext.BaseDirectory;
        }

        return Path.Combine(local, "GgufLauncher", "recent-models.json");
    }

    /// <summary>
    /// Reads the stored list. A missing or unreadable file is an empty list so the UI still
    /// starts; a corrupt file is treated the same way.
    /// </summary>
    public IReadOnlyList<RecentModel> Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(_filePath);
            List<RecentModel>? models = JsonSerializer.Deserialize(json, RecentModelsJsonContext.Default.ListRecentModel);
            if (models is null)
            {
                return [];
            }

            return models
                .Where(m => m is not null && !string.IsNullOrWhiteSpace(m.Path))
                .Take(MaxCount)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// True when the model path is already in the stored list. Used to keep the UI API from
    /// reading or writing arbitrary paths on disk.
    /// </summary>
    public bool Contains(string modelPath)
    {
        string fullPath = Path.GetFullPath(modelPath);
        return Load().Any(m => string.Equals(m.Path, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Moves (or inserts) the model to the front of the list and persists it. Returns the
    /// updated list.
    /// </summary>
    public IReadOnlyList<RecentModel> Record(string modelPath, DateTimeOffset? when = null)
    {
        string fullPath = Path.GetFullPath(modelPath);
        List<RecentModel> models = Load()
            .Where(m => !string.Equals(m.Path, fullPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        models.Insert(0, new RecentModel
        {
            Path = fullPath,
            LastRunUtc = (when ?? DateTimeOffset.UtcNow).ToString("O", CultureInfo.InvariantCulture),
        });

        if (models.Count > MaxCount)
        {
            models.RemoveRange(MaxCount, models.Count - MaxCount);
        }

        Save(models);
        return models;
    }

    private void Save(List<RecentModel> models)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(models, RecentModelsJsonContext.Default.ListRecentModel);

        // Unique temp name so two launcher processes cannot clobber each other's write.
        string tempPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<RecentModel>))]
internal partial class RecentModelsJsonContext : JsonSerializerContext
{
}
