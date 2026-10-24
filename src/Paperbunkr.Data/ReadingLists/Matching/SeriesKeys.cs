using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ReadingLists.Matching;

/// <summary>
/// One comparison key per series name (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.2): the name with a volume
/// marker removed and then <see cref="TitleNormalizer.StripDown"/> applied - the loosest tier of <see cref="TitleNormalizer.NamesMatch"/>,
/// as a value that can be stored and indexed. Two names are equivalent exactly when their keys are equal.
/// </summary>
public static class SeriesKeys
{
    public static string Of(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        string key = TitleNormalizer.StripDown(TitleNormalizer.StripVolume(trimmed)).ToLowerInvariant();

        // A name made only of punctuation (or only "the"/"and") strips to nothing; fall back so it still equals itself and nothing else.
        return key.Length > 0 ? key : trimmed.ToLowerInvariant();
    }
}
