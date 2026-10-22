namespace GgufLauncher.Shell;

/// <summary>Immutable snapshot of how an extension is associated on this machine.</summary>
public sealed record FileAssociationStatus
{
    /// <summary>The extension that was inspected, including its leading dot.</summary>
    public required string Extension { get; init; }

    /// <summary>The program identifier currently registered for the extension, if any.</summary>
    public string? ProgId { get; init; }

    /// <summary>The shell open command that would run for the extension, if any.</summary>
    public string? OpenCommand { get; init; }

    /// <summary>Whether the resolved program identifier belongs to this launcher.</summary>
    public required bool IsOurProgId { get; init; }

    /// <summary>The registry location the program identifier was read from, if any.</summary>
    public string? ProgIdSource { get; init; }

    /// <summary>Renders the snapshot as text the caller can print.</summary>
    /// <returns>A multi-line summary of the association.</returns>
    public string Describe()
    {
        return string.Join(
            Environment.NewLine,
            $"Extension: {Extension}",
            $"ProgId: {ProgId ?? "(none)"}",
            $"Source: {ProgIdSource ?? "(none)"}",
            $"OpenCommand: {OpenCommand ?? "(none)"}",
            $"IsOurProgId: {IsOurProgId}");
    }
}
