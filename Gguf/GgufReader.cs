using System.Globalization;
using System.Text;

namespace GgufLauncher.Gguf;

/// <summary>
/// Parses the header, the metadata section and the tensor directory of a GGUF v2/v3 file.
/// </summary>
/// <remarks>
/// The reader is built for the double click use case: it never loads tensor payloads, so a
/// twenty gigabyte model costs no more memory than its metadata, and every length prefix is
/// validated against the bytes that are actually left in the file before anything is allocated.
/// Every failure is reported as a <see cref="GgufException"/>.
/// </remarks>
public static class GgufReader
{
    /// <summary>The largest number of metadata entries a file may declare.</summary>
    private const ulong MaxMetadataEntries = 1_000_000;
    /// <summary>The largest byte length a single string may declare.</summary>
    private const ulong MaxStringBytes = 1_048_576;
    /// <summary>The largest number of elements a single array may declare.</summary>
    private const ulong MaxArrayElements = 1_000_000;
    /// <summary>The largest number of array elements a single file may declare in total.</summary>
    private const ulong MaxTotalArrayElements = 4_000_000;
    /// <summary>The largest number of nested array levels that are parsed.</summary>
    private const int MaxArrayDepth = 8;
    /// <summary>The largest number of dimensions a tensor may declare.</summary>
    private const uint MaxTensorDimensions = 4;
    /// <summary>The alignment used between the tensors when the file does not name one.</summary>
    private const ulong DefaultAlignment = 32;
    /// <summary>The largest alignment a file may ask for before the value is treated as broken.</summary>
    private const ulong MaxAlignment = 1_048_576;
    /// <summary>The name of the tensor that holds an output projection matrix of its own.</summary>
    private const string OutputProjectionTensorName = "output.weight";
    /// <summary>The bytes a GGUF file starts with: the ASCII text <c>GGUF</c>.</summary>
    private static readonly byte[] ExpectedMagic = { 0x47, 0x47, 0x55, 0x46 };

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

        try
        {
            using FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
            using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            ParseContext context = new ParseContext(reader);

            Header header = ReadHeader(context);
            Dictionary<string, GgufValue> metadata = ReadMetadata(context, header.MetadataEntryCount);
            TensorDirectory directory = ValidateTensorDirectory(context, header.TensorCount);

            ulong tensorDataBytes = MeasureTensorDataBytes(context, metadata);
            ulong vocabularyCount = CountVocabularyTokens(metadata);
            bool hasSeparateOutputProjection = directory.HasOutputProjection;

            return new GgufModel(metadata, tensorDataBytes, vocabularyCount, hasSeparateOutputProjection);
        }
        catch (EndOfStreamException ex)
        {
            throw new GgufException("The file ends unexpectedly; it is truncated or not a GGUF model.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new GgufException($"The file '{path}' could not be opened for reading: {ex.Message}", ex);
        }
    }

    /// <summary>Reads and validates the 24 byte file header.</summary>
    private static Header ReadHeader(ParseContext context)
    {
        byte[] magic = context.Reader.ReadBytes(4);
        if (magic.Length != 4)
        {
            throw new GgufException("The file is too small to hold a GGUF header.");
        }

        if (!magic.AsSpan().SequenceEqual(ExpectedMagic))
        {
            throw new GgufException(DescribeMagic(magic));
        }

        uint version = context.Reader.ReadUInt32();
        if (version == 1)
        {
            throw new GgufException("GGUF version 1 is not supported; export the model as GGUF version 2 or 3.");
        }

        if (version is < 2 or > 3)
        {
            throw new GgufException($"Unsupported GGUF version {version}; only version 2 and version 3 can be read.");
        }

        ulong tensorCount = context.Reader.ReadUInt64();
        ulong entryCount = context.Reader.ReadUInt64();
        long remaining = context.Remaining;

        if (entryCount > MaxMetadataEntries)
        {
            throw new GgufException($"The file declares {entryCount} metadata entries, more than the supported maximum of {MaxMetadataEntries}.");
        }

        if (entryCount > (ulong)(remaining / 2))
        {
            throw new GgufException($"The file declares {entryCount} metadata entries but only {remaining} bytes are left; the file is truncated.");
        }

        if (tensorCount > (ulong)(remaining / 8))
        {
            throw new GgufException($"The file declares {tensorCount} tensors but only {remaining} bytes are left; the file is truncated.");
        }

        return new Header((int)version, tensorCount, entryCount);
    }

