namespace Paperbunkr.Data.Entities;

/// <summary>
/// External metadata/tracking source a <see cref="TrackingLink"/> points at. See
/// docs/onboarding.md §7 and §9 for how these are used (content-type inference, chapter/volume
/// sync).
/// </summary>
public enum TrackingService
{
    AniList,
    MangaUpdates,
    MyAnimeList,
    Kitsu,
    Metron,
    ComicVine,
    Shikimori,
    Bangumi,
    MangaBaka
}
