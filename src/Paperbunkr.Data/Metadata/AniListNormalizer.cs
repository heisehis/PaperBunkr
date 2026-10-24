using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

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
        OtherTags: ResolveOtherTags(media.Tags),
        CountryOfOrigin: media.CountryOfOrigin,
        CrossReferences: media.IdMal is int mal and > 0 ? [(ExternalMetadataProvider.MyAnimeList, mal.ToString())] : null,
        WeightedTags: ResolveWeightedTags(media),
        Authors: StaffNames(media.Staff, story: true),
        Artists: StaffNames(media.Staff, story: false),
        ContentRating: ResolveContentRating(media),
        Rating: media.AverageScore is int score and > 0 ? score : null,
        AlternateTitles: media.Synonyms is { Count: > 0 } syn ? syn.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : null,
        PublicationStart: PublicationDate(media.StartDate),
        PublicationEnd: PublicationDate(media.EndDate),
        Popularity: media.Popularity is >= 0 ? media.Popularity : null);

    private static ProviderPublicationDate? PublicationDate(AniListFuzzyDateDto? date) =>
        date is null || (date.Year is null && date.Month is null && date.Day is null)
            ? null : new(date.Year, date.Month, date.Day);

    /// <summary>AniList ranks a tag 0-100 by how well it fits; genres carry no rank, so they are listed as unweighted.</summary>
    internal static IssueTagWeight WeightFromRank(int? rank) => rank switch
    {
        >= 85 => IssueTagWeight.Core,
        >= 65 => IssueTagWeight.Defining,
        >= 40 => IssueTagWeight.Recurrent,
        > 0 => IssueTagWeight.Incidental,
        _ => IssueTagWeight.Unset,
    };

    private static IReadOnlyList<ProviderTag>? ResolveWeightedTags(AniListMediaDto media)
    {
        var tags = new List<ProviderTag>();
        foreach (string genre in media.Genres ?? [])
        {
            if (!string.IsNullOrWhiteSpace(genre))
            {
                tags.Add(new ProviderTag(genre, "Genre", IssueTagWeight.Unset, IsGenre: true, IsSpoiler: false));
            }
        }

        foreach (var tag in media.Tags ?? [])
        {
            if (!string.IsNullOrWhiteSpace(tag.Name))
            {
                string category = string.IsNullOrWhiteSpace(tag.Category) ? "Uncategorized" : tag.Category!.Split('-')[0].Trim();
                tags.Add(new ProviderTag(tag.Name!, category, WeightFromRank(tag.Rank), IsGenre: false, tag.IsMediaSpoiler));
            }
        }

        return tags.Count > 0 ? tags : null;
    }

    /// <summary>The freeform role decides: "Story" and "Original" are authors, "Art" and "Illustration" are artists, "Story &amp; Art" is both; anything else (translation, lettering) is neither.</summary>
    private static IReadOnlyList<string>? StaffNames(AniListStaffConnectionDto? staff, bool story)
    {
        var names = staff?.Edges?
            .Where(e => e.Role is { Length: > 0 } role && (story
                ? role.Contains("Story", StringComparison.OrdinalIgnoreCase) || role.Contains("Original", StringComparison.OrdinalIgnoreCase)
                : role.Contains("Art", StringComparison.OrdinalIgnoreCase) || role.Contains("Illustrat", StringComparison.OrdinalIgnoreCase)))
            .Select(e => e.Node?.Name?.Full)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return names is { Count: > 0 } ? names : null;
    }

    private static string? ResolveContentRating(AniListMediaDto media) =>
        media.IsAdult ? "adult" : media.Genres?.Contains("Ecchi", StringComparer.OrdinalIgnoreCase) == true ? "suggestive" : null;

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