    /// <summary>Reads the metadata key/value pairs in the order they appear in the file.</summary>
    private static Dictionary<string, GgufValue> ReadMetadata(ParseContext context, ulong entryCount)
    {
        Dictionary<string, GgufValue> metadata = new Dictionary<string, GgufValue>(StringComparer.Ordinal);

        for (ulong index = 0; index < entryCount; index++)
        {
            string key = ReadString(context);
            GgufValueType type = ReadValueType(context);
            metadata[key] = ReadValue(context, type, 0);
        }

        return metadata;
    }

    /// <summary>Reads a value type tag and rejects unknown codes.</summary>
    private static GgufValueType ReadValueType(ParseContext context)
    {
        uint raw = context.Reader.ReadUInt32();
        if (raw > (uint)GgufValueType.Float64)
        {
            throw new GgufException($"The file uses the unknown metadata value type {raw}; only types 0 to 12 exist.");
        }

        return (GgufValueType)raw;
    }

    /// <summary>Reads a single metadata value of the given type.</summary>
    private static GgufValue ReadValue(ParseContext context, GgufValueType type, int depth)
    {
        return type switch
        {
            GgufValueType.UInt8 => new GgufValue(type, context.Reader.ReadByte()),
            GgufValueType.Int8 => new GgufValue(type, (sbyte)context.Reader.ReadByte()),
            GgufValueType.UInt16 => new GgufValue(type, context.Reader.ReadUInt16()),
            GgufValueType.Int16 => new GgufValue(type, context.Reader.ReadInt16()),
            GgufValueType.UInt32 => new GgufValue(type, context.Reader.ReadUInt32()),
            GgufValueType.Int32 => new GgufValue(type, context.Reader.ReadInt32()),
            GgufValueType.Float32 => new GgufValue(type, context.Reader.ReadSingle()),
            GgufValueType.Bool => new GgufValue(type, ReadBool(context)),
            GgufValueType.String => new GgufValue(type, ReadString(context)),
            GgufValueType.Array => new GgufValue(type, ReadArray(context, depth + 1)),
            GgufValueType.UInt64 => new GgufValue(type, context.Reader.ReadUInt64()),
            GgufValueType.Int64 => new GgufValue(type, context.Reader.ReadInt64()),
            GgufValueType.Float64 => new GgufValue(type, context.Reader.ReadDouble()),
            _ => throw new GgufException($"The file uses the unsupported metadata value type {type}."),
        };
    }

    /// <summary>Reads an array value, including arrays that hold other arrays.</summary>
    private static GgufValue[] ReadArray(ParseContext context, int depth)
    {
        if (depth > MaxArrayDepth)
        {
            throw new GgufException($"The file nests metadata arrays deeper than {MaxArrayDepth} levels; it is refused.");
        }

        GgufValueType elementType = ReadValueType(context);
        ulong count = context.Reader.ReadUInt64();
        long remaining = context.Remaining;

        if (count > MaxArrayElements)
        {
            throw new GgufException($"A metadata array declares {count} elements, more than the supported maximum of {MaxArrayElements}.");
        }

        if (count > (ulong)remaining)
        {
            throw new GgufException($"A metadata array declares {count} elements but only {remaining} bytes are left; the file is truncated.");
        }

        context.ArrayElementsRead += count;
        if (context.ArrayElementsRead > MaxTotalArrayElements)
        {
            throw new GgufException($"The file declares more than {MaxTotalArrayElements} array elements in total; it is refused.");
        }

        GgufValue[] items = new GgufValue[(int)count];
        for (ulong index = 0; index < count; index++)
        {
            items[(int)index] = ReadValue(context, elementType, depth);
        }

        return items;
    }

