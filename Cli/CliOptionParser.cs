namespace GgufLauncher.Cli;

/// <summary>
/// Reads the command line of the launcher. The options are matched group by group, so every
/// handler stays short and the first problem on the command line is the one that is reported.
/// </summary>
internal static class CliOptionParser
{
    /// <summary>The KV cache quantisations llama.cpp accepts for the key and the value cache.</summary>
    private static readonly string[] AllowedCacheTypes = ["f16", "q8_0", "q4_0"];

    /// <summary>Parses the argument list into the options of the launcher.</summary>
    public static CliOptions Parse(string[] args)
    {
        Draft draft = new();

        for (int i = 0; i < args.Length; i++)
        {
            Apply(draft, args, ref i);
        }

        return draft.Build();
    }

    /// <summary>
    /// Applies one argument to the draft. An argument that matches no option has to be the
    /// model file, everything that starts with a dash is an option the launcher does not know.
    /// </summary>
    private static void Apply(Draft draft, string[] args, ref int index)
    {
        string arg = args[index];

        if (ApplyMode(draft, arg) || ApplyToggle(draft, arg))
        {
            return;
        }

        if (ApplyNumber(draft, args, ref index) || ApplyPath(draft, args, ref index) || ApplyTuning(draft, args, ref index))
        {
            return;
        }

        if (arg.StartsWith('-'))
        {
            throw new ArgumentException($"Unknown option '{arg}'.");
        }

        SetModel(draft, arg);
    }

