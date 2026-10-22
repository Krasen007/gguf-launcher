using GgufLauncher.Gguf;

namespace GgufLauncher.Launch;

/// <summary>A model file plus everything the launcher worked out about it.</summary>
public sealed record ResolvedModel
{
    /// <summary>Full path of the model file the launcher was asked to serve.</summary>
    public required string Path { get; init; }

    /// <summary>Metadata read from the GGUF header, the source of every derived flag.</summary>
    public required GgufModel Gguf { get; init; }

    /// <summary>Multimodal projector to pass via <c>--mmproj</c>, when one applies.</summary>
    public string? Mmproj { get; init; }

    /// <summary>Speculative-decoding draft model to pass via <c>--spec-draft-model</c>, when one applies.</summary>
    public string? DraftModel { get; init; }

    /// <summary>Strategy for the draft model, e.g. <c>draft-dspark</c>.</summary>
    public string? DraftSpecType { get; init; }

    /// <summary>True when the clicked file is a projector sidecar rather than a servable model.</summary>
    public bool IsProjector => Gguf.IsProjector || IsProjectorMetadata();

    /// <summary>True when the clicked file looks like a draft model rather than a servable model.</summary>
    public bool IsDraftOnly => SiblingFinder.IsDraftFileName(Path);

    /// <summary>The <c>general.type</c> of the file, for example <c>model</c> or <c>mmproj</c>.</summary>
    public string? Type => Gguf.GetString("general.type");

    /// <summary>
    /// Projector sidecars announce themselves in two ways: they carry a <c>general.type</c> of
    /// <c>mmproj</c>, and their architecture is the literal <c>clip</c>. Both are checked because
    /// a file name alone cannot be trusted.
    /// </summary>
    private bool IsProjectorMetadata()
    {
        return string.Equals(Type, "mmproj", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Gguf.Architecture, "clip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Best available human label for the model.</summary>
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Gguf.Name) is false)
            {
                return Gguf.Name;
            }

            if (string.IsNullOrWhiteSpace(Gguf.SizeLabel) is false && string.IsNullOrWhiteSpace(Gguf.Architecture) is false)
            {
                return $"{Gguf.SizeLabel} ({Gguf.Architecture})";
            }

            return System.IO.Path.GetFileNameWithoutExtension(Path);
        }
    }

    /// <summary>Short one-line summary of the model for the console banner.</summary>
    public string Summary
    {
        get
        {
            List<string> parts = [];

            if (string.IsNullOrWhiteSpace(Gguf.Architecture) is false)
            {
                parts.Add(Gguf.Architecture);
            }

            if (string.IsNullOrWhiteSpace(Gguf.FileTypeName) is false)
            {
                parts.Add(Gguf.FileTypeName);
            }

            if (Gguf.ParameterCount is { } parameters)
            {
                parts.Add(FormatParameterCount(parameters));
            }
            else if (string.IsNullOrWhiteSpace(Gguf.SizeLabel) is false)
            {
                parts.Add(Gguf.SizeLabel);
            }

            if (Gguf.ContextLength is { } ctx)
            {
                parts.Add($"{ctx / 1024}K ctx");
            }

            if (Gguf.IsMultimodal)
            {
                parts.Add("vision");
            }

            return parts.Count > 0 ? string.Join(" · ", parts) : "unknown model";
        }
    }

    private static string FormatParameterCount(ulong parameters)
    {
        double billions = parameters / 1_000_000_000d;
        return billions >= 1d
            ? $"{billions:0.##}B params"
            : $"{parameters / 1_000_000d:0.#}M params";
    }
}
