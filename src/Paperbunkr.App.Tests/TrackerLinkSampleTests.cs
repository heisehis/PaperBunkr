using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="TrackerLinkSample.SupportsScore"/>/<see cref="TrackerLinkSample.SupportsFinishDate"/> -
/// docs/superpowers/specs/2026-09-18-per-tracker-score-and-finish-date-design.md's verified
/// per-tracker capability table.
/// </summary>
public class TrackerLinkSampleTests
{
    private static TrackerLinkSample Link(TrackingService service) => new() { Service = service, ExternalId = "1" };

    [Theory]
    [InlineData(TrackingService.AniList)]
    [InlineData(TrackingService.MyAnimeList)]
    [InlineData(TrackingService.Shikimori)]
    [InlineData(TrackingService.Bangumi)]
    [InlineData(TrackingService.MangaBaka)]
    [InlineData(TrackingService.MangaUpdates)]
    [InlineData(TrackingService.Kitsu)]
    [InlineData(TrackingService.MangaDex)]
    public void SupportsScore_TrueForEveryTracker(TrackingService service)
    {
        Assert.True(Link(service).SupportsScore);
    }

    [Theory]
    [InlineData(TrackingService.AniList, true)]
    [InlineData(TrackingService.MyAnimeList, true)]
    [InlineData(TrackingService.MangaBaka, true)]
    [InlineData(TrackingService.Kitsu, true)]
    [InlineData(TrackingService.Shikimori, false)]
    [InlineData(TrackingService.Bangumi, false)]
    [InlineData(TrackingService.MangaUpdates, false)]
    [InlineData(TrackingService.MangaDex, false)]
    public void SupportsFinishDate_TrueOnlyForTheFourConfirmedServices(TrackingService service, bool expected)
    {
        Assert.Equal(expected, Link(service).SupportsFinishDate);
    }
}
