namespace GgufLauncher.Launch;

/// <summary>Companion files discovered next to a model in the same folder.</summary>
/// <param name="Mmproj">Multimodal projector sidecar, or null.</param>
/// <param name="DraftModel">Speculative-decoding draft model, or null.</param>
/// <param name="DraftSpecType">Matching <c>--spec-type</c> value, or null when it cannot be inferred.</param>
public sealed record SiblingFiles(string? Mmproj, string? DraftModel, string? DraftSpecType)
{
    /// <summary>
    /// Shared "nothing was found" instance, so callers can test for the empty case
    /// without allocating a new record for every lookup.
    /// </summary>
    public static readonly SiblingFiles None = new(null, null, null);
}
