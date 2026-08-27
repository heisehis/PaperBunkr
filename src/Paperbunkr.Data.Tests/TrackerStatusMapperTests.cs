using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Tracking.Adapters;
using Xunit;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Direct unit tests for each tracker adapter's pure status/progress mapping function
/// (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md), mirroring
/// <c>CeLibraryMigratorTests.MapMangaField_MatchesDocsSection6Table</c>'s and this session's own
/// <c>LanguageIsoClassifierTests</c>' pattern. Explicitly covers the two documented lossy cases:
/// MyAnimeList's <c>is_rereading</c> flag and Bangumi's ReReading-collapses-into-Doing.
/// </summary>
public class TrackerStatusMapperTests
{
    [Theory]
    [InlineData(ReadingStatus.Planned, "PLANNING")]
    [InlineData(ReadingStatus.Reading, "CURRENT")]
    [InlineData(ReadingStatus.Completed, "COMPLETED")]
    [InlineData(ReadingStatus.Paused, "PAUSED")]
    [InlineData(ReadingStatus.Dropped, "DROPPED")]
    [InlineData(ReadingStatus.ReReading, "REPEATING")]
    public void AniListStatusMapper_MatchesDesignTable(ReadingStatus status, string expected)
    {
        Assert.Equal(expected, AniListStatusMapper.ToMediaListStatus(status));
    }

    [Theory]
    [InlineData(ReadingStatus.Planned, "plan_to_read", false)]
    [InlineData(ReadingStatus.Reading, "reading", false)]
    [InlineData(ReadingStatus.Completed, "completed", false)]
    [InlineData(ReadingStatus.Paused, "on_hold", false)]
    [InlineData(ReadingStatus.Dropped, "dropped", false)]
    [InlineData(ReadingStatus.ReReading, "reading", true)]
    public void MyAnimeListStatusMapper_MatchesDesignTable_IncludingIsRereadingLossyCase(ReadingStatus status, string expectedStatus, bool expectedIsRereading)
    {
        var (mappedStatus, isRereading) = MyAnimeListStatusMapper.ToListStatus(status);

        Assert.Equal(expectedStatus, mappedStatus);
        Assert.Equal(expectedIsRereading, isRereading);
    }

    [Theory]
    [InlineData(ReadingStatus.Planned, "planned")]
    [InlineData(ReadingStatus.Reading, "watching")]
    [InlineData(ReadingStatus.Completed, "completed")]
    [InlineData(ReadingStatus.Paused, "on_hold")]
    [InlineData(ReadingStatus.Dropped, "dropped")]
    [InlineData(ReadingStatus.ReReading, "rewatching")]
    public void ShikimoriStatusMapper_MatchesDesignTable(ReadingStatus status, string expected)
    {
        Assert.Equal(expected, ShikimoriStatusMapper.ToUserRateStatus(status));
    }

    [Theory]
    [InlineData(ReadingStatus.Planned, 1)]
    [InlineData(ReadingStatus.Completed, 2)]
    [InlineData(ReadingStatus.Reading, 3)]
    [InlineData(ReadingStatus.Paused, 4)]
    [InlineData(ReadingStatus.Dropped, 5)]
    [InlineData(ReadingStatus.ReReading, 3)]
    public void BangumiCollectionTypeMapper_MatchesDesignTable_IncludingReReadingCollapseLossyCase(ReadingStatus status, int expected)
    {
        Assert.Equal(expected, BangumiCollectionTypeMapper.ToCollectionType(status));
    }

    [Fact]
    public void BangumiCollectionTypeMapper_ReReadingAndReading_MapToSameValue()
    {
        // The documented lossy case, asserted explicitly rather than just via the table above:
        // Bangumi genuinely cannot distinguish these two states.
        Assert.Equal(
            BangumiCollectionTypeMapper.ToCollectionType(ReadingStatus.Reading),
            BangumiCollectionTypeMapper.ToCollectionType(ReadingStatus.ReReading));
    }
}
