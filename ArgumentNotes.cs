namespace GgufLauncher;

/// <summary>
/// Human-readable notes for the llama-server flags the launcher writes or that users commonly
/// put in an args file. <see cref="Describe"/> turns one editor line into the note shown in the
/// quick-pick window.
/// </summary>
public static class ArgumentNotes
{
    public const string UnknownNote = "no description in this launcher; passed to llama-server unchanged";

    /// <summary>
    /// Returns the note for the argument on one args-file line, or an empty string when the
    /// line is blank, a comment, or not an argument. The note belongs to the first flag on the
    /// line.
    /// </summary>
    public static string Describe(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return "";
        }

        string trimmed = line.Trim();
        if (trimmed.StartsWith('#'))
        {
            return "";
        }

        int end = 0;
        while (end < trimmed.Length && !char.IsWhiteSpace(trimmed[end]))
        {
            end++;
        }

        string flag = trimmed[..end];
        if (!flag.StartsWith('-'))
        {
            return "";
        }

        return Notes.TryGetValue(flag, out string? note)
            ? $"{flag}: {note}"
            : $"{flag}: {UnknownNote}";
    }

    private static Dictionary<string, string> Build()
    {
        Dictionary<string, string> notes = new(StringComparer.OrdinalIgnoreCase);

        Add(notes, "path to the model file", "-m", "--model");
        Add(notes, "context size in tokens - how much prompt and conversation the model can keep in memory", "-c", "--ctx-size");
        Add(notes, "prompt batch size - how many tokens llama-server processes at a time", "-b", "--batch-size");
        Add(notes, "micro-batch size - the chunk uploaded to the GPU within a batch", "-ub", "--ubatch-size");
        Add(notes, "CPU threads used for generation", "-t", "--threads");
        Add(notes, "number of request slots served in parallel", "-np", "--parallel");
        Add(notes, "model layers offloaded to the GPU (99 means every layer)", "-ngl", "--gpu-layers");
        Add(notes, "GPU layers used by the speculative draft model", "-ngld", "--gpu-layers-draft");
        Add(notes, "flash attention mode: on, off, or auto", "-fa", "--flash-attn");
        Add(notes, "use the chat template embedded in the model", "--jinja");
        Add(notes, "data type of the KV cache keys - q4_0 saves memory", "--cache-type-k", "-ctk");
        Add(notes, "data type of the KV cache values - q4_0 saves memory", "--cache-type-v", "-ctv");
        Add(notes, "KV cache key type for the speculative draft model", "--cache-type-k-draft", "-ctkd");
        Add(notes, "KV cache value type for the speculative draft model", "--cache-type-v-draft", "-ctvd");
        Add(notes, "sampling temperature - higher values are more random", "--temp", "--temperature");
        Add(notes, "nucleus sampling cutoff - keep the smallest token set whose probability adds up to this", "--top-p");
        Add(notes, "only sample from the N most likely tokens", "--top-k");
        Add(notes, "drop tokens below this fraction of the best token's probability", "--min-p");
        Add(notes, "penalty for tokens that already appeared in the text", "--presence-penalty");
        Add(notes, "penalty for repeated tokens; 1.0 disables it", "--repeat-penalty");
        Add(notes, "how many recent tokens the repeat penalty considers", "--repeat-last-n");
        Add(notes, "network interface to bind (0.0.0.0 exposes the server on the LAN)", "--host");
        Add(notes, "TCP port for the HTTP server", "--port");
        Add(notes, "model name shown in llama-server's web UI", "--alias");
        Add(notes, "launcher-only: do not open the browser when the server is ready", "--no-browser");
        Add(notes, "multimodal projector file that lets a vision model read images", "--mmproj");
        Add(notes, "draft model used for speculative decoding", "--spec-draft-model", "-md", "--model-draft");
        Add(notes, "speculative decoding strategy (draft-dspark or draft-mtp)", "--spec-type");
        Add(notes, "minimum probability for a speculative draft token", "--spec-draft-p-min");
        Add(notes, "minimum number of draft tokens tried per step", "--spec-draft-n-min");
        Add(notes, "maximum number of draft tokens tried per step", "--spec-draft-n-max");
        Add(notes, "sample draft tokens on the CPU to avoid GPU backend aborts", "--no-spec-draft-backend-sampling");
        Add(notes, "random seed; -1 picks a new one on every launch", "--seed");
        Add(notes, "skip the warmup pass after loading the model", "--no-warmup");
        Add(notes, "require this key in the Authorization header", "--api-key");
        Add(notes, "return embeddings instead of chat completions", "--embeddings", "--embedding");
        Add(notes, "LoRA adapter to apply", "--lora");
        Add(notes, "control vector (steering) file to apply", "--control-vector");
        Add(notes, "layer range the control vector applies to", "--control-vector-layer-range");
        Add(notes, "override a model metadata key at load time", "--override-kv");
        Add(notes, "override the type or device of tensors matching a pattern", "--override-tensor");
        Add(notes, "bias the logit of a specific token", "--logit-bias");
        Add(notes, "how the model is split across GPUs", "--split-mode");
        Add(notes, "how tensors are divided between GPUs", "--tensor-split");
        Add(notes, "GPU used for small tensors and scratch buffers", "--main-gpu");
        Add(notes, "load the model into RAM instead of memory-mapping it", "--no-mmap");
        Add(notes, "lock the model in RAM so it is never swapped out", "--mlock");
        Add(notes, "chat template to use instead of the model's default", "--chat-template");
        Add(notes, "disable the built-in web UI", "--no-webui");
        Add(notes, "increase log verbosity", "--verbose", "-v");

        return notes;
    }

    private static void Add(Dictionary<string, string> notes, string description, params string[] flags)
    {
        foreach (string flag in flags)
        {
            notes[flag] = description;
        }
    }

    private static readonly Dictionary<string, string> Notes = Build();
}
