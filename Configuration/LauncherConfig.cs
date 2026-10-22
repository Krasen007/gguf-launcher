using System.Text.Json;

namespace GgufLauncher.Configuration;

/// <summary>
/// Global defaults for the launcher. Loaded from <c>config.json</c> and then
/// refined by command-line flags and per-model overrides.
/// </summary>
public sealed class LauncherConfig
{
    /// <summary>Absolute path to <c>llama-server.exe</c>. When null the launcher searches PATH and the winget package folder.</summary>
    public string? LlamaServerPath { get; set; }

    /// <summary>Interface the server binds to. Every interface by default so the model is reachable from the LAN.</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>TCP port for the OpenAI-compatible API and the built-in web UI.</summary>
    public int Port { get; set; } = 8080;

    /// <summary>Context window. Null or 0 means "use the context length baked into the model".</summary>
    public int? ContextSize { get; set; }

    /// <summary>
    /// Ceiling for the context the launcher picks from the model itself. A model usually
    /// advertises the context it was trained with, which can be far larger than the GPU can
    /// hold once a draft model and a projector share the memory, so the launcher starts from
    /// the smaller of the two.
    /// </summary>
    /// <remarks>
    /// The automatic tuner never picks a context larger than this value, not even when
    /// <see cref="AutoTuneConfig.MaxContextSize"/> asks for more; the smaller of the two
    /// wins.
    /// </remarks>
    public int MaxContextSize { get; set; } = 65536;

    /// <summary>CPU threads. 0 means "let llama.cpp decide".</summary>
    public int Threads { get; set; }

    /// <summary>
    /// Layers to offload to the GPU. Null means the launcher decides on its own from the
    /// size of the model and the free video memory. A negative value is passed to
    /// llama.cpp as it is, where -1 asks for every layer.
    /// </summary>
    public int? GpuLayers { get; set; }

    /// <summary>Use flash attention. Faster and lighter on memory, supported by every recent llama.cpp build.</summary>
    public bool FlashAttention { get; set; } = true;

    /// <summary>Use the model's Jinja chat template. Required by most modern instruct models
    /// and by tool/function calling. Some clients send non-alternating roles which strict
    /// Jinja templates reject; use --no-jinja if your client does not alternate roles.</summary>
    public bool Jinja { get; set; } = true;

    /// <summary>Number of server slots. 1 keeps the KV cache budget for a single conversation.</summary>
    public int ParallelSlots { get; set; } = 1;

    /// <summary>Open the built-in web UI in the default browser once the server is healthy.</summary>
    public bool OpenBrowser { get; set; } = true;

    /// <summary>Disable the built-in web UI and serve the API only.</summary>
    public bool NoWebUi { get; set; }

    /// <summary>Keep Mixture-of-Experts weights in system RAM. Required for large MoE models on small GPUs.</summary>
    public bool CpuMoe { get; set; }

    /// <summary>Explicit multimodal projector path. Overrides sibling auto-detection.</summary>
    public string? Mmproj { get; set; }

    /// <summary>Explicit speculative-decoding draft model path. Overrides sibling auto-detection.</summary>
    public string? SpecDraftModel { get; set; }

    /// <summary>Speculative decoding strategy, e.g. <c>draft-dspark</c> or <c>draft-mtp</c>.</summary>
    public string? SpecType { get; set; }

    /// <summary>Maximum number of tokens drafted per speculative step.</summary>
    public int SpecDraftNMax { get; set; } = 3;

    /// <summary>
    /// Whether llama.cpp may sample the draft model on the GPU backend. The Vulkan
    /// backend aborts the whole server on the first request when it does, so this
    /// stays off unless it is switched on explicitly.
    /// </summary>
    public bool? DraftBackendSampling { get; set; }

    /// <summary>KV cache data type for K, e.g. <c>q8_0</c>. Null leaves the llama.cpp default.</summary>
    public string? CacheTypeK { get; set; }

    /// <summary>KV cache data type for V, e.g. <c>q8_0</c>. Null leaves the llama.cpp default.</summary>
    public string? CacheTypeV { get; set; }

    /// <summary>Raw llama.cpp flags appended verbatim to every launch.</summary>
    public List<string> ExtraArgs { get; set; } = [];

    /// <summary>Settings of the automatic tuner that fits the model into the free video memory.</summary>
    public AutoTuneConfig AutoTune { get; set; } = new();

