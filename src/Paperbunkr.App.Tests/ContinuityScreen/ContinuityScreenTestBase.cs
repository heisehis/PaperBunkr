using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary>
/// Shared set-up for the Continuity screen's view-model tests (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md):
/// a temp SQLite database through <see cref="PaperbunkrDbContext.DatabasePathOverride"/> (the pattern every screen test uses - the
/// screen has no context-factory seam), synchronous runners for the overview, the map and the identity sweep so nothing leaves the
/// pinned test thread, no GCD extract, and no network. Derived classes join <see cref="AvaloniaTestCollection"/>, which serialises
/// everything that touches the shared database override.
/// </summary>
public abstract class ContinuityScreenTestBase : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    protected ContinuityScreenTestBase()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_continuity_screen_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    protected static ActivityService QuietActivity() => new(dispatch: a => a(), recordRun: _ => { });

    protected static ContinuityScreenViewModel CreateScreen(
        Action<int>? goToSeriesDetail = null,
        Action<int>? goToReader = null,
        Action<int>? goToReadingList = null,
        ActivityService? activity = null,
        Action<string, string>? notify = null)
    {
        var vm = new ContinuityScreenViewModel(goToSeriesDetail, goToReader, goToReadingList, notify, activity ?? QuietActivity(),
            loadOnConstruction: true, runner: work => Task.FromResult(work()), openGcd: () => null)
        {
            MapBackgroundRunner = work => Task.FromResult(work()),
        };
        vm.Suggestions.IdentityRunner = work => work();
        vm.Suggestions.IdentitySources = () => new Dictionary<ComicProvider, IArcIdentitySource>();
        vm.Suggestions.IdentityWikidata = () => new NoWikidata();
        return vm;
    }

    /// <summary>Opens an event and returns its page.</summary>
    protected static EventPageViewModel OpenEvent(int eventId, ContinuityScreenViewModel? screen = null)
    {
        screen ??= CreateScreen();
        screen.LoadEvent(eventId);
        return screen.EventPage;
    }

    protected static void Drain() => TestDispatcher.Drain();

    protected static int SeedSeries(string name, params (string Number, int? Year)[] issues)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name };
        foreach (var (number, year) in issues)
        {
            series.Issues.Add(new Issue { Number = number, Year = year });
        }

        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    protected static int SeedIssue(string seriesName, string number, string? format = null, int? year = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = number, Format = format, Year = year };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    protected static int SeedEvent(string name, DateTime? start = null, DateTime? end = null, params int[] memberIssueIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = new StoryEvent { Name = name, StartDate = start, EndDate = end, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        int position = 0;
        foreach (int issueId in memberIssueIds)
        {
            storyEvent.Members.Add(new EventMembership { IssueId = issueId, Position = position++, Role = EventMembershipRole.Core });
        }

        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        return storyEvent.Id;
    }

    protected static int SeedContinuity(string name, params int[] seriesIds)
    {
        using var context = PaperbunkrDb.CreateContext();
        var continuity = ContinuityResolver.GetOrCreate(context, name);
        foreach (int seriesId in seriesIds)
        {
            ContinuityResolver.AddSeriesToContinuity(context, seriesId, continuity.Id);
        }

        return continuity.Id;
    }

    protected static void MarkRead(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var issue = context.Issues.Find(issueId)!;
        issue.PageCount = 20;
        issue.LastPageRead = 19;
        issue.OpenCount = 1;
        context.SaveChanges();
    }

    /// <summary>An empty Wikidata: every search misses, so the connector runs without the network.</summary>
    protected sealed class NoWikidata : IWikidataLookup
    {
        public Task<IReadOnlyList<WikidataSearchResult>> SearchEntitiesAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WikidataSearchResult>>(Array.Empty<WikidataSearchResult>());

        public Task<WikidataEntity?> GetEntityAsync(string qid, CancellationToken cancellationToken) => Task.FromResult<WikidataEntity?>(null);

        public Task<IReadOnlyList<string>> FindByComicVineIdAsync(string comicVineId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
