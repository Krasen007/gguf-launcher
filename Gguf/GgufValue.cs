using System.Globalization;

namespace GgufLauncher.Gguf;

/// <summary>
/// A single typed GGUF metadata value together with the storage type it was read from.
/// </summary>
/// <remarks>
/// The payload is kept boxed so that one struct can hold every value type without a large
/// discriminated union, but the <c>TryGet</c> accessors coerce between widths: the format allows
/// a model to store, for example, a count either as <c>uint32</c> or as <c>uint64</c>, and readers
/// are expected to accept both. Negative values are never silently wrapped into unsigned
/// numbers - they are reported as a failed conversion instead.
/// </remarks>
/// <param name="Type">The wire type the value was stored as.</param>
/// <param name="Value">The boxed payload, or <see langword="null"/> for an absent value.</param>
public readonly record struct GgufValue(GgufValueType Type, object? Value)
{
    /// <summary>The longest text rendered by <see cref="Display"/> before it is elided.</summary>
    private const int MaxDisplayTextLength = 120;

    /// <summary>
    /// Attempts to read the value as an unsigned integer, accepting every integer width the
    /// format allows.
    /// </summary>
    /// <param name="result">The converted value, or zero when the conversion fails.</param>
    /// <returns><see langword="true"/> when the payload is a non-negative integer.</returns>
    public bool TryGetUInt64(out ulong result)
    {
        result = 0;
        switch (Value)
        {
            case byte v:
                result = v;
                return true;
            case sbyte v:
                return TryAssignSigned(v, out result);
            case ushort v:
                result = v;
                return true;
            case short v:
                return TryAssignSigned(v, out result);
            case uint v:
                result = v;
                return true;
            case int v:
                return TryAssignSigned(v, out result);
            case ulong v:
                result = v;
                return true;
            case long v:
                return TryAssignSigned(v, out result);
            default:
                return false;
        }
    }

    /// <summary>Attempts to read the value as a boolean.</summary>
    /// <param name="result">The converted value, or <see langword="false"/> when it fails.</param>
    /// <returns><see langword="true"/> when the payload was stored as a boolean.</returns>
    public bool TryGetBool(out bool result)
    {
        result = false;
        if (Value is bool flag)
        {
            result = flag;
            return true;
        }

        return false;
    }

    /// <summary>Attempts to read the value as a string.</summary>
    /// <param name="result">The converted value, or an empty string when it fails.</param>
    /// <returns><see langword="true"/> when the payload was stored as a string.</returns>
    public bool TryGetString(out string result)
    {
        result = string.Empty;
        if (Value is string text)
        {
            result = text;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the elements of an array value.
    /// </summary>
    /// <returns>
    /// The array elements, or an empty list when this value is not an array. The returned list is
    /// a read-only view; it is never a copy of the underlying data.
    /// </returns>
    public IReadOnlyList<GgufValue> AsArray()
    {
        if (Type == GgufValueType.Array && Value is GgufValue[] items)
        {
            return items;
        }

        return Array.Empty<GgufValue>();
    }

    /// <summary>
    /// Gets a short human readable rendering of the value, suitable for log lines and dialogs.
    /// </summary>
    /// <remarks>
    /// Large arrays are summarised as a count instead of being dumped, because a vocabulary array
    /// holds hundreds of thousands of entries, and long strings are elided.
    /// </remarks>
    public string Display
    {
        get
        {
            object? value = Value;
            if (value is null)
            {
                return "null";
            }

            if (Type == GgufValueType.Array)
            {
                return $"[array of {AsArray().Count}]";
            }

            if (value is string text)
            {
                return Elide(text);
            }

            if (value is bool flag)
            {
                return flag ? "true" : "false";
            }

            if (value is float single)
            {
                return single.ToString("G9", CultureInfo.InvariantCulture);
            }

            if (value is double @double)
            {
                return @double.ToString("G17", CultureInfo.InvariantCulture);
            }

            if (value is byte or sbyte or short or ushort or int or uint or long or ulong)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
            }

            return Elide(value.ToString() ?? string.Empty);
        }
    }

    /// <summary>
    /// Converts a signed integer to an unsigned one, refusing negative values instead of
    /// wrapping them around.
    /// </summary>
    /// <param name="value">The signed value to convert.</param>
    /// <param name="result">The converted value, or zero when the value is negative.</param>
    /// <returns><see langword="true"/> when the value was non-negative.</returns>
    private static bool TryAssignSigned(long value, out ulong result)
    {
        if (value < 0)
        {
            result = 0;
            return false;
        }

        result = (ulong)value;
        return true;
    }

    /// <summary>Shortens text that is too long to be shown in a single line.</summary>
    /// <param name="text">The text to shorten.</param>
    /// <returns>The original text, or a prefix followed by an ellipsis.</returns>
    private static string Elide(string text)
    {
        if (text.Length <= MaxDisplayTextLength)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, MaxDisplayTextLength - 3), "...");
    }
}