    /// <summary>
    /// Applies the options that pick the operation. They only change the mode, so the last one
    /// on the command line wins.
    /// </summary>
    private static bool ApplyMode(Draft draft, string arg)
    {
        switch (arg)
        {
            case "--register":
                draft.Mode = CliMode.Register;
                break;
            case "--unregister":
                draft.Mode = CliMode.Unregister;
                break;
            case "--status":
                draft.Mode = CliMode.Status;
                break;
            case "-h":
            case "--help":
                draft.Mode = CliMode.Help;
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>
    /// Applies the options that only switch a feature on or off. Every pair of them keeps its
    /// own value, so the last one on the command line wins.
    /// </summary>
    private static bool ApplyToggle(Draft draft, string arg)
    {
        switch (arg)
        {
            case "--dry-run":
                draft.DryRun = true;
                break;
            case "--quiet":
                draft.Quiet = true;
                break;
            case "--no-browser":
                draft.OpenBrowser = false;
                break;
            case "--browser":
                draft.OpenBrowser = true;
                break;
            case "--no-webui":
                draft.NoWebUi = true;
                break;
            case "--webui":
                draft.NoWebUi = false;
                break;
            case "--cpu-moe":
                draft.CpuMoe = true;
                break;
            case "--no-cpu-moe":
                draft.CpuMoe = false;
                break;
            case "--jinja":
                draft.Jinja = true;
                break;
            case "--no-jinja":
                draft.Jinja = false;
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>
    /// Applies the options that carry a number. The value is checked right away, so the first
    /// bad number on the command line is the one that gets reported.
    /// </summary>
    private static bool ApplyNumber(Draft draft, string[] args, ref int index)
    {
        string arg = args[index];

        switch (arg)
        {
            case "--port":
                draft.Port = ParsePort(Next(args, ref index, arg));
                break;
            case "--ctx":
                draft.Context = ParsePositive(Next(args, ref index, arg), arg);
                break;
            case "--threads":
                draft.Threads = ParsePositive(Next(args, ref index, arg), arg);
                break;
            case "--ngl":
                draft.GpuLayers = ParsePositive(Next(args, ref index, arg), arg);
                break;
            case "--vram-margin":
                draft.VramMarginMiB = ParsePositive(Next(args, ref index, arg), arg);
                break;
            case "--batch-size":
                draft.BatchSize = ParsePositive(Next(args, ref index, arg), arg);
                break;
            case "--ubatch-size":
                draft.UbatchSize = ParsePositive(Next(args, ref index, arg), arg);
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>
    /// Applies the options that carry a path or a name. They are taken over unchanged; what the
    /// launcher does with them is decided later. The model file is the one path that is stored
    /// right away, so it follows the rule that only one model can be given.
    /// </summary>
    private static bool ApplyPath(Draft draft, string[] args, ref int index)
    {
        string arg = args[index];

        switch (arg)
        {
            case "--config":
                draft.Config = Next(args, ref index, arg);
                break;
            case "--llama-server":
                draft.Server = Next(args, ref index, arg);
                break;
            case "--host":
                draft.Host = Next(args, ref index, arg);
                break;
            case "--mmproj":
                draft.Mmproj = Next(args, ref index, arg);
                break;
            case "--spec-draft-model":
                draft.DraftModel = Next(args, ref index, arg);
                break;
            case "--spec-type":
                draft.SpecType = Next(args, ref index, arg);
                break;
            case "-m":
            case "--model":
                SetModel(draft, Next(args, ref index, arg));
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>
    /// Applies the options of the automatic tuner. Their value is a word rather than a
    /// number, so they get a group of their own.
    /// </summary>
    private static bool ApplyTuning(Draft draft, string[] args, ref int index)
    {
        string arg = args[index];

        switch (arg)
        {
            case "--auto-tune":
                draft.AutoTune = ParseSwitch(Next(args, ref index, arg), arg);
                break;
            case "--cache-type":
                draft.CacheType = ParseCacheType(Next(args, ref index, arg), arg);
                break;
            default:
                return false;
        }

        return true;
    }

    /// <summary>
    /// Stores the model file. A second one is refused because the launcher can serve a single
    /// model at a time.
    /// </summary>
    private static void SetModel(Draft draft, string arg)
    {
        if (draft.Model is not null)
        {
            throw new ArgumentException("Only one model file can be given.");
        }

        draft.Model = arg;
    }

    /// <summary>Returns the value that follows the option and moves past it.</summary>
    private static string Next(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Option '{option}' needs a value.");
        }

        index++;
        return args[index];
    }

    /// <summary>Reads a TCP port and refuses everything outside the valid range.</summary>
    private static int ParsePort(string value)
    {
        if (!int.TryParse(value, out int port) || port is < 1 or > 65535)
        {
            throw new ArgumentException($"'{value}' is not a valid port.");
        }

        return port;
    }

    /// <summary>Reads a number that must not be negative, such as a context size.</summary>
    private static int ParsePositive(string value, string option)
    {
        if (!int.TryParse(value, out int number) || number < 0)
        {
            throw new ArgumentException($"Option '{option}' needs a non-negative number, got '{value}'.");
        }

        return number;
    }

    /// <summary>Reads a word that switches a feature on or off.</summary>
    private static bool ParseSwitch(string value, string option)
    {
        return value.ToLowerInvariant() switch
        {
            "on" or "true" => true,
            "off" or "false" => false,
            _ => throw new ArgumentException($"Option '{option}' needs 'on' or 'off', got '{value}'."),
        };
    }

    /// <summary>Reads a KV cache quantisation and refuses the ones llama.cpp does not know.</summary>
    private static string ParseCacheType(string value, string option)
    {
        string wanted = value.ToLowerInvariant();
        foreach (string allowed in AllowedCacheTypes)
        {
            if (string.Equals(allowed, wanted, StringComparison.Ordinal))
            {
                return allowed;
            }
        }

        throw new ArgumentException($"Option '{option}' needs one of {string.Join(", ", AllowedCacheTypes)}, got '{value}'.");
    }

    /// <summary>Collects the values while the argument list is scanned, before they are frozen.</summary>
    private sealed class Draft
    {
        /// <summary>Operation the launcher has to perform.</summary>
        public CliMode Mode { get; set; } = CliMode.Launch;
        /// <summary>Model file to serve.</summary>
        public string? Model { get; set; }
        /// <summary>Configuration file to use instead of the search.</summary>
        public string? Config { get; set; }
        /// <summary>Path of the llama-server binary to use.</summary>
        public string? Server { get; set; }
        /// <summary>Port to listen on.</summary>
        public int? Port { get; set; }
        /// <summary>Interface the server binds to.</summary>
        public string? Host { get; set; }
        /// <summary>Context size to request.</summary>
        public int? Context { get; set; }
        /// <summary>Number of CPU threads for the server.</summary>
        public int? Threads { get; set; }
        /// <summary>Number of layers to offload to the graphics card.</summary>
        public int? GpuLayers { get; set; }
        /// <summary>Whether the automatic tuner runs.</summary>
        public bool? AutoTune { get; set; }
        /// <summary>Video memory to keep free for everything else, in MiB.</summary>
        public int? VramMarginMiB { get; set; }
        /// <summary>Quantisation of both parts of the KV cache.</summary>
        public string? CacheType { get; set; }
        /// <summary>Logical batch size.</summary>
        public int? BatchSize { get; set; }
        /// <summary>Physical batch size.</summary>
        public int? UbatchSize { get; set; }
        /// <summary>Whether the browser is opened once the server is ready.</summary>
        public bool? OpenBrowser { get; set; }
        /// <summary>Whether the built-in web interface is switched off.</summary>
        public bool? NoWebUi { get; set; }
        /// <summary>Whether the expert weights stay in system memory.</summary>
        public bool? CpuMoe { get; set; }
        /// <summary>Whether the model's Jinja chat template is used.</summary>
        public bool? Jinja { get; set; }
        /// <summary>Multimodal projector to use.</summary>
        public string? Mmproj { get; set; }
        /// <summary>Draft model for speculative decoding.</summary>
        public string? DraftModel { get; set; }
        /// <summary>Speculative decoding strategy.</summary>
        public string? SpecType { get; set; }
        /// <summary>Whether the command line is printed instead of run.</summary>
        public bool DryRun { get; set; }
        /// <summary>Whether the launcher keeps its output to a minimum.</summary>
        public bool Quiet { get; set; }

        /// <summary>Turns the collected values into the immutable options of the launcher.</summary>
        public CliOptions Build()
        {
            CliMode mode = Mode;

            if (mode == CliMode.Launch && Model is null)
            {
                mode = CliMode.Help;
            }

            return new CliOptions
            {
                Mode = mode,
                ModelPath = Model,
                ConfigPath = Config,
                LlamaServerPath = Server,
                Port = Port,
                Host = Host,
                ContextSize = Context,
                Threads = Threads,
                GpuLayers = GpuLayers,
                AutoTune = AutoTune,
                VramMarginMiB = VramMarginMiB,
                CacheType = CacheType,
                BatchSize = BatchSize,
                UbatchSize = UbatchSize,
                OpenBrowser = OpenBrowser,
                NoWebUi = NoWebUi,
                CpuMoe = CpuMoe,
                Jinja = Jinja,
                Mmproj = Mmproj,
                SpecDraftModel = DraftModel,
                SpecType = SpecType,
                DryRun = DryRun,
                Quiet = Quiet,
            };
        }
    }
}
