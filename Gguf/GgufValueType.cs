namespace GgufLauncher.Gguf;

/// <summary>
/// The type tag of a single GGUF metadata value, matching the <c>gguf_type</c> codes of the
/// GGUF v2/v3 specification.
/// </summary>
/// <remarks>
/// The underlying numbers are part of the on-disk format: they are written verbatim and must
/// never be renumbered. Unknown codes are rejected by the reader instead of being guessed.
/// </remarks>
public enum GgufValueType
{
    /// <summary>An unsigned 8-bit integer.</summary>
    UInt8 = 0,

    /// <summary>A signed 8-bit integer stored in two's complement.</summary>
    Int8 = 1,

    /// <summary>An unsigned 16-bit little-endian integer.</summary>
    UInt16 = 2,

    /// <summary>A signed 16-bit little-endian integer stored in two's complement.</summary>
    Int16 = 3,

    /// <summary>An unsigned 32-bit little-endian integer.</summary>
    UInt32 = 4,

    /// <summary>A signed 32-bit little-endian integer stored in two's complement.</summary>
    Int32 = 5,

    /// <summary>A 32-bit IEEE-754 floating point value.</summary>
    Float32 = 6,

    /// <summary>A boolean stored as one byte, where 0 is false and 1 is true.</summary>
    Bool = 7,

    /// <summary>A UTF-8 string prefixed with its length in bytes (not characters).</summary>
    String = 8,

    /// <summary>An array whose elements all share one element type.</summary>
    Array = 9,

    /// <summary>An unsigned 64-bit little-endian integer.</summary>
    UInt64 = 10,

    /// <summary>A signed 64-bit little-endian integer stored in two's complement.</summary>
    Int64 = 11,

    /// <summary>A 64-bit IEEE-754 floating point value.</summary>
    Float64 = 12,
}
