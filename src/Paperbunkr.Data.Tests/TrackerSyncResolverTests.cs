using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Tracking;
using Xunit;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="TrackerSyncResolver"/>'s conflict rule and remote-apply logic in isolation -
/// no HTTP, no database, matching this codebase's "pure Data-layer logic gets exhaustive tests, the
/// ViewModel wiring around it doesn't need its own copy" precedent.
/// </summary>
public class TrackerSyncResolverTests
{
    [Theory]
    [InlineData(5, 10, true)]   // remote further along -> remote wins
    [InlineData(10, 5, false)]  // local further along -> local wins (push)
    [InlineData(null, 3, true)] // no local progress at all -> remote wins
    [InlineData(3, null, false)] // no remote progress -> local wins
    public void RemoteWins_ComparesChapterProgressFirst(int? localProgress, int? remoteProgress, bool expected)
    {
        var remote = new TrackerRemoteEntry(ReadingStatus.Reading, remoteProgress);

        bool result = TrackerSyncResolver.RemoteWins(localProgress, ReadingStatus.Reading, remote);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void RemoteWins_EqualProgress_TiebreaksOnStatusRank_RemoteHigherWins()
    {
        var remote = new TrackerRemoteEntry(ReadingStatus.Completed, 10);

        Assert.True(TrackerSyncResolver.RemoteWins(10, ReadingStatus.Reading, remote));
    }

    [Fact]
    public void RemoteWins_EqualProgress_TiebreaksOnStatusRank_LocalHigherOrEqual_LocalWins()
    {
        var remote = new TrackerRemoteEntry(ReadingStatus.Planned, 10);

        Assert.False(TrackerSyncResolver.RemoteWins(10, ReadingStatus.Reading, remote));
    }

    [Fact]
    public void RemoteWins_BothUnknownAndNoProgress_LocalWins_NeverRegressesOnAnUnknownRemote()
    {
        var remote = new TrackerRemoteEntry(ReadingStatus.Unknown, null);

        Assert.False(TrackerSyncResolver.RemoteWins(null, ReadingStatus.Unknown, remote));
    }

    [Fact]
    public void ApplyRemote_AdoptsRemoteStatus()
    {
        var series = new Series { Name = "Test" };
        var remote = new TrackerRemoteEntry(ReadingStatus.Completed, null);

        TrackerSyncResolver.ApplyRemote(series, remote);

        Assert.Equal(ReadingStatus.Completed, series.ReadingStatus);
    }

    [Fact]
    public void ApplyRemote_MarksIssuesReadUpToRemoteProgress_SkipsAlreadyReadAndBeyondProgress()
    {
        var issue1 = new Issue { Number = "1", PageCount = 20, LastPageRead = 0 };
        var issue2 = new Issue { Number = "2", PageCount = 20, LastPageRead = 0 };
        var issue3 = new Issue { Number = "3", PageCount = 20, LastPageRead = 0 };
        var series = new Series { Name = "Test", Issues = { issue1, issue2, issue3 } };
        var remote = new TrackerRemoteEntry(ReadingStatus.Reading, 2);

        var newlyRead = TrackerSyncResolver.ApplyRemote(series, remote);

        Assert.True(issue1.HasBeenRead());
        Assert.True(issue2.HasBeenRead());
        Assert.False(issue3.HasBeenRead());
        Assert.Equal(2, newlyRead.Count);
        Assert.Contains(issue1, newlyRead);
        Assert.Contains(issue2, newlyRead);
    }

    [Fact]
    public void ApplyRemote_SkipsIssuesAlreadyRead_NotReturnedAsNewlyRead()
    {
        var alreadyRead = new Issue { Number = "1", PageCount = 20, LastPageRead = 19 };
        var series = new Series { Name = "Test", Issues = { alreadyRead } };
        var remote = new TrackerRemoteEntry(ReadingStatus.Reading, 5);

        var newlyRead = TrackerSyncResolver.ApplyRemote(series, remote);

        Assert.Empty(newlyRead);
    }

    [Fact]
    public void ApplyRemote_UnscannedIssueWithUnknownPageCount_SilentlySkipped_NotInNewlyRead()
    {
        var unscanned = new Issue { Number = "1", PageCount = null };
        var series = new Series { Name = "Test", Issues = { unscanned } };
        var remote = new TrackerRemoteEntry(ReadingStatus.Reading, 5);

        var newlyRead = TrackerSyncResolver.ApplyRemote(series, remote);

        Assert.Empty(newlyRead);
        Assert.False(unscanned.HasBeenRead());
    }

    [Fact]
    public void ApplyRemote_NoChapterProgress_OnlyUpdatesStatus_TouchesNoIssues()
    {
        var issue = new Issue { Number = "1", PageCount = 20, LastPageRead = 0 };
        var series = new Series { Name = "Test", Issues = { issue } };
        var remote = new TrackerRemoteEntry(ReadingStatus.Planned, null);

        var newlyRead = TrackerSyncResolver.ApplyRemote(series, remote);

        Assert.Empty(newlyRead);
        Assert.False(issue.HasBeenRead());
        Assert.Equal(ReadingStatus.Planned, series.ReadingStatus);
    }
}
