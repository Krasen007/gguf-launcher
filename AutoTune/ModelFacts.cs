using GgufLauncher.Gguf;

namespace GgufLauncher.AutoTune;

/// <summary>
/// The facts about a model that the VRAM estimator needs. Every value is read from the GGUF
/// header, and every value that the header does not carry is zero or null so that the
/// estimator can decide how to compensate for it.
/// </summary>
/// <param name="Architecture">The architecture of the model, for example <c>qwen3</c>.</param>
/// <param name="SizeLabel">The size label of the model, for example <c>9B</c>.</param>
/// <param name="ParameterCount">The parameter count of the model, when the file announces it.</param>
/// <param name="ParameterCountMillions">The parameter count in millions, zero when it is unknown.</param>
/// <param name="TensorDataBytes">The number of bytes the tensor data of the file occupies.</param>
/// <param name="VocabularyCount">The number of entries in the vocabulary of the model.</param>
/// <param name="HasSeparateOutputProjection">
/// True when the file carries its own output projection tensor next to the token embedding.
/// </param>
/// <param name="BlockCount">The number of transformer blocks of the model.</param>
/// <param name="EmbeddingLength">The width of the token embedding of the model.</param>
/// <param name="HeadCount">The number of attention heads of the model.</param>
/// <param name="HeadCountKv">The number of key and value heads of the model.</param>
/// <param name="HeadDim">The width of one key or value head.</param>
/// <param name="FullAttentionInterval">How often a full attention layer appears in a hybrid model.</param>
/// <param name="SlidingWindow">The window of the sliding attention layers, zero when there are none.</param>
/// <param name="NativeContextLength">The context length the model was trained with, zero when unknown.</param>
/// <param name="QuantName">The quantization of the model, for example <c>Q4_K_M</c>.</param>
/// <param name="FileSizeBytes">The size of the model file on disk.</param>
public sealed record ModelFacts(
    string Architecture,
    string? SizeLabel,
    ulong? ParameterCount,
    long ParameterCountMillions,
    ulong TensorDataBytes,
    ulong VocabularyCount,
    bool HasSeparateOutputProjection,
    ulong BlockCount,
    ulong EmbeddingLength,
    ulong HeadCount,
    ulong HeadCountKv,
    ulong HeadDim,
    ulong FullAttentionInterval,
    ulong SlidingWindow,
    ulong NativeContextLength,
    string? QuantName,
    long FileSizeBytes)
{
    /// <summary>Size class derived from the parameter count.</summary>
    /// <remarks>
    /// A model that does not announce its parameter count lands in
    /// <see cref="ModelSizeClass.Medium"/>, which is the neutral middle of the table.
    /// </remarks>
    public ModelSizeClass SizeClass { get; } = ParameterCountMillions > 0
        ? ModelSize.Classify(ParameterCountMillions)
        : ModelSizeClass.Medium;

    /// <summary>Collects the facts of a model from its parsed GGUF metadata.</summary>
    /// <param name="model">The parsed metadata of the model.</param>
    /// <param name="modelPath">The path of the model file, used for its size on disk.</param>
    /// <returns>The facts the estimator works with.</returns>
    public static ModelFacts From(GgufModel model, string modelPath)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrEmpty(modelPath);

        string architecture = string.IsNullOrWhiteSpace(model.Architecture) ? "unknown" : model.Architecture;

        ulong blockCount = ReadUInt64(model, architecture, "block_count");
        ulong embeddingLength = ReadUInt64(model, architecture, "embedding_length");
        ulong headCount = ReadUInt64(model, architecture, "attention.head_count");
        ulong headCountKv = ReadUInt64(model, architecture, "attention.head_count_kv");
        ulong keyLength = ReadUInt64(model, architecture, "attention.key_length");
        ulong valueLength = ReadUInt64(model, architecture, "attention.value_length");
        ulong fullAttentionInterval = ReadUInt64(model, architecture, "full_attention_interval");

        // The sliding window is announced under two names depending on the architecture.
        ulong slidingWindow = ReadUInt64(model, architecture, "attention.sliding_window");
        if (slidingWindow == 0)
        {
            slidingWindow = ReadUInt64(model, architecture, "n_swa");
        }

        // The head width is the key length, then the value length, then the embedding divided
        // by the number of heads.
        ulong headDim = keyLength > 0
            ? keyLength
            : valueLength > 0
                ? valueLength
                : headCount > 0
                    ? embeddingLength / headCount
                    : 0;

        long millions = ModelSize.ParseSizeLabel(model.SizeLabel)
            ?? (model.ParameterCount is { } parameters ? (long)(parameters / 1_000_000) : 0);

        return new ModelFacts(
            architecture,
            model.SizeLabel,
            model.ParameterCount,
            millions,
            model.TensorDataBytes,
            model.VocabularyCount,
            model.HasSeparateOutputProjection,
            blockCount,
            embeddingLength,
            headCount,
            headCountKv,
            headDim,
            fullAttentionInterval,
            slidingWindow,
            model.ContextLength ?? 0,
            model.FileTypeName,
            ReadFileSize(modelPath));
    }

    /// <summary>Reads an architecture scoped metadata value, treating a missing value as zero.</summary>
    /// <param name="model">The parsed metadata of the model.</param>
    /// <param name="architecture">The architecture prefix of the key.</param>
    /// <param name="suffix">The key without the architecture prefix.</param>
    /// <returns>The value, or zero when the key is missing.</returns>
    private static ulong ReadUInt64(GgufModel model, string architecture, string suffix)
    {
        return model.GetUInt64(architecture + "." + suffix) ?? 0;
    }

    /// <summary>Reads the size of the model file, treating an unreadable file as empty.</summary>
    /// <param name="path">The path of the model file.</param>
    /// <returns>The size in bytes, or zero when the file cannot be read.</returns>
    private static long ReadFileSize(string path)
    {
        try
        {
            FileInfo info = new(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return 0;
        }
    }
}
