using System.Collections.ObjectModel;

namespace GgufLauncher.Gguf;

/// <summary>
/// The metadata of one GGUF model file: its header fields and the derived properties the
/// launcher needs to build a llama-server command line.
/// </summary>
/// <remarks>
/// Instances are immutable and are created by <see cref="Read(string)"/>. Because
/// <c>general.architecture</c> may appear after the architecture specific keys in the file, all
/// raw pairs are stored first and the architecture prefixed values are resolved afterwards in a
/// second pass.
/// </remarks>
public sealed class GgufModel
{
    /// <summary>
    /// The architectures that are known to need a multimodal server, used when a file carries no
    /// <c>clip.*</c> keys of its own.
    /// </summary>
    private static readonly string[] MultimodalArchitectures =
    {
        "llama4", "qwen2vl", "qwen3vl", "qwen3vlmoe", "gemma3", "gemma3n", "dots3note", "cogvlm",
        "ernie4_5", "ernie4_5-moe", "step35", "minicpmv", "pixtral", "glm4v", "glm4_1v", "glm5v",
        "idefics3", "internvl",
    };

    private readonly IReadOnlyDictionary<string, GgufValue> _metadata;
    private readonly string? _architecture;
    private readonly string? _name;
    private readonly string? _sizeLabel;
    private readonly ulong? _parameterCount;
    private readonly uint? _fileType;
    private readonly string? _fileTypeName;
    private readonly ulong? _contextLength;
    private readonly uint? _expertCount;
    private readonly uint? _expertUsedCount;
    private readonly bool _isMultimodal;
    private readonly bool _isProjector;
    private readonly ulong _tensorDataBytes;
    private readonly ulong _vocabularyCount;
    private readonly bool _hasSeparateOutputProjection;

    /// <summary>Initializes a model from the raw values produced by the reader.</summary>
    /// <param name="metadata">Every raw key/value pair of the metadata section.</param>
    /// <param name="tensorDataBytes">The number of bytes of tensor payload in the file.</param>
    /// <param name="vocabularyCount">The number of entries in the tokenizer vocabulary.</param>
    /// <param name="hasSeparateOutputProjection">Whether the file stores an output projection matrix of its own.</param>
    internal GgufModel(
        Dictionary<string, GgufValue> metadata,
        ulong tensorDataBytes,
        ulong vocabularyCount,
        bool hasSeparateOutputProjection)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        _metadata = new ReadOnlyDictionary<string, GgufValue>(metadata);
        _tensorDataBytes = tensorDataBytes;
        _vocabularyCount = vocabularyCount;
        _hasSeparateOutputProjection = hasSeparateOutputProjection;

        _architecture = GetString("general.architecture");
        _name = GetString("general.name");
        _sizeLabel = GetString("general.size_label");
        _parameterCount = GetUInt64("general.parameter_count");
        _fileType = GetUInt32("general.file_type");
        _fileTypeName = _fileType.HasValue ? GgufFileType.Describe(_fileType.Value) : null;

        string? architecture = _architecture;
        _contextLength = architecture is null ? null : GetUInt64(architecture + ".context_length");
        _expertCount = architecture is null ? null : GetUInt32(architecture + ".expert_count");
        _expertUsedCount = architecture is null ? null : GetUInt32(architecture + ".expert_used_count");

        bool hasClipMetadata = HasKeyStartingWith("clip.");
        bool hasVisionEncoder = GetBool("clip.has_vision_encoder") == true;
        bool isClipArchitecture = string.Equals(architecture, "clip", StringComparison.OrdinalIgnoreCase);
        bool isProjectorType = string.Equals(GetString("general.type"), "mmproj", StringComparison.OrdinalIgnoreCase);

