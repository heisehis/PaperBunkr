using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Tests.Services;

public class ScrapeSweeperTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_sweeper_{Guid.NewGuid():N}.db");
    private DateTime _now = T0;
    private readonly List<DaemonEvent> _events = new();
    private readonly List<int> _writeBacks = new();
    private Func<int, ComicVineIssueDetails?> _respond = _ => Details();
    private Exception? _throw;

    public ScrapeSweeperTests()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath};Foreign Keys=True").Options);

    private sealed class Recorder(List<DaemonEvent> sink) : IEventPublisher
    {
        public void Publish(DaemonEvent daemonEvent) => sink.Add(daemonEvent);
    }

    private sealed class Source(ScrapeSweeperTests owner) : IComicVineIssueDetailsSource
    {
        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken)
        {
            if (owner._throw is { } ex) throw ex;
            return Task.FromResult(owner._respond(issueId));
        }
    }

    private static ComicVineIssueDetails Details() => new(
        1, 1, "Spawn", "263", "Endgame", null, new ComicVineDatePart(2016, 5, 1), new ComicVineDatePart(null, null, null), "Story",
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), new[] { new ComicVineCredit("Todd", "Writer") });

    private ScrapeSweeper Sweeper() =>
        new(NewContext, new ScrapeByIdService(NewContext, () => new Source(this)), new Recorder(_events), _writeBacks.Add, () => _now);

    private int SeedImported(ScrapeStatus status, int attempts = 0, DateTime? lastAttempt = null, bool terminal = false, int comicVineIssueId = 1)
    {
        using var context = NewContext();
        var issue = new Issue { Series = new Series { Name = "Spawn" }, Number = "263", FilePath = $"C:/x/{Guid.NewGuid():N}.cbz" };
        var watched = new WatchedSeries { Name = "Spawn", ExternalVolumeId = 1 + comicVineIssueId };
        context.Issues.Add(issue);
        context.WatchedSeries.Add(watched);
        context.SaveChanges();
        var wanted = new WantedIssue
        {
            WatchedSeriesId = watched.Id, ExternalIssueId = comicVineIssueId, IssueNumber = "263", IssueId = issue.Id, CreatedAt = T0, ImportedAt = T0,
            Status = WantedIssueStatus.Imported, ScrapeStatus = status, ScrapeAttempts = attempts, ScrapeLastAttemptAt = lastAttempt, ScrapeFailureIsTerminal = terminal,
        };
        context.WantedIssues.Add(wanted);
        context.SaveChanges();
        return wanted.Id;
    }

    private WantedIssue Load(int id)
    {
        using var context = NewContext();
        return context.WantedIssues.AsNoTracking().Single(w => w.Id == id);
    }

    [Fact]
    public async Task APendingRow_IsScraped_WritesBackTheFile_AndAnnouncesIt()
    {
        var id = SeedImported(ScrapeStatus.Pending);

        Assert.Equal(1, await Sweeper().SweepAsync(CancellationToken.None));

        var saved = Load(id);
        Assert.Equal(ScrapeStatus.Scraped, saved.ScrapeStatus);
        Assert.Equal(1, saved.ScrapeAttempts);
        Assert.Null(saved.ScrapeError);
        Assert.Single(_writeBacks);                                  // the file gets re-tagged
        var scraped = Assert.IsType<IssueScrapedEvent>(Assert.Single(_events));
        Assert.Equal(id, scraped.WantedIssueId);
    }

    [Fact]
    public async Task ARetryableFailure_IsKept_AndTriedAgainOnlyAfterTheBackoff()
    {
        _throw = new ComicVineException("ComicVine request failed");
        var id = SeedImported(ScrapeStatus.Pending);
        var sweeper = Sweeper();

        await sweeper.SweepAsync(CancellationToken.None);

        var failed = Load(id);
        Assert.Equal(ScrapeStatus.Failed, failed.ScrapeStatus);
        Assert.False(failed.ScrapeFailureIsTerminal);
        Assert.True(Assert.IsType<IssueScrapeFailedEvent>(Assert.Single(_events)).WillRetry);

        // Too soon: nothing to do. After the backoff (5 min for the first failure) it is eligible again.
        Assert.False(sweeper.HasWork());
        _now = T0.AddMinutes(4);
        Assert.False(sweeper.HasWork());
        _now = T0.AddMinutes(6);
        Assert.True(sweeper.HasWork());

        _throw = null;
        await sweeper.SweepAsync(CancellationToken.None);
        Assert.Equal(ScrapeStatus.Scraped, Load(id).ScrapeStatus);
    }

    [Fact]
    public async Task ATerminalFailure_IsNeverRetriedAutomatically_SoComicVineIsNotAskedAgain()
    {
        _respond = _ => null;                                        // ComicVine says there is no such issue
        var id = SeedImported(ScrapeStatus.Pending);
        var sweeper = Sweeper();

        await sweeper.SweepAsync(CancellationToken.None);

        var failed = Load(id);
        Assert.Equal(ScrapeStatus.Failed, failed.ScrapeStatus);
        Assert.True(failed.ScrapeFailureIsTerminal);
        Assert.False(Assert.IsType<IssueScrapeFailedEvent>(Assert.Single(_events)).WillRetry);

        _now = T0.AddDays(30);
        Assert.False(sweeper.HasWork());
        Assert.Equal(0, await sweeper.SweepAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ARowThatKeepsFailing_StopsAfterTheAttemptCap()
    {
        _throw = new ComicVineException("ComicVine request failed");
        var id = SeedImported(ScrapeStatus.Failed, attempts: ScrapeSweeper.MaxAttempts, lastAttempt: T0.AddYears(-1));

        Assert.False(Sweeper().HasWork());
        Assert.Equal(ScrapeStatus.Failed, Load(id).ScrapeStatus);
    }

    [Fact]
    public async Task ATurnedOffToggle_StopsTheSweep_AndLeavesRowsPending()
    {
        var id = SeedImported(ScrapeStatus.Pending);
        using (var context = NewContext())
        {
            context.GetOrCreateAcquisitionSettings().ScrapeOnImport = false;
            context.SaveChanges();
        }

        var sweeper = Sweeper();

        Assert.False(sweeper.HasWork());
        Assert.Equal(0, await sweeper.SweepAsync(CancellationToken.None));
        Assert.Equal(ScrapeStatus.Pending, Load(id).ScrapeStatus);   // still queued: turning the toggle back on resumes it
    }

    [Fact]
    public async Task APass_HandlesOnlyABatch_SoABacklogTrickles()
    {
        for (int i = 0; i < ScrapeSweeper.BatchSize + 3; i++)
        {
            SeedImported(ScrapeStatus.Pending, comicVineIssueId: 100 + i);
        }

        Assert.Equal(ScrapeSweeper.BatchSize, await Sweeper().SweepAsync(CancellationToken.None));

        using var context = NewContext();
        Assert.Equal(3, context.WantedIssues.Count(w => w.ScrapeStatus == ScrapeStatus.Pending));
    }

    [Fact]
    public void Retry_PutsAFailedRowBackInTheQueueNow_AndDismissRetiresIt()
    {
        var failed = SeedImported(ScrapeStatus.Failed, attempts: 5, lastAttempt: T0, terminal: true);
        var other = SeedImported(ScrapeStatus.Failed, attempts: 2, lastAttempt: T0, comicVineIssueId: 2);

        using (var context = NewContext())
        {
            Assert.True(ScrapeSweeper.Requeue(context, failed));
            Assert.True(ScrapeSweeper.Dismiss(context, other));
            Assert.False(ScrapeSweeper.Requeue(context, failed));    // already pending: nothing to retry
        }

        var requeued = Load(failed);
        Assert.Equal(ScrapeStatus.Pending, requeued.ScrapeStatus);
        Assert.Equal(0, requeued.ScrapeAttempts);
        Assert.False(requeued.ScrapeFailureIsTerminal);
        Assert.Equal(ScrapeStatus.NotApplicable, Load(other).ScrapeStatus);
    }

    [Fact]
    public void TheBackoffGrows()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), ScrapeSweeper.Backoff(1));
        Assert.Equal(TimeSpan.FromMinutes(20), ScrapeSweeper.Backoff(2));
        Assert.Equal(TimeSpan.FromMinutes(80), ScrapeSweeper.Backoff(4));
    }
}
