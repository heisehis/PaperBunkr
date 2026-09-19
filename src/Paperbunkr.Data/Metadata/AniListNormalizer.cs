using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// AniList DTO -&gt; canonical <see cref="IMetadataProvider"/> model mapping (§56's "provider DTO -&gt;
/// normalizer -&gt; canonical model" pipeline), kept separate from <see cref="AniListMetadataProvider"/>'s
/// HTTP mechanics so the mapping itself is unit-testable against literal DTOs with no network involved.
/// </summary>
internal static class AniListNormalizer
{
    public static MetadataSearchResult ToSearchResult(AniListMediaDto media) => new(
        ExternalId: media.Id.ToString(),
        Title: PreferredTitle(media.Title),
        Url: media.SiteUrl);

    public static ExternalMediaMetadata ToMediaMetadata(AniListMediaDto media) => new(
        ExternalId: media.Id.ToString(),
        Title: PreferredTitle(media.Title),
        Url: media.SiteUrl,
        Description: media.Description,
        Status: media.Status,
        ChapterCount: media.Chapters,
        VolumeCount: media.Volumes,
        TitleEnglish: media.Title?.English,
        TitleRomaji: media.Title?.Romaji,
        TitleNative: media.Title?.Native,
        Genre: media.Genres is { Count: > 0 } genres ? string.Join(", ", genres) : null,
        CoverImageUrl: media.CoverImage?.Large,
        Creator: ResolveCreator(media.Staff),
        PublicationYear: media.StartDate?.Year,
        PublicationFormat: media.Format,
        GenreTags: media.Genres is { Count: > 0 } genreTags ? genreTags : null,
        OtherTags: ResolveOtherTags(media.Tags));

    /// <summary>English title when AniList has one, romaji otherwise - AniList's own MediaTitle
    /// object doesn't guarantee an English title exists, but every manga entry has a romaji one.</summary>
    private static string PreferredTitle(AniListTitleDto? title) =>
        title?.English ?? title?.Romaji ?? string.Empty;

    /// <summary>Dedup join of every staff member's name regardless of their freeform <c>role</c>
    /// text - AniList has no reliable story/art role split to key off (docs/superpowers/specs/
    /// 2026-09-18-external-metadata-full-extraction-design.md §3).</summary>
    private static string? ResolveCreator(AniListStaffConnectionDto? staff)
    {
        var names = staff?.Edges?
            .Select(e => e.Node?.Name?.Full)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names is { Count: > 0 } ? string.Join(", ", names) : null;
    }

    /// <summary>Every non-spoiler tag, uncategorized - AniList's <c>tags</c> field carries no
    /// grouping of its own (unlike MangaDex's tag groups).</summary>
    private static IReadOnlyList<(string Value, string Category)>? ResolveOtherTags(List<AniListTagDto>? tags)
    {
        var values = tags?
            .Where(t => !t.IsMediaSpoiler && !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => (t.Name!, "Uncategorized"))
            .ToList();

        return values is { Count: > 0 } ? values : null;
    }
}