    /// <summary>Per-model overrides, keyed by full path, file name, or file name without extension.</summary>
    public Dictionary<string, ModelOverride> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Absolute path of the config file this instance was loaded from, or null when defaults were used.</summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Problems found while reading the configuration, for example a file that is not valid
    /// JSON. The launcher prints them; the configuration itself stays usable.
    /// </summary>
    public List<string> Warnings { get; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    /// <summary>
    /// Loads the first config file that exists, searching: an explicit path, the
    /// launcher executable's own folder, then %APPDATA%\GgufLauncher.
    /// </summary>
    public static LauncherConfig Load(string? explicitPath = null)
    {
        List<string> warnings = [];
        LauncherConfig? loaded = null;

        foreach (string? candidate in CandidatePaths(explicitPath))
        {
            if (candidate is null || !File.Exists(candidate))
            {
                continue;
            }

            loaded = Parse(candidate, warnings);
            if (loaded is not null)
            {
                break;
            }
        }

        LauncherConfig config = loaded ?? new LauncherConfig();
        config.Warnings.AddRange(warnings);
        return config;
    }

    private static IEnumerable<string?> CandidatePaths(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            yield return Path.GetFullPath(explicitPath);
            yield break;
        }

        string exeFolder = AppContext.BaseDirectory;
        yield return Path.Combine(exeFolder, "config.json");

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            yield return Path.Combine(appData, "GgufLauncher", "config.json");
        }
    }

    private static LauncherConfig? Parse(string path, List<string> warnings)
    {
        try
        {
            string json = File.ReadAllText(path);
            LauncherConfig? config = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOptions);
            if (config is null)
            {
                warnings.Add($"ignoring '{path}': the file holds no configuration");
                return null;
            }

            config.SourcePath = path;
            config.Models = config.Models is null
                ? new Dictionary<string, ModelOverride>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, ModelOverride>(config.Models, StringComparer.OrdinalIgnoreCase);
            config.ExtraArgs ??= [];
            return config;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warnings.Add($"ignoring '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Finds the override for a model, trying the full path, then the file name,
    /// then the file name without its extension.
    /// </summary>
    public ModelOverride? FindOverride(string modelPath)
    {
        if (Models.Count == 0)
        {
            return null;
        }

        string full = Path.GetFullPath(modelPath);
        string fileName = Path.GetFileName(full);
        string stem = Path.GetFileNameWithoutExtension(full);

        return Models.GetValueOrDefault(full)
            ?? Models.GetValueOrDefault(fileName)
            ?? Models.GetValueOrDefault(stem);
    }

    /// <summary>
    /// Settings of the automatic tuner that fits a model into the free video memory.
    /// The keys are the same as the ones of the tuner itself, so a single object
    /// describes the whole plan.
    /// </summary>
    /// <remarks>
    /// Every value is kept in a nullable field underneath, so a per-model section can
    /// leave a key out and still inherit it from the global section. The public
    /// properties always show the effective value, with the built-in default as the
    /// fallback.
    /// </remarks>
    public sealed class AutoTuneConfig
    {
        private bool? _enabled;
        private int? _marginMiB;
        private int? _maxContextSize;
        private bool? _allowProjectorSkip;
        private bool? _allowDraftModelSkip;

        /// <summary>Whether the launcher tunes the flags for the free video memory. On by default.</summary>
        public bool Enabled
        {
            get => _enabled ?? true;
            set => _enabled = value;
        }

        /// <summary>Video memory that is kept free for everything else, in MiB.</summary>
        public int MarginMiB
        {
            get => _marginMiB ?? 1024;
            set => _marginMiB = value;
        }

        /// <summary>Context window to use. Null lets the tuner pick one that fits.</summary>
        public int? ContextSize { get; set; }

        /// <summary>Layers to offload to the GPU. Null lets the tuner pick a number that fits.</summary>
        public int? GpuLayers { get; set; }

        /// <summary>Quantisation of the key part of the KV cache. Null lets the tuner pick one.</summary>
        public string? CacheTypeK { get; set; }

        /// <summary>Quantisation of the value part of the KV cache. Null lets the tuner pick one.</summary>
        public string? CacheTypeV { get; set; }

        /// <summary>Logical batch size. Null lets the tuner pick one.</summary>
        public int? BatchSize { get; set; }

        /// <summary>Physical batch size, the one that sizes the compute buffer. Null lets the tuner pick one.</summary>
        public int? MicroBatchSize { get; set; }

        /// <summary>CPU threads. Null or 0 lets the tuner pick a number.</summary>
        public int? Threads { get; set; }

        /// <summary>Largest context window the tuner may pick.</summary>
        public int MaxContextSize
        {
            get => _maxContextSize ?? 65536;
            set => _maxContextSize = value;
        }

        /// <summary>Whether the projector may be left out when it does not fit in the free video memory.</summary>
        public bool AllowProjectorSkip
        {
            get => _allowProjectorSkip ?? true;
            set => _allowProjectorSkip = value;
        }

        /// <summary>Whether the draft model may be left out when it does not fit in the free video memory.</summary>
        public bool AllowDraftModelSkip
        {
            get => _allowDraftModelSkip ?? true;
            set => _allowDraftModelSkip = value;
        }

        /// <summary>The value of <see cref="Enabled"/> as it was written, or null when the key was left out.</summary>
        internal bool? EnabledRaw => _enabled;

        /// <summary>The value of <see cref="MarginMiB"/> as it was written, or null when the key was left out.</summary>
        internal int? MarginMiBRaw => _marginMiB;

        /// <summary>The value of <see cref="MaxContextSize"/> as it was written, or null when the key was left out.</summary>
        internal int? MaxContextSizeRaw => _maxContextSize;

        /// <summary>The value of <see cref="AllowProjectorSkip"/> as it was written, or null when the key was left out.</summary>
        internal bool? AllowProjectorSkipRaw => _allowProjectorSkip;

        /// <summary>The value of <see cref="AllowDraftModelSkip"/> as it was written, or null when the key was left out.</summary>
        internal bool? AllowDraftModelSkipRaw => _allowDraftModelSkip;
    }
}
