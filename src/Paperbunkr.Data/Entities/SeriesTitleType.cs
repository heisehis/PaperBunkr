namespace Paperbunkr.Data.Entities;

/// <summary>
/// What kind of alternate title a <see cref="SeriesTitle"/> is - deliberately provider-neutral
/// (docs/superpowers/specs/2026-08-19-metadata-model-multi-value-titles-design.md), not AniList's
/// own romaji/english/native vocabulary, per this codebase's standing rule that provider terminology
/// must be mapped, never copied directly into the canonical model.
/// </summary>
public enum SeriesTitleType
{
    /// <summary>Original-script title (e.g. AniList's <c>native</c> - Japanese/Korean/Chinese script).</summary>
    Native,

    /// <summary>Latin-alphabet transliteration of the native title (e.g. AniList's <c>romaji</c>).</summary>
    Romanized,

    /// <summary>An official localized/translated title in another language (e.g. AniList's <c>english</c>).</summary>
    Localized,
}
