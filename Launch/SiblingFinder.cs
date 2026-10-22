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
            // Only match "mtp" as a draft model indicator when it's a standalone token,
            // not when it's embedded in a model name like "Mythos-MTP".
            // The key distinction: in draft models, "mtp" is a spec type indicator,
            // while in model names it's part of the architecture/model identifier.
            || IsMtpAsDraftIndicator(stem);
    }

    /// <summary>
    /// Checks if "mtp" appears as a distinct token indicating a draft model for speculative decoding.
    /// Draft models typically have "mtp" as a separate component surrounded by hyphens
    /// (e.g., "model-mtp.gguf" or "-mtp-" pattern), NOT embedded in model names
    /// like "Mythos-MTP" or "Qwythos-9B-MTP-...".
    /// </summary>
    private static bool IsMtpAsDraftIndicator(string stem)
    {
        // Check for "dspark" which is the primary draft model indicator
        if (stem.Contains("dspark", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Count hyphens in the stem to help distinguish model names from draft model indicators
        int hyphenCount = 0;
        for (int i = 0; i < stem.Length; i++)
        {
            if (stem[i] == '-') hyphenCount++;
        }

        // Check for the conventional "*-mtp*.gguf" pattern where "mtp" is a distinct token.
        // The key distinction: in draft models, "mtp" is a standalone token,
        // while in model names it's embedded within architecture/model identifiers.

        for (int i = 0; i <= stem.Length - 4; i++)
        {
            // Check if the next 3 characters (case-insensitive) equal "mtp"
            string substring = stem.Substring(i, 3);
            if (substring.Equals("mtp", StringComparison.OrdinalIgnoreCase))
            {
                // Check the character before "mtp" (if any)
                bool precededByHyphen = i > 0 && stem[i - 1] == '-';
                // Check the character after "mtp" (if any)
                bool followedByHyphen = i + 3 < stem.Length && stem[i + 3] == '-';

                // Pattern 1: "mtp" at the very start of the stem (e.g., "mtp-model.gguf")
                bool atStart = i == 0;

                // Pattern 2: "mtp" at the very end of the stem (e.g., "model-mtp.gguf")
                // i+3 is the position after "mtp", so at end means i+3 >= stem.Length
                bool atEnd = i + 3 >= stem.Length;

                // Pattern 3: "mtp" surrounded by hyphens on both sides (e.g., "model-mtp-extra.gguf")
                // This is the most common draft model pattern
                bool surroundedByHyphens = precededByHyphen && followedByHyphen;

                // Pattern 4: "mtp" preceded by hyphen only (e.g., "-mtp" at start or "model-mtp")
                // but NOT preceded by a letter that would make it part of a word
                bool precededByHyphenOnly = precededByHyphen && !char.IsLetter(stem[i - 1]);

                // Pattern 5: "mtp" followed by hyphen only (e.g., "mtp-" or "model-mtp-")
                // but NOT followed by a letter that would make it part of a word
                bool followedByHyphenOnly = followedByHyphen && !char.IsLetter(stem[i + 3]);

                // Only match if "mtp" is a clear standalone token, not embedded in letters
                // Reject if "mtp" is directly preceded or followed by a letter (would be part of a word)
                bool precededByLetter = i > 0 && char.IsLetter(stem[i - 1]);
                bool followedByLetter = i + 3 < stem.Length && char.IsLetter(stem[i + 3]);

                // Skip if "mtp" is part of a larger word (has letters on either side)
                if (precededByLetter || followedByLetter)
                {
                    continue;
                }

                // Additional check: if "mtp" is surrounded by hyphens on BOTH sides AND there are
                // multiple hyphens in the stem (indicating it's part of a model name, not a draft model),
                // then don't match. This handles cases like "Qwythos-9B-Claude-Mythos-5-1M-MTP-Q4_K_M"
                // where "MTP" is embedded in the model name.
                bool isEmbeddedInModelName = surroundedByHyphens && hyphenCount > 2;

                // If "mtp" matches the draft-model patterns (surrounded by hyphens or at boundaries),
                // and is NOT part of a larger word, and is NOT embedded in a model name, return true
                if ((surroundedByHyphens || precededByHyphenOnly || followedByHyphenOnly || atStart || atEnd)
                    && !isEmbeddedInModelName)
                {
                    return true;
                }
            }
        }
        return false;
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