    /// <summary>Reads a length prefixed UTF-8 string.</summary>
    private static string ReadString(ParseContext context)
    {
        ulong length = context.Reader.ReadUInt64();
        long remaining = context.Remaining;

        if (length > (ulong)remaining)
        {
            throw new GgufException($"A string declares {length} bytes but only {remaining} bytes are left; the file is truncated or corrupt.");
        }

        if (length > MaxStringBytes)
        {
            throw new GgufException($"A string declares {length} bytes, more than the supported maximum of {MaxStringBytes} bytes.");
        }

        byte[] bytes = context.Reader.ReadBytes((int)length);
        if ((ulong)bytes.Length != length)
        {
            throw new GgufException("The file ends unexpectedly while a string was being read.");
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Reads a boolean, rejecting values other than zero and one.</summary>
    private static bool ReadBool(ParseContext context)
    {
        byte raw = context.Reader.ReadByte();
        return raw switch
        {
            0 => false,
            1 => true,
            _ => throw new GgufException($"A boolean holds the invalid value {raw}; only 0 and 1 are allowed."),
        };
    }

    /// <summary>Reads the tensor directory between metadata and payloads to validate the file layout.</summary>
    /// <param name="context">The parse context.</param>
    /// <param name="tensorCount">The number of tensors the header declares.</param>
    /// <returns>What the tensor directory revealed about the file.</returns>
    private static TensorDirectory ValidateTensorDirectory(ParseContext context, ulong tensorCount)
    {
        bool hasSeparateOutputProjection = false;

        for (ulong index = 0; index < tensorCount; index++)
        {
            string name = ReadString(context);
            if (string.Equals(name, OutputProjectionTensorName, StringComparison.Ordinal))
            {
                hasSeparateOutputProjection = true;
            }

            uint dimensions = context.Reader.ReadUInt32();
            if (dimensions > MaxTensorDimensions)
            {
                throw new GgufException($"Tensor {index} declares {dimensions} dimensions; at most {MaxTensorDimensions} are allowed.");
            }

            for (uint dimension = 0; dimension < dimensions; dimension++)
            {
                context.Reader.ReadUInt64();
            }

            context.Reader.ReadUInt32();
            ulong offset = context.Reader.ReadUInt64();
            if (offset > (ulong)context.Remaining)
            {
                throw new GgufException($"Tensor {index} points past the end of the file; the file is truncated.");
            }
        }

        if (tensorCount > 0 && context.Remaining == 0)
        {
            throw new GgufException("The file holds no tensor data; it is truncated.");
        }

        return new TensorDirectory(hasSeparateOutputProjection);
    }

    /// <summary>Measures how many bytes of the file are taken up by the tensor payloads.</summary>
    /// <param name="context">The parse context, sitting at the end of the tensor directory.</param>
    /// <param name="metadata">The metadata of the file, which names the requested alignment.</param>
    /// <returns>The number of bytes of tensor payload, or zero when the file holds none.</returns>
    /// <remarks>
    /// A GGUF file keeps its tensors behind the tensor directory, and the first tensor starts at a
    /// file offset that is a multiple of the alignment, so the payload size is the length of the
    /// file minus that aligned offset. The gap in front of the tensors is at most the alignment
    /// wide, which keeps the result accurate to a few bytes.
    /// </remarks>
    private static ulong MeasureTensorDataBytes(ParseContext context, Dictionary<string, GgufValue> metadata)
    {
        long fileLength = context.Reader.BaseStream.Length;
        long tensorDataStart = AlignUp(context.Position, ReadAlignment(metadata));

        return fileLength > tensorDataStart ? (ulong)(fileLength - tensorDataStart) : 0;
    }

    /// <summary>Counts the entries of the tokenizer vocabulary of the model.</summary>
    /// <param name="metadata">The metadata of the file.</param>
    /// <returns>The number of vocabulary entries, or zero when the file does not say.</returns>
    /// <remarks>
    /// The vocabulary is normally stored as the array of all tokens, but some files only publish
    /// the number of entries, so those keys are used as a fallback.
    /// </remarks>
    private static ulong CountVocabularyTokens(Dictionary<string, GgufValue> metadata)
    {
        if (metadata.TryGetValue("tokenizer.ggml.tokens", out GgufValue tokens) && tokens.Type == GgufValueType.Array)
        {
            return (ulong)tokens.AsArray().Count;
        }

        if (metadata.TryGetValue("tokenizer.ggml.vocab_size", out GgufValue tokenizerSize) && tokenizerSize.TryGetUInt64(out ulong size))
        {
            return size;
        }

        if (metadata.TryGetValue("llama.vocab_size", out GgufValue llamaSize) && llamaSize.TryGetUInt64(out ulong llamaCount))
        {
            return llamaCount;
        }

        return 0;
    }

    /// <summary>Reads the alignment the file asks its tensors to use.</summary>
    /// <param name="metadata">The metadata of the file.</param>
    /// <returns>The alignment, or the default one when the file does not name a usable value.</returns>
    private static ulong ReadAlignment(Dictionary<string, GgufValue> metadata)
    {
        if (metadata.TryGetValue("general.alignment", out GgufValue value) &&
            value.TryGetUInt64(out ulong alignment) &&
            alignment > 0 &&
            alignment <= MaxAlignment)
        {
            return alignment;
        }

        return DefaultAlignment;
    }

    /// <summary>Rounds a file offset up to the next multiple of the alignment.</summary>
    /// <param name="position">The offset to round up.</param>
    /// <param name="alignment">The alignment, which is never zero.</param>
    /// <returns>The smallest offset that is a multiple of the alignment and not before the given one.</returns>
    private static long AlignUp(long position, ulong alignment)
    {
        long step = (long)alignment;
        long remainder = position % step;

        return remainder == 0 ? position : position + (step - remainder);
    }

    /// <summary>Builds a friendly message for a file that does not start with the GGUF magic.</summary>
    private static string DescribeMagic(byte[] magic)
    {
        string hex = string.Join(" ", magic.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

        if (magic[0] == 0x46 && magic[1] == 0x55 && magic[2] == 0x47 && magic[3] == 0x47)
        {
            return $"The file is not a GGUF model: it is big endian ({hex}), but GGUF files are little endian.";
        }

        return $"The file is not a GGUF model: it starts with {hex} instead of 47 47 55 46.";
    }

    /// <summary>Keeps the stream position available so the bytes left in the file stay known.</summary>
    private sealed class ParseContext(BinaryReader reader)
    {
        /// <summary>Gets the reader over the file.</summary>
        public BinaryReader Reader { get; } = reader;

        /// <summary>Gets the number of array elements read so far.</summary>
        public ulong ArrayElementsRead { get; set; }

        /// <summary>Gets the current position in the file.</summary>
        public long Position => Reader.BaseStream.Position;

        /// <summary>Gets the number of bytes that are still available in the file.</summary>
        public long Remaining => Reader.BaseStream.Length - Reader.BaseStream.Position;
    }

    /// <summary>The fields of the file header.</summary>
    private sealed record Header(int Version, ulong TensorCount, ulong MetadataEntryCount);

    /// <summary>What the tensor directory of a file revealed.</summary>
    /// <param name="HasOutputProjection">Whether the file stores an output projection matrix of its own.</param>
    private sealed record TensorDirectory(bool HasOutputProjection);
}
