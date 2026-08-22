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
        VolumeCount: media.Volumes);

    /// <summary>English title when AniList has one, romaji otherwise - AniList's own MediaTitle
    /// object doesn't guarantee an English title exists, but every manga entry has a romaji one.</summary>
    private static string PreferredTitle(AniListTitleDto? title) =>
        title?.English ?? title?.Romaji ?? string.Empty;
}
