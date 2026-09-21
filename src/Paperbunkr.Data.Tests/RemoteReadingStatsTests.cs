using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Insights and Stats with books from remote libraries (docs/superpowers/specs/2026-09-19-remote-library-
/// sharing-design.md §8): what you *read* counts wherever the book lives; what you *own* does not include
/// someone else's library. Uses a default (local-only) context, exactly as the screens do.
/// </summary>
public class RemoteReadingStatsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_remotestats_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private int _localSeriesId, _remoteSeriesId, _localIssueId, _remoteIssueId;

    public RemoteReadingStatsTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options) { IncludeRemote = true };
        context.Database.EnsureCreated();
        var source = new RemoteSource { InstanceId = "h", DisplayName = "Den PC", Host = "h", CertFingerprint = "x" };
        context.RemoteSources.Add(source);
        context.SaveChanges();

        var local = new Series { Name = "Local Series", Publisher = "Acme" };
        var remote = new Series { Name = "Remote Series", RemoteSourceId = source.Id, RemoteSeriesId = 1, ReadingStatus = ReadingStatus.Reading };
        context.Series.AddRange(local, remote);
        context.SaveChanges();
        (_localSeriesId, _remoteSeriesId) = (local.Id, remote.Id);

        // Each series owns #1, #2, #4, #5 (so #3 is a real gap in the local one).
        var l1 = new Issue { SeriesId = local.Id, Number = "1", FilePath = "a", PageCount = 20, Writer = "Local Writer", AddedTime = Now.AddDays(-5) };
        // A real run with #3 missing: ComputeGaps needs at least three numbered issues before it will talk about a "gap".
        var localRun = new[] { "2", "4", "5" }.Select(n => new Issue { SeriesId = local.Id, Number = n, FilePath = "f" + n, PageCount = 20, AddedTime = Now.AddDays(-5) }).ToArray();
        var r1 = new Issue { SeriesId = remote.Id, Number = "1", RemoteSourceId = source.Id, RemoteIssueId = 10, PageCount = 30, Writer = "Remote Writer", AddedTime = Now.AddDays(-5), LastPageRead = 10, OpenedTime = Now.AddDays(-1) };
        var remoteRun = new[] { "2", "4", "5" }.Select((n, k) => new Issue { SeriesId = remote.Id, Number = n, RemoteSourceId = source.Id, RemoteIssueId = 20 + k, PageCount = 30, AddedTime = Now.AddDays(-5) }).ToArray();
        context.Issues.AddRange(l1);
        context.Issues.AddRange(localRun);
        context.Issues.AddRange(r1);
        context.Issues.AddRange(remoteRun);
        context.SaveChanges();
        (_localIssueId, _remoteIssueId) = (l1.Id, r1.Id);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext Ctx() => new(_options);   // default: local-only, like the Insights/Stats screens

    private void Finished(int issueId, int seriesId, int pages, int daysAgo)
    {
        using var context = Ctx();
        context.ReadingEvents.Add(new ReadingEvent
        {
            ItemType = ReadingItemType.Comic, ItemId = issueId, Kind = ReadingEventKind.Finished,
            TimestampUtc = Now.AddDays(-daysAgo), SeriesId = seriesId, PagesRead = pages,
        });
        context.SaveChanges();
    }

    [Fact]
    public void Stats_CountsAFinishedRemoteBook_InLifetimeReading()
    {
        Finished(_localIssueId, _localSeriesId, 20, 3);
        Finished(_remoteIssueId, _remoteSeriesId, 30, 2);

        using var context = Ctx();
        var snap = StatsResolver.Build(context, InsightsRange.Days90, Now);

        Assert.Equal(2, snap.Lifetime.ItemsRead);                    // the remote read counts
        Assert.Equal(50, snap.Lifetime.PagesRead);
        Assert.Equal(2, snap.Lifetime.SeriesRead);
    }

    [Fact]
    public void Stats_LibrarySizeFigures_ExcludeTheRemoteLibrary()
    {
        using var context = Ctx();
        var snap = StatsResolver.Build(context, InsightsRange.Days90, Now);

        Assert.Equal(new[] { "Local Writer" }, snap.TopAuthors.Select(a => a.Label).ToArray());   // "Remote Writer" is not in your collection
        Assert.Equal(4, snap.LibraryGrowth.Points.Count);                              // only your own four issues grew the library (the remote four did not)
        Assert.DoesNotContain(snap.Breakdown.ByReadingStatus, s => s.Label == ReadingStatus.Reading.ToString());   // the remote series' status isn't a fact about your library
    }

    [Fact]
    public void Insights_ContinueReading_OffersAStartedRemoteBook()
    {
        using var context = Ctx();
        var snap = InsightsResolver.Build(context, Now);

        var row = Assert.Single(snap.Continue, c => c.SeriesId == _remoteSeriesId);
        Assert.Equal(_remoteIssueId, row.ResumeIssueId);              // "pick up where you left off" works across libraries
    }

    [Fact]
    public void Insights_Gaps_NeverListANumberMissingFromARemoteSeries()
    {
        using var context = Ctx();
        var snap = InsightsResolver.Build(context, Now);

        Assert.Contains(snap.Gaps, g => g.SeriesId == _localSeriesId && g.MissingNumbers.Contains(3));   // your own collection: #3 is a real gap
        Assert.DoesNotContain(snap.Gaps, g => g.SeriesId == _remoteSeriesId);                             // theirs may be incomplete by design
    }
}
