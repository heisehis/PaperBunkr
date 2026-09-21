using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Sharing;
using Paperbunkr.Sharing.Protocol;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="RemoteMirrorSync"/> (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md
/// §7.1): idempotent upsert keyed by the host's ids, client-local reading state preserved across
/// re-syncs, removal of what the host stopped sharing, and no leak into local data.
/// </summary>
public class RemoteMirrorSyncTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mirror_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly int _sourceId;

    public RemoteMirrorSyncTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = Ctx();
        context.Database.EnsureCreated();
        var source = new RemoteSource { InstanceId = "host-1", DisplayName = "Den PC", Host = "h", CertFingerprint = "x" };
        context.RemoteSources.Add(source);
        context.SaveChanges();
        _sourceId = source.Id;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext Ctx(bool includeRemote = true) => new(_options) { IncludeRemote = includeRemote };

    private static CatalogSeriesDto Series(int id, string name = "Saga", string reading = "RightToLeft") =>
        new(id, name, null, "Manga", reading, "Ongoing", "Image", "Sci-Fi", "sum", "Vaughan");

    private static CatalogIssueDto Issue(int id, int seriesId, string title, params IssueTagDto[] tags) => new(
        id, seriesId, title, id.ToString(), 54, "1", null, null, null, "Arc", null, null, false, "summary",
        2012, 3, 14, "BKV", null, null, null, null, null, null, null, "Image", null, null,
        22, "en", "Series", "Teen", "Alana", null, null, null, 4.5f, "978", "Color", new DateTime(2012, 3, 14), 0.65, tags);

    private MirrorSyncResult Sync(IEnumerable<CatalogSeriesDto> series, IEnumerable<CatalogIssueDto> issues)
    {
        using var context = Ctx();
        return RemoteMirrorSync.Apply(context, _sourceId, series.ToList(), issues.ToList());
    }

    [Fact]
    public void FirstSync_CreatesMirrorRows_WithNoLocalFilePath_AndMappedFields()
    {
        var result = Sync(new[] { Series(5) }, new[] { Issue(50, 5, "Chapter One", new IssueTagDto("Tags", "Theme", "Core", "Space")) });

        Assert.Equal(1, result.SeriesAdded);
        Assert.Equal(1, result.IssuesAdded);

        using var context = Ctx();
        var series = context.Series.Single();
        var issue = context.Issues.Include(i => i.Tags).Single();
        Assert.Equal(_sourceId, series.RemoteSourceId);
        Assert.Equal(5, series.RemoteSeriesId);
        Assert.Equal(ReadingMode.RightToLeft, series.ReadingMode);
        Assert.Equal(ContentType.Manga, series.ContentType);
        Assert.Equal(_sourceId, issue.RemoteSourceId);
        Assert.Equal(50, issue.RemoteIssueId);
        Assert.Null(issue.FilePath);
        Assert.Equal("Chapter One", issue.Title);
        Assert.Equal(series.Id, issue.SeriesId);
        Assert.Equal(ColorMode.Color, issue.ColorMode);
        Assert.Equal(0.65, issue.CoverAspectRatio);
        Assert.Equal("Alana", issue.Characters);
        var tag = Assert.Single(issue.Tags);
        Assert.Equal((IssueTagField.Tags, "Theme", IssueTagWeight.Core, "Space"), (tag.Field, tag.Category, tag.Weight, tag.Value));
        Assert.Equal(issue.Id, series.CoverIssueId);
    }

    [Fact]
    public void ResyncingTheSameCatalog_IsIdempotent()
    {
        var series = new[] { Series(5) };
        var issues = new[] { Issue(50, 5, "One"), Issue(51, 5, "Two") };
        Sync(series, issues);

        var again = Sync(series, issues);

        Assert.Equal(0, again.SeriesAdded);
        Assert.Equal(0, again.IssuesAdded);
        Assert.Equal(1, again.SeriesUpdated);
        Assert.Equal(2, again.IssuesUpdated);
        using var context = Ctx();
        Assert.Equal(1, context.Series.Count());
        Assert.Equal(2, context.Issues.Count());
    }

    [Fact]
    public void Resync_UpdatesCatalogFields_ButPreservesClientLocalReadingState()
    {
        Sync(new[] { Series(5) }, new[] { Issue(50, 5, "Old Title") });
        int localId;
        using (var context = Ctx())
        {
            var issue = context.Issues.Single();
            localId = issue.Id;
            issue.LastPageRead = 17;
            issue.OpenCount = 4;
            issue.OpenedTime = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            issue.Rating = 4.5f;
            issue.Review = "my review";
            issue.Notes = "my note";
            issue.Checked = true;
            context.SaveChanges();
        }

        Sync(new[] { Series(5) }, new[] { Issue(50, 5, "New Title") });

        using var check = Ctx();
        var back = check.Issues.Single();
        Assert.Equal(localId, back.Id);                     // same row, not delete+insert
        Assert.Equal("New Title", back.Title);              // catalog field refreshed
        Assert.Equal(17, back.LastPageRead);                // client-local state untouched
        Assert.Equal(4, back.OpenCount);
        Assert.NotNull(back.OpenedTime);
        Assert.Equal(4.5f, back.Rating);
        Assert.Equal("my review", back.Review);
        Assert.Equal("my note", back.Notes);
        Assert.True(back.Checked);
    }

    [Fact]
    public void Tags_AreReplacedToMatchTheCatalog()
    {
        Sync(new[] { Series(5) }, new[] { Issue(50, 5, "One",
            new IssueTagDto("Tags", "Theme", "Core", "Space"), new IssueTagDto("Tags", "Theme", "Unset", "War")) });

        Sync(new[] { Series(5) }, new[] { Issue(50, 5, "One",
            new IssueTagDto("Tags", "Theme", "Defining", "Space"), new IssueTagDto("Genre", "Genre", "Unset", "Sci-Fi")) });

        using var context = Ctx();
        var tags = context.Issues.Include(i => i.Tags).Single().Tags.OrderBy(t => t.Value).ToList();
        Assert.Equal(new[] { "Sci-Fi", "Space" }, tags.Select(t => t.Value).ToArray()); // "War" dropped, "Sci-Fi" added
        Assert.Equal(IssueTagWeight.Defining, tags.Single(t => t.Value == "Space").Weight); // kept tag updated in place
    }

    [Fact]
    public void IssuesTheHostStoppedSharing_AreRemoved_AndAnEmptiedSeriesGoesWithThem()
    {
        Sync(new[] { Series(5), Series(6, "Other") }, new[] { Issue(50, 5, "A"), Issue(51, 5, "B"), Issue(60, 6, "C") });

        var result = Sync(new[] { Series(5) }, new[] { Issue(50, 5, "A") });

        Assert.Equal(2, result.IssuesRemoved);
        Assert.Equal(1, result.SeriesRemoved);
        using var context = Ctx();
        Assert.Equal(new[] { 50 }, context.Issues.Select(i => i.RemoteIssueId!.Value).ToArray());
        Assert.Equal(new[] { 5 }, context.Series.Select(s => s.RemoteSeriesId!.Value).ToArray());
    }

    [Fact]
    public void RemovingTheCoverIssue_DoesNotTripTheRestrictForeignKey()
    {
        Sync(new[] { Series(5) }, new[] { Issue(50, 5, "Cover"), Issue(51, 5, "Second") });
        using (var context = Ctx())
        {
            var series = context.Series.Single();
            Assert.Equal(context.Issues.Single(i => i.RemoteIssueId == 50).Id, series.CoverIssueId);
        }

        Sync(new[] { Series(5) }, new[] { Issue(51, 5, "Second") }); // the cover issue vanishes

        using var check = Ctx();
        Assert.Equal(check.Issues.Single().Id, check.Series.Single().CoverIssueId); // re-pointed to what remains
    }

    [Fact]
    public void ACatalogIssueWithNoSeries_IsSkipped_NotAnError()
    {
        var result = Sync(new[] { Series(5) }, new[] { Issue(50, 5, "Fine"), Issue(70, 999, "Orphan") });

        Assert.Equal(1, result.IssuesAdded);
        using var context = Ctx();
        Assert.Single(context.Issues);
    }

    [Fact]
    public void UnknownEnumValues_FallBackToDefaults_InsteadOfFailingTheSync()
    {
        var weird = Series(5) with { ContentType = "FutureType", ReadingMode = "Diagonal", Status = "Hiatus2" };
        var issue = Issue(50, 5, "One") with { ColorMode = "Hologram" };

        Sync(new[] { weird }, new[] { issue });

        using var context = Ctx();
        Assert.Equal(ContentType.Unknown, context.Series.Single().ContentType);
        Assert.Equal(ReadingMode.LeftToRight, context.Series.Single().ReadingMode);
        Assert.Equal(ColorMode.Unknown, context.Issues.Single().ColorMode);
    }

    [Fact]
    public void Sync_NeverTouchesLocalRows_OrOtherSourcesRows()
    {
        int otherSourceId;
        using (var context = Ctx())
        {
            var other = new RemoteSource { InstanceId = "host-2", DisplayName = "B", Host = "h", CertFingerprint = "x" };
            context.RemoteSources.Add(other);
            var local = new Series { Name = "Saga" }; // same name as the mirrored series, on purpose
            context.Series.Add(local);
            context.SaveChanges();
            otherSourceId = other.Id;
            context.Issues.Add(new Issue { SeriesId = local.Id, Number = "1", FilePath = @"C:\a.cbz", Title = "Local" });
            context.SaveChanges();
        }
        // Another source's mirror with the SAME remote ids - a different key space.
        using (var context = Ctx())
        {
            RemoteMirrorSync.Apply(context, otherSourceId, new[] { Series(5, "B-Saga") }, new[] { Issue(50, 5, "B-One") });
        }

        Sync(new[] { Series(5) }, new[] { Issue(50, 5, "A-One") });
        Sync(Array.Empty<CatalogSeriesDto>(), Array.Empty<CatalogIssueDto>()); // host un-shares everything

        using var check = Ctx();
        Assert.Equal(new[] { "Local", "B-One" }.OrderBy(x => x), check.Issues.Select(i => i.Title).OrderBy(x => x).ToArray());
        Assert.Single(check.Issues.Where(i => i.FilePath == @"C:\a.cbz"));
    }

    [Fact]
    public void AChangedPageCount_IsReported_SoCachedPagesOfTheReplacedBookCanBeDropped()
    {
        Sync(new[] { Series(5) }, new[] { Issue(50, 5, "One") with { PageCount = 22 }, Issue(51, 5, "Two") with { PageCount = 30 } });

        var result = Sync(new[] { Series(5) }, new[] { Issue(50, 5, "One") with { PageCount = 24 }, Issue(51, 5, "Two") with { PageCount = 30 } });

        Assert.Equal(new[] { 50 }, result.ChangedContent);                   // 50 changed 22 -> 24; 51 did not
    }

    [Fact]
    public void AChangedContentStamp_IsReported_EvenWhenThePageCountIsTheSame()
    {
        Sync(new[] { Series(5) }, new[] { Issue(50, 5, "One") with { PageCount = 22, ContentStamp = "aaaa" }, Issue(51, 5, "Two") with { PageCount = 30, ContentStamp = "bbbb" } });

        var result = Sync(new[] { Series(5) }, new[] { Issue(50, 5, "One") with { PageCount = 22, ContentStamp = "cccc" }, Issue(51, 5, "Two") with { PageCount = 30, ContentStamp = "bbbb" } });

        Assert.Equal(new[] { 50 }, result.ChangedContent);                   // same page count, different file: the stamp catches it
        using var context = Ctx();
        Assert.Equal("cccc", context.Issues.Single(i => i.RemoteIssueId == 50).RemoteContentStamp);
    }

    [Fact]
    public void LearningAStampForTheFirstTime_IsNotAChange()
    {
        Sync(new[] { Series(5) }, new[] { Issue(50, 5, "One") with { PageCount = 22 } });               // host that predates stamps

        var result = Sync(new[] { Series(5) }, new[] { Issue(50, 5, "One") with { PageCount = 22, ContentStamp = "aaaa" } });

        Assert.Empty(result.ChangedContent);
    }

    [Fact]
    public void NoChange_ReportsNoReplacedContent_AndAFirstSyncNeverDoes()
    {
        var first = Sync(new[] { Series(5) }, new[] { Issue(50, 5, "One") });
        var again = Sync(new[] { Series(5) }, new[] { Issue(50, 5, "One") });

        Assert.Empty(first.ChangedContent);
        Assert.Empty(again.ChangedContent);
    }

    [Fact]
    public void ADefaultContext_IsRefused_BecauseItCouldNotSeeTheExistingMirror()
    {
        using var context = Ctx(includeRemote: false);

        Assert.Throws<InvalidOperationException>(() =>
            RemoteMirrorSync.Apply(context, _sourceId, new[] { Series(5) }, new[] { Issue(50, 5, "One") }));
    }

    [Fact]
    public void AFailureMidSync_RollsBackEverything_IncludingAlreadySavedSeriesChanges()
    {
        Sync(new[] { Series(5, "Saga") }, new[] { Issue(50, 5, "Stable") });

        // The series changes are saved first; make the later issue insert blow up so the rollback has
        // real work to do (a trigger, since every column the sync writes is otherwise valid).
        using (var context = Ctx())
        {
            context.Database.ExecuteSqlRaw(
                "CREATE TRIGGER boom BEFORE INSERT ON Issues WHEN NEW.Title = 'BOOM' BEGIN SELECT RAISE(ABORT, 'boom'); END;");
        }

        Assert.ThrowsAny<Exception>(() => Sync(
            new[] { Series(5, "Renamed"), Series(7, "Brand New") },
            new[] { Issue(50, 5, "Changed"), Issue(70, 7, "BOOM") }));

        using var check = Ctx();
        Assert.Equal(new[] { "Saga" }, check.Series.Select(s => s.Name).ToArray());   // rename rolled back, "Brand New" never persisted
        Assert.Equal(new[] { "Stable" }, check.Issues.Select(i => i.Title!).ToArray());
    }

    [Fact]
    public void ALargeLibrary_SyncsInReasonableTime()
    {
        var series = Enumerable.Range(1, 100).Select(i => Series(i, $"Series {i}")).ToList();
        var issues = Enumerable.Range(1, 2000).Select(i => Issue(i, (i % 100) + 1, $"Issue {i}", new IssueTagDto("Tags", "Theme", "Core", "Space"))).ToList();

        var watch = Stopwatch.StartNew();
        var first = Sync(series, issues);
        var firstTime = watch.Elapsed;
        watch.Restart();
        var second = Sync(series, issues);
        var secondTime = watch.Elapsed;

        Assert.Equal(2000, first.IssuesAdded);
        Assert.Equal(2000, second.IssuesUpdated);
        Assert.True(firstTime < TimeSpan.FromSeconds(30), $"first sync took {firstTime}");
        Assert.True(secondTime < TimeSpan.FromSeconds(30), $"re-sync took {secondTime}");
    }
}
