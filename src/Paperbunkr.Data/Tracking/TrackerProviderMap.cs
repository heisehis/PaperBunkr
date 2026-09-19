using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tracking;

/// <summary>
/// Maps a metadata source (<see cref="ExternalMetadataProvider"/>) to the tracker
/// (<see cref="TrackingService"/>) of the same name, for the seven services that exist as both -
/// an existing <see cref="ExternalMediaId"/> is then already the tracker's id for that series
/// (docs/superpowers/specs/2026-09-18-tracker-behavior-settings-design.md §3.5). Bangumi has no
/// metadata provider; AnimePlanet/GCD/LoCG have no tracker.
/// </summary>
public static class TrackerProviderMap
{
    public static TrackingService? ToTrackingService(ExternalMetadataProvider provider) => provider switch
    {
        ExternalMetadataProvider.AniList => TrackingService.AniList,
        ExternalMetadataProvider.MyAnimeList => TrackingService.MyAnimeList,
        ExternalMetadataProvider.MangaDex => TrackingService.MangaDex,
        ExternalMetadataProvider.MangaBaka => TrackingService.MangaBaka,
        ExternalMetadataProvider.MangaUpdates => TrackingService.MangaUpdates,
        ExternalMetadataProvider.Kitsu => TrackingService.Kitsu,
        ExternalMetadataProvider.Shikimori => TrackingService.Shikimori,
        _ => null,
    };

    public static ExternalMetadataProvider? ToMetadataProvider(TrackingService service) => service switch
    {
        TrackingService.AniList => ExternalMetadataProvider.AniList,
        TrackingService.MyAnimeList => ExternalMetadataProvider.MyAnimeList,
        TrackingService.MangaDex => ExternalMetadataProvider.MangaDex,
        TrackingService.MangaBaka => ExternalMetadataProvider.MangaBaka,
        TrackingService.MangaUpdates => ExternalMetadataProvider.MangaUpdates,
        TrackingService.Kitsu => ExternalMetadataProvider.Kitsu,
        TrackingService.Shikimori => ExternalMetadataProvider.Shikimori,
        _ => null,
    };
}
