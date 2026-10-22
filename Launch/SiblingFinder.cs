using System.Text.RegularExpressions;

namespace GgufLauncher.Launch;

/// <summary>
/// Finds the companion files llama.cpp expects to sit beside a model: a
/// multimodal projector (<c>mmproj-*.gguf</c>) and a speculative-decoding
/// draft model (<c>dspark-*.gguf</c>, <c>*-mtp*.gguf</c>).
/// </summary>
public static partial class SiblingFinder
{
    [GeneratedRegex(@"(^|[^a-z])mtp([^a-z]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex MtpPattern();

    /// <summary>Lists every candidate companion file found beside the model.</summary>
    public static SiblingFiles Find(string modelPath)
    {
        string full = Path.GetFullPath(modelPath);
        string folder = Path.GetDirectoryName(full) ?? string.Empty;
        if (!Directory.Exists(folder))
        {
            return SiblingFiles.None;
        }

        string[] ggufFiles;
        try
        {
            ggufFiles = Directory.GetFiles(folder, "*.gguf");
        }
        catch (IOException)
        {
            return SiblingFiles.None;
        }
        catch (UnauthorizedAccessException)
        {
            return SiblingFiles.None;
        }

        List<string> projectors = [];
        List<string> drafts = [];

        foreach (string file in ggufFiles)
        {
            if (string.Equals(file, full, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsMmprojFileName(file))
            {
                projectors.Add(file);
            }
            else if (IsDraftFileName(file))
            {
                drafts.Add(file);
            }
        }

        return new SiblingFiles(
            SelectProjector(projectors),
            SelectDraft(drafts, out string? specType),
            specType);
    }

    /// <summary>True when the file name marks a multimodal projector sidecar.</summary>
    public static bool IsMmprojFileName(string path)
    {
        return Path.GetFileName(path).Contains("mmproj", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the file name marks a speculative-decoding draft model.</summary>
    public static bool IsDraftFileName(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        return stem.Contains("dspark", StringComparison.OrdinalIgnoreCase)
            || stem.Contains("draft", StringComparison.OrdinalIgnoreCase)
            || MtpPattern().IsMatch(stem);
    }

    /// <summary>Infers the <c>--spec-type</c> strategy from a draft model's file name.</summary>
    public static string? InferSpecType(string draftPath)
    {
        string stem = Path.GetFileNameWithoutExtension(draftPath);
        if (stem.Contains("dspark", StringComparison.OrdinalIgnoreCase))
        {
            return "draft-dspark";
        }

        return MtpPattern().IsMatch(stem) ? "draft-mtp" : null;
    }

    private static string? SelectProjector(List<string> projectors)
    {
        if (projectors.Count == 0)
        {
            return null;
        }

        // The 4x projector is the sweet spot on small GPUs: finer detail than the
        // 16x default for a smaller file, so prefer it when both are present.
        return projectors
            .OrderByDescending(p => p.Contains("-4x", StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string? SelectDraft(List<string> drafts, out string? specType)
    {
        specType = null;
        if (drafts.Count == 0)
        {
            return null;
        }

        // Only a draft whose strategy we can infer is safe to wire up
        // automatically; --spec-draft-model without --spec-type is a no-op.
        List<string> inferable = drafts
            .Where(d => InferSpecType(d) is not null)
            .OrderByDescending(d => d.Contains("dspark", StringComparison.OrdinalIgnoreCase))
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (inferable.Count == 0)
        {
            return null;
        }

        specType = InferSpecType(inferable[0]);
        return inferable[0];
    }
}