        _isMultimodal = hasClipMetadata || hasVisionEncoder || IsMultimodalArchitecture(architecture);
        _isProjector = isProjectorType || (hasClipMetadata && (architecture is null || isClipArchitecture));
    }

    /// <summary>Reads the metadata of a GGUF file.</summary>
    /// <param name="path">The path of the <c>.gguf</c> file to read.</param>
    /// <returns>The parsed model metadata.</returns>
    /// <exception cref="GgufException">
    /// The file is missing, unreadable, not a GGUF model, truncated or uses an unsupported
    /// feature. The message is safe to show to an end user.
    /// </exception>
    public static GgufModel Read(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return GgufReader.Read(path);
    }

    /// <summary>Gets the architecture of the model, for example <c>qwen3</c>, if it is known.</summary>
    public string? Architecture => _architecture;

    /// <summary>Gets the human readable model name, if the file provides one.</summary>
    public string? Name => _name;

    /// <summary>Gets the size label of the model, for example <c>4.7B</c>, if it is known.</summary>
    public string? SizeLabel => _sizeLabel;

    /// <summary>Gets the number of parameters of the model, if the file provides one. This optional key is not part of the GGUF specification.</summary>
    public ulong? ParameterCount => _parameterCount;

    /// <summary>Gets the llama.cpp quantization name of the file, for example <c>Q4_K_M</c>.</summary>
    public string? FileTypeName => _fileTypeName;

    /// <summary>Gets the training context length of the architecture, if it is known.</summary>
    public ulong? ContextLength => _contextLength;

    /// <summary>Gets the number of experts of a mixture of experts model, if it is known.</summary>
    public uint? ExpertCount => _expertCount;

    /// <summary>Gets the number of experts used per token, if it is known.</summary>
    public uint? ExpertUsedCount => _expertUsedCount;

    /// <summary>Gets a value indicating whether the model is multimodal, which decides whether the launcher starts llama-server with a projector.</summary>
    public bool IsMultimodal => _isMultimodal;

    /// <summary>
    /// Gets a value indicating whether the file is a projector sidecar (<c>mmproj-*.gguf</c>)
    /// rather than a servable base model.
    /// </summary>
    /// <remarks>
    /// A projector carries <c>clip.*</c> metadata and either no architecture at all or the
    /// <c>clip</c> architecture, and it announces itself with <c>general.type</c> set to
    /// <c>mmproj</c>.
    /// </remarks>
    public bool IsProjector => _isProjector;

    /// <summary>
    /// Gets the number of bytes of tensor payload in the file, which is the size of the model
    /// weights and therefore the closest estimate of the video memory they need.
    /// </summary>
    /// <remarks>
    /// The value counts everything that follows the header, the metadata and the tensor
    /// directory, so it also holds the small padding gaps between the tensors. It is a little
    /// larger than the memory llama.cpp really reserves for the weights, which makes it a safe
    /// estimate to plan with.
    /// </remarks>
    public ulong TensorDataBytes => _tensorDataBytes;

    /// <summary>
    /// Gets the number of words in the tokenizer vocabulary of the model, or zero when the file
    /// does not say how large the vocabulary is.
    /// </summary>
    /// <remarks>
    /// The vocabulary decides how big the buffer with the scores of all words becomes, so the
    /// launcher needs the number to work out how much memory that buffer takes.
    /// </remarks>
    public ulong VocabularyCount => _vocabularyCount;

    /// <summary>
    /// Gets a value indicating whether the file stores an output projection matrix of its own,
    /// next to the embedding matrix.
    /// </summary>
    /// <remarks>
    /// Most models use one matrix for both, but some keep a second copy named
    /// <c>output.weight</c>. Only one of the two is needed on the graphics card, so the launcher
    /// discounts the copy from the memory the weights take.
    /// </remarks>
    public bool HasSeparateOutputProjection => _hasSeparateOutputProjection;

    /// <summary>
    /// Reads an unsigned integer metadata value, accepting every integer width the format allows.
    /// </summary>
    /// <param name="key">The metadata key to look up.</param>
    /// <returns>
    /// The value, or <see langword="null"/> when the key is missing, holds another type or holds
    /// a negative number.
    /// </returns>
    public ulong? GetUInt64(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!_metadata.TryGetValue(key, out GgufValue value))
        {
            return null;
        }

        return value.TryGetUInt64(out ulong result) ? result : null;
    }

    /// <summary>Reads a string metadata value.</summary>
    /// <param name="key">The metadata key to look up.</param>
    /// <returns>The value, or <see langword="null"/> when the key is missing or holds another type.</returns>
    public string? GetString(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!_metadata.TryGetValue(key, out GgufValue value))
        {
            return null;
        }

        return value.TryGetString(out string result) ? result : null;
    }

    /// <summary>Reads a boolean metadata value.</summary>
    /// <param name="key">The metadata key to look up.</param>
    /// <returns>The value, or <see langword="null"/> when the key is missing or holds another type.</returns>
    public bool? GetBool(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (!_metadata.TryGetValue(key, out GgufValue value))
        {
            return null;
        }

        return value.TryGetBool(out bool result) ? result : null;
    }

    /// <summary>Reads an unsigned integer metadata value as a 32 bit number.</summary>
    /// <param name="key">The metadata key to look up.</param>
    /// <returns>The value, or <see langword="null"/> when it is missing, negative or too large.</returns>
    private uint? GetUInt32(string key)
    {
        if (!_metadata.TryGetValue(key, out GgufValue value))
        {
            return null;
        }

        if (!value.TryGetUInt64(out ulong result) || result > uint.MaxValue)
        {
            return null;
        }

        return (uint)result;
    }

    /// <summary>Checks whether any metadata key starts with the given prefix.</summary>
    /// <param name="prefix">The prefix to look for, for example <c>clip.</c>.</param>
    /// <returns><see langword="true"/> when at least one key matches.</returns>
    private bool HasKeyStartingWith(string prefix)
    {
        foreach (string key in _metadata.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Checks whether an architecture is known to require a multimodal server.</summary>
    /// <param name="architecture">The architecture name, if the file provides one.</param>
    /// <returns><see langword="true"/> when the architecture is a known multimodal one.</returns>
    private static bool IsMultimodalArchitecture(string? architecture)
    {
        if (architecture is null)
        {
            return false;
        }

        foreach (string known in MultimodalArchitectures)
        {
            if (string.Equals(known, architecture, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
