namespace GgufLauncher.Gguf;

/// <summary>
/// Translates the numeric <c>general.file_type</c> code of a GGUF file into the quantization
/// names used by llama.cpp.
/// </summary>
/// <remarks>
/// The codes are the <c>llama_ftype</c> enumeration of llama.cpp. Names are reported without the
/// <c>MOSTLY_</c> and <c>ALL_</c> prefixes so that they stay machine friendly, and unassigned
/// codes are reported as <c>Unknown (N)</c> instead of being mapped to a wrong quantization.
/// </remarks>
public static class GgufFileType
{
    /// <summary>
    /// Gets the llama.cpp quantization name of a file type code, for example <c>Q4_K_M</c>.
    /// </summary>
    /// <param name="fileType">The raw <c>general.file_type</c> value.</param>
    /// <returns>
    /// The quantization name without the enum prefix, or <c>Unknown (N)</c> when the code is not
    /// assigned by llama.cpp.
    /// </returns>
    public static string Describe(uint fileType) => fileType switch
    {
        0 => "F32",
        1 => "F16",
        2 => "Q4_0",
        3 => "Q4_1",
        7 => "Q8_0",
        8 => "Q5_0",
        9 => "Q5_1",
        10 => "Q2_K",
        11 => "Q3_K_S",
        12 => "Q3_K_M",
        13 => "Q3_K_L",
        14 => "Q4_K_S",
        15 => "Q4_K_M",
        16 => "Q5_K_S",
        17 => "Q5_K_M",
        18 => "Q6_K",
        19 => "IQ2_XXS",
        20 => "IQ2_XS",
        21 => "Q2_K_S",
        22 => "IQ3_XS",
        23 => "IQ3_XXS",
        24 => "IQ1_S",
        25 => "IQ4_NL",
        26 => "IQ3_S",
        27 => "IQ3_M",
        28 => "IQ2_S",
        29 => "IQ2_M",
        30 => "IQ4_XS",
        31 => "IQ1_M",
        32 => "BF16",
        36 => "TQ1_0",
        37 => "TQ2_0",
        38 => "MXFP4_MOE",
        39 => "NVFP4",
        40 => "Q1_0",
        41 => "Q2_0",
        _ => $"Unknown ({fileType})",
    };
}
