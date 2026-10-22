using GgufLauncher.Configuration;
using GgufLauncher.Gguf;

namespace GgufLauncher.Launch;

/// <summary>
/// Turns a path to a <c>.gguf</c> file into everything needed to launch it:
/// the parsed metadata, the companion files, and any per-model config overrides.
/// </summary>
public static class ModelResolver
{
    /// <summary>Resolves the model, throwing <see cref="GgufException"/> for unreadable files.</summary>
    public static ResolvedModel Resolve(string modelPath, LauncherConfig config)
    {
        string full = Path.GetFullPath(modelPath);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Model file not found: {full}", full);
        }

        GgufModel gguf = GgufModel.Read(full);
        SiblingFiles siblings = SiblingFinder.Find(full);
        ModelOverride? overrides = config.FindOverride(full);

        string? mmproj = FirstNonEmpty(overrides?.Mmproj, config.Mmproj, siblings.Mmproj);
        string? draft = FirstNonEmpty(overrides?.SpecDraftModel, config.SpecDraftModel, siblings.DraftModel);
        string? specType = overrides?.SpecType ?? config.SpecType ?? siblings.DraftSpecType;

        // A draft model whose strategy cannot be inferred would be ignored by
        // llama.cpp, so drop it rather than pass a flag that does nothing.
        if (draft is not null && specType is null)
        {
            draft = null;
        }

        return new ResolvedModel
        {
            Path = full,
            Gguf = gguf,
            Mmproj = mmproj,
            DraftModel = draft,
            DraftSpecType = specType,
        };
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
