using System.Globalization;

namespace GgufLauncher.AutoTune;

/// <summary>The rough size of a model, derived from its parameter count.</summary>
public enum ModelSizeClass
{
    /// <summary>At most 2 billion parameters.</summary>
    Tiny,

    /// <summary>At most 5 billion parameters.</summary>
    Small,

    /// <summary>At most 14 billion parameters.</summary>
    Medium,

    /// <summary>At most 32 billion parameters.</summary>
    Large,

    /// <summary>At most 70 billion parameters.</summary>
    XLarge,

    /// <summary>More than 70 billion parameters.</summary>
    Huge,
}

/// <summary>
/// Turns the size label of a model into a parameter count and a parameter count into a
/// size class. The classes are the coarse buckets the launcher uses to pick sensible
/// default launch parameters.
/// </summary>
public static class ModelSize
{
    /// <summary>Upper parameter count, in millions, of the <see cref="ModelSizeClass.Tiny"/> class.</summary>
    private const long TinyLimit = 2_000;

    /// <summary>Upper parameter count, in millions, of the <see cref="ModelSizeClass.Small"/> class.</summary>
    private const long SmallLimit = 5_000;

    /// <summary>Upper parameter count, in millions, of the <see cref="ModelSizeClass.Medium"/> class.</summary>
    private const long MediumLimit = 14_000;

    /// <summary>Upper parameter count, in millions, of the <see cref="ModelSizeClass.Large"/> class.</summary>
    private const long LargeLimit = 32_000;

    /// <summary>Upper parameter count, in millions, of the <see cref="ModelSizeClass.XLarge"/> class.</summary>
    private const long XLargeLimit = 70_000;

    /// <summary>
    /// Parses a size label such as <c>9B</c>, <c>1.0B</c>, <c>444M</c> or <c>27B</c> into a
    /// parameter count in millions.
    /// </summary>
    /// <param name="label">The label to parse. May be null or blank.</param>
    /// <returns>
    /// The parameter count in millions, or <see langword="null"/> when the label carries no
    /// usable number or no unit.
    /// </returns>
    public static long? ParseSizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        string text = label.Trim();
        char unit = char.ToUpperInvariant(text[^1]);
        if (unit is not ('B' or 'M'))
        {
            // A bare number is not a size label, so it cannot be trusted.
            return null;
        }

        string number = text[..^1].Trim();
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return null;
        }

        if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            return null;
        }

        double millions = unit == 'B' ? value * 1000d : value;
        return (long)Math.Round(millions, MidpointRounding.AwayFromZero);
    }

    /// <summary>Maps a parameter count in millions to a size class.</summary>
    /// <param name="millions">The parameter count in millions, for example 9000 for a 9B model.</param>
    /// <returns>The size class the parameter count falls into.</returns>
    public static ModelSizeClass Classify(long millions) => millions switch
    {
        <= TinyLimit => ModelSizeClass.Tiny,
        <= SmallLimit => ModelSizeClass.Small,
        <= MediumLimit => ModelSizeClass.Medium,
        <= LargeLimit => ModelSizeClass.Large,
        <= XLargeLimit => ModelSizeClass.XLarge,
        _ => ModelSizeClass.Huge,
    };
}
