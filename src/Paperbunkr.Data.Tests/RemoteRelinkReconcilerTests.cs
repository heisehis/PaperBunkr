using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Sharing;
using Paperbunkr.Sharing.Protocol;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="RemoteRelinkReconciler"/> (docs/superpowers/specs/2026-09-19-remote-library-sharing-
/// design.md §7.1): after a host is rebuilt its ids all change; existing mirror rows - and the client-
/// local reading data on them - must follow the books, never be handed to the wrong one, and never be
/// deleted silently.
/// </summary>
public class RemoteRelinkReconcilerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_relink_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly int _sourceId;

    public RemoteRelinkReconcilerTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = Ctx();
        context.Database.EnsureCreated();
        var source = new RemoteSource { InstanceId = "old-host", DisplayName = "Den PC", Host = "h", CertFingerprint = "x" };
        context.RemoteSources.Add(source);
        context.SaveChanges();
        _sourceId = source.Id;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext Ctx() => new(_options) { IncludeRemote = true };

    private static CatalogSeriesDto S(int id, string name) => new(id, name, null, "Comic", "LeftToRight", "Ongoing", null, null, null, null);

    private static CatalogIssueDto I(int id, int seriesId, string number, string? volume = null, int? year = null, string? title = null) => new(
        id, seriesId, title ?? $"Issue {number}", number, null, volume, null, null, null, null, null, null, null, null,
        year, null, null, null, null, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null, null, null, "Unknown", null, null, Array.Empty<IssueTagDto>());

    private void SeedOld(IEnumerable<CatalogSeriesDto> series, IEnumerable<CatalogIssueDto> issues)
    {
        using var context = Ctx();
        RemoteMirrorSync.Apply(context, _sourceId, series.ToList(), issues.ToList());
    }

    private RelinkResult Relink(IEnumerable<CatalogSeriesDto> series, IEnumerable<CatalogIssueDto> issues)
    {
        using var context = Ctx();
        return RemoteRelinkReconciler.Reconcile(context, _sourceId, series.ToList(), issues.ToList());
    }

    private void MarkRead(string title, int page)
    {
        using var context = Ctx();
        var issue = context.Issues.Single(i => i.Title == title);
        issue.LastPageRead = page;
        issue.Rating = 5;
        context.SaveChanges();
    }

    [Fact]
    public void RebuiltHost_WithCompletelyNewIds_KeepsEveryRowAndItsReadingState()
    {
        SeedOld(new[] { S(1, "Saga"), S(2, "Paper Girls") },
                new[] { I(10, 1, "1"), I(11, 1, "2"), I(20, 2, "1") });
        MarkRead("Issue 2", 17);

        // The host was rebuilt: every id is different.
        var freshSeries = new[] { S(500, "Saga"), S(600, "Paper Girls") };
        var freshIssues = new[] { I(9001, 500, "1"), I(9002, 500, "2"), I(9100, 600, "1") };
        var result = Relink(freshSeries, freshIssues);
        using (var context = Ctx()) RemoteMirrorSync.Apply(context, _sourceId, freshSeries, freshIssues);

        Assert.Equal(2, result.SeriesMatched);
        Assert.Equal(3, result.IssuesMatched);
        Assert.Equal(0, result.IssuesOrphaned);

        using var check = Ctx();
        Assert.Equal(3, check.Issues.Count());           // no duplicates, nothing lost
        var two = check.Issues.Single(i => i.Title == "Issue 2");
        Assert.Equal(9002, two.RemoteIssueId);            // re-keyed to the new host id
        Assert.Equal(17, two.LastPageRead);               // progress followed the book
        Assert.Equal(5f, two.Rating);
    }

    [Fact]
    public void SeriesNames_MatchThroughTheCanonicalFold()
    {
        SeedOld(new[] { S(1, "Batman: The Dark Knight") }, new[] { I(10, 1, "1") });

        var result = Relink(new[] { S(7, "Batman - Dark Knight") }, new[] { I(70, 7, "1") });

        Assert.Equal(1, result.SeriesMatched);
        Assert.Equal(1, result.IssuesMatched);
    }

    [Fact]
    public void AmbiguousSeries_AreLeftAlone_NotGuessed()
    {
        // Two old series that fold to the same key: no way to know which the new one continues.
        SeedOld(new[] { S(1, "The Flash"), S(2, "Flash") }, new[] { I(10, 1, "1"), I(20, 2, "1") });

        var result = Relink(new[] { S(9, "Flash") }, new[] { I(90, 9, "1") });

        Assert.Equal(0, result.SeriesMatched);
        Assert.Equal(2, result.IssuesOrphaned);
    }

    [Fact]
    public void SameNumberVariants_WithADistinguishingVolumeYear_FollowTheirOwnBook()
    {
        SeedOld(new[] { S(1, "Batman") },
                new[]
                {
                    I(10, 1, "1", volume: "1", year: 1940, title: "Golden Age #1"),
                    I(11, 1, "1", volume: "3", year: 2011, title: "New 52 #1"),
                });
        MarkRead("New 52 #1", 12);

        var fresh = new[] { I(900, 5, "1", volume: "3", year: 2011, title: "New 52 #1"), I(901, 5, "1", volume: "1", year: 1940, title: "Golden Age #1") };
        var result = Relink(new[] { S(5, "Batman") }, fresh);

        Assert.Equal(2, result.IssuesMatched);
        using var check = Ctx();
        var read = check.Issues.Single(i => i.Title == "New 52 #1");
        Assert.Equal(900, read.RemoteIssueId);
        Assert.Equal(12, read.LastPageRead);
        Assert.Null(check.Issues.Single(i => i.Title == "Golden Age #1").LastPageRead);
    }

    [Fact]
    public void SameNumberVariants_ThatCannotBeToldApart_AreOrphanedInsteadOfGuessed()
    {
        SeedOld(new[] { S(1, "Batman") }, new[] { I(10, 1, "1", title: "A"), I(11, 1, "1", title: "B") });

        var result = Relink(new[] { S(5, "Batman") }, new[] { I(900, 5, "1", title: "A"), I(901, 5, "1", title: "B") });

        Assert.Equal(0, result.IssuesMatched);
        Assert.Equal(2, result.IssuesOrphaned);
    }

    [Fact]
    public void IssuesWithNoCounterpart_AreKeptAsOrphans_NotDeleted_AndInvisibleToTheNextSync()
    {
        SeedOld(new[] { S(1, "Saga") }, new[] { I(10, 1, "1"), I(11, 1, "2") });
        MarkRead("Issue 2", 9);

        // The rebuilt host no longer shares issue 2.
        var freshSeries = new[] { S(500, "Saga") };
        var freshIssues = new[] { I(9001, 500, "1") };
        var result = Relink(freshSeries, freshIssues);
        using (var context = Ctx()) RemoteMirrorSync.Apply(context, _sourceId, freshSeries, freshIssues);

        Assert.Equal(1, result.IssuesOrphaned);
        using var check = Ctx();
        var orphan = check.Issues.Single(i => i.Title == "Issue 2");
        Assert.Null(orphan.RemoteIssueId);                // keyless: cannot be fetched, cannot collide
        Assert.Equal(9, orphan.LastPageRead);             // the user's data is still there
        Assert.Equal(2, check.Issues.Count());
    }

    [Fact]
    public void NewIdsThatCollideWithOldOnes_DoNotTripTheUniqueIndex()
    {
        // Old ids 10/11 swap meaning: the new host's issue 10 is the old issue 11 and vice versa.
        SeedOld(new[] { S(1, "Saga") }, new[] { I(10, 1, "1"), I(11, 1, "2") });

        var result = Relink(new[] { S(1, "Saga") }, new[] { I(10, 1, "2"), I(11, 1, "1") });

        Assert.Equal(2, result.IssuesMatched);
        using var check = Ctx();
        Assert.Equal(11, check.Issues.Single(i => i.Title == "Issue 1").RemoteIssueId);
        Assert.Equal(10, check.Issues.Single(i => i.Title == "Issue 2").RemoteIssueId);
    }

    [Fact]
    public void OneShotsWithoutNumbers_MatchByTitle()
    {
        SeedOld(new[] { S(1, "Maus") }, new[] { I(10, 1, "", title: "Maus I") });

        var result = Relink(new[] { S(3, "Maus") }, new[] { I(30, 3, "", title: "Maus I") });

        Assert.Equal(1, result.IssuesMatched);
    }

    [Fact]
    public void ADefaultContext_IsRefused()
    {
        using var context = new PaperbunkrDbContext(_options) { IncludeRemote = false };

        Assert.Throws<InvalidOperationException>(() =>
            RemoteRelinkReconciler.Reconcile(context, _sourceId, Array.Empty<CatalogSeriesDto>(), Array.Empty<CatalogIssueDto>()));
    }
}
