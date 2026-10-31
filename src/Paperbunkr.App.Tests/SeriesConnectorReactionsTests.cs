using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Scheduling;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The series connector's App side (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §5.2-§5.3): the reactions to a
/// scan or scrape, the off switch, and the weekly task's catalog entry. No network: no Metron login is saved in the temp database.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SeriesConnectorReactionsTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public SeriesConnectorReactionsTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_series_connector_{Guid.NewGuid():N}.db");
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
    }

    private static Task Inline(Func<Task> work) => work();

    private static void SeedTwoVolumes()
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Series.Add(new Series { Name = "Superman (2011)", Issues = { new Issue { Number = "1", Year = 2011 }, new Issue { Number = "52", Year = 2016 } } });
        context.Series.Add(new Series { Name = "Superman (2016)", Issues = { new Issue { Number = "1", Year = 2016 }, new Issue { Number = "45", Year = 2018 } } });
        context.SaveChanges();
    }

    private static void SetEnabled(bool on)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.GetOrCreateAppSettings().FindRelatedSeriesAutomatically = on;
        context.SaveChanges();
    }

    private static int RelationCount()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.MediaRelations.AsNoTracking().Count();
    }

    private static LibraryScanCompletedEvent Scan() => new(Array.Empty<string>(), 0, 0, 0, TimeSpan.Zero, Array.Empty<int>(), Array.Empty<int>());

    [Fact]
    public void AScan_LinksVolumes_AndReportsOnceToTheActivityCenter()
    {
        SeedTwoVolumes();
        var events = new LibraryEvents();
        var activity = new ActivityService(dispatch: a => a());
        using var reactions = new SeriesConnectorReactions(events, activity, PaperbunkrDb.CreateContext, Inline);

        events.Raise(Scan());

        Assert.Equal(1, RelationCount());
        Assert.Contains(activity.RecentJobs, j => j.Title == "Finding related series");
    }

    [Fact]
    public void AScanThatChangesNothing_AddsNoActivityNoise()
    {
        var events = new LibraryEvents();
        var activity = new ActivityService(dispatch: a => a());
        using var reactions = new SeriesConnectorReactions(events, activity, PaperbunkrDb.CreateContext, Inline);

        events.Raise(Scan());

        Assert.DoesNotContain(activity.RecentJobs, j => j.Title == "Finding related series");
    }

    [Fact]
    public void AScrape_LinksToo_ButAScrapeThatAppliedNothingDoesNot()
    {
        SeedTwoVolumes();
        var events = new LibraryEvents();
        var activity = new ActivityService(dispatch: a => a());
        using var reactions = new SeriesConnectorReactions(events, activity, PaperbunkrDb.CreateContext, Inline);

        events.Raise(new ScrapeCompletedEvent(Array.Empty<int>()));
        Assert.Equal(0, RelationCount());

        events.Raise(new ScrapeCompletedEvent(new[] { 1 }));
        Assert.Equal(1, RelationCount());
    }

    [Fact]
    public async Task TurnedOff_NoTriggerLinksAnything_AndTheWeeklyTaskSaysSo()
    {
        SeedTwoVolumes();
        SetEnabled(false);
        var events = new LibraryEvents();
        using var reactions = new SeriesConnectorReactions(events, new ActivityService(dispatch: a => a()), PaperbunkrDb.CreateContext, Inline);

        events.Raise(Scan());
        string summary = await SeriesConnectorRunner.RunFullAsync(PaperbunkrDb.CreateContext, null, CancellationToken.None);

        Assert.Equal(0, RelationCount());
        Assert.Equal(SeriesConnectorRunner.TurnedOff, summary);
    }

    [Fact]
    public async Task FullRun_WithoutMetron_StillLinksLocally()
    {
        SeedTwoVolumes();

        string summary = await SeriesConnectorRunner.RunFullAsync(PaperbunkrDb.CreateContext, null, CancellationToken.None);

        Assert.Equal("1 relation found · 0 removed", summary);
    }

    [Fact]
    public void WeeklyTask_IsInTheCatalog_OnByDefault_AfterGcdMatching()
    {
        var task = Assert.Single(ScheduledTaskCatalog.All, t => t.Id == ScheduledTaskCatalog.FindRelatedSeries);
        var gcd = Assert.Single(ScheduledTaskCatalog.All, t => t.Id == ScheduledTaskCatalog.GcdMatch);

        Assert.True(task.DefaultEnabled);
        Assert.Equal(TimeSpan.FromDays(7), task.DefaultInterval);
        Assert.True(task.Priority > gcd.Priority);
        Assert.Equal(ScheduledTaskCatalog.All.Count, ScheduledTaskCatalog.All.Select(t => t.Priority).Distinct().Count());
    }
}
