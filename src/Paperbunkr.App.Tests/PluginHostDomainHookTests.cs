using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Plugins;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="PluginHostService"/> wiring for the Plugin API 4.1 domain hooks (docs/superpowers/specs/
/// 2026-09-20-plugin-api-4-1-design.md §5): events raised by the app's producers reach real <c>.csx</c>
/// plugins as typed hook payloads, through the real dispatcher, and a misbehaving plugin surfaces as a
/// plugin-scoped Activity Center alert. The producers themselves are covered by
/// <see cref="DomainEventProducerTests"/>; the dispatcher's queue/timeout behaviour by
/// <c>DomainHookDispatcherTests</c> in Paperbunkr.Plugins.Tests.
/// </summary>
public sealed class PluginHostDomainHookTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _pluginsRoot;
    private readonly LibraryEvents _events = new();
    private readonly ReadingEventRecorder _recorder;
    private readonly PluginHostService _host = new();
    private readonly TestPluginEnvironment _environment = new();

    public PluginHostDomainHookTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_hostdomain_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Database.EnsureCreated();
        }

        _pluginsRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_hostdomain_plugins_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_pluginsRoot);

        _recorder = new ReadingEventRecorder(NewContext);
        _host.ContextFactory = NewContext;
    }

    public void Dispose()
    {
        _host.DetachDomainEvents();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_pluginsRoot)) Directory.Delete(_pluginsRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext() => new(_dbOptions);

    private void WritePlugin(string key, string hook, string script)
    {
        string dir = Path.Combine(_pluginsRoot, key);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "plugin.xml"), $"""
            <Plugin key="{key}" name="{key} plugin">
              <Command hook="{hook}" key="{key}.cmd" name="{key}" script="run.csx" />
            </Plugin>
            """);
        File.WriteAllText(Path.Combine(dir, "run.csx"), script);
    }

    /// <summary>Discovers whatever plugins were written and attaches the host to this test's own producers.</summary>
    private void Attach()
    {
        _host.Engine.Discover(_pluginsRoot, _environment);
        Assert.All(_host.Engine.AllCommands, c => Assert.False(c.IsBroken, c.CompileError));
        _host.AttachDomainEvents(_recorder, _events);
    }

    private async Task Settle() => Assert.True(await _host.DomainHooks.WaitForIdleAsync(TimeSpan.FromSeconds(15)));

    private IReadOnlyList<string> AlertTitles => _environment.RecordedActivity.Alerts.Select(a => a.Title).ToList();

    private int SeedComic(string number = "7")
    {
        using var context = NewContext();
        var series = new Series { Name = "Kilo Station" };
        var issue = new Issue { Series = series, Number = number };
        context.Series.Add(series);
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    // ---------------------------------------------------------------- BookRead

    private const string DescribeBookRead = """
        Environment.Activity.RaiseAlert(PluginAlertSeverity.Info,
            ItemType + ":" + ItemId + ":" + (Issue != null ? "issue#" + Issue.Number : "noissue") + ":" + (Book != null ? "book" : "nobook") + ":" + PagesRead + ":" + SeriesId);
        return null;
        """;

    [Fact]
    public async Task A_finished_comic_reaches_a_BookRead_plugin_with_its_issue_loaded()
    {
        WritePlugin("scrobbler", PluginHooks.BookRead, DescribeBookRead);
        Attach();
        int issueId = SeedComic("7");

        _recorder.RecordFinished(ReadingItemType.Comic, issueId, seriesId: 3, publisher: null, primaryGenre: null, pagesRead: 22);
        await Settle();

        Assert.Equal(new[] { $"Comic:{issueId}:issue#7:nobook:22:3" }, AlertTitles);
    }

    [Fact]
    public async Task A_finished_item_that_has_since_been_deleted_arrives_with_neither_entity()
    {
        WritePlugin("scrobbler", PluginHooks.BookRead, DescribeBookRead);
        Attach();

        _recorder.RecordFinished(ReadingItemType.Novel, itemId: 9999, seriesId: null, publisher: null, primaryGenre: null, pagesRead: null);
        await Settle();

        Assert.Equal(new[] { "Novel:9999:noissue:nobook::" }, AlertTitles);
    }

    [Fact]
    public async Task BookRead_carries_the_finish_time_from_the_saved_row()
    {
        WritePlugin("clock", PluginHooks.BookRead, """
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, FinishedUtc.Kind + ":" + (DateTime.UtcNow - FinishedUtc < TimeSpan.FromMinutes(1)));
            return null;
            """);
        Attach();

        _recorder.RecordFinished(ReadingItemType.Comic, itemId: 1, seriesId: null, publisher: null, primaryGenre: null, pagesRead: 5);
        await Settle();

        Assert.Equal(new[] { "Utc:True" }, AlertTitles);
    }

    [Fact]
    public async Task A_re_read_reaches_the_plugin_again()
    {
        WritePlugin("counter", PluginHooks.BookRead, DescribeBookRead);
        Attach();
        int issueId = SeedComic();

        _recorder.RecordFinished(ReadingItemType.Comic, issueId, null, null, null, pagesRead: 20);
        _recorder.RecordFinished(ReadingItemType.Comic, issueId, null, null, null, pagesRead: 20);
        await Settle();

        Assert.Equal(2, AlertTitles.Count);
    }

    [Fact]
    public async Task BookRead_still_dispatches_when_the_item_lookup_itself_fails()
    {
        WritePlugin("scrobbler", PluginHooks.BookRead, DescribeBookRead);
        Attach();
        _host.ContextFactory = () => throw new InvalidOperationException("database unavailable");

        _recorder.RecordFinished(ReadingItemType.Comic, itemId: 5, seriesId: null, publisher: null, primaryGenre: null, pagesRead: 3);
        await Settle();

        Assert.Equal(new[] { "Comic:5:noissue:nobook:3:" }, AlertTitles);
    }

    // ---------------------------------------------------------------- the other three hooks

    [Fact]
    public async Task A_completed_scan_reaches_a_LibraryScanCompleted_plugin()
    {
        WritePlugin("indexer", PluginHooks.LibraryScanCompleted, """
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info,
                string.Join(",", FolderPaths) + "|" + AddedCount + "|" + UpdatedCount + "|" + SeriesTouched + "|" + string.Join(",", AddedItemIds) + "|" + UpdatedItemIds.Count + "|" + Duration.TotalSeconds);
            return null;
            """);
        Attach();

        _events.Raise(new LibraryScanCompletedEvent(
            new[] { @"D:\Comics", @"E:\Manga" }, AddedCount: 3, UpdatedCount: 0, SeriesTouched: 2, TimeSpan.FromSeconds(4), new[] { 10, 11, 12 }, Array.Empty<int>()));
        await Settle();

        Assert.Equal(new[] { @"D:\Comics,E:\Manga|3|0|2|10,11,12|0|4" }, AlertTitles);
    }

    [Fact]
    public async Task A_confirmed_missing_file_reaches_a_MissingFileDetected_plugin()
    {
        WritePlugin("watchdog", PluginHooks.MissingFileDetected, """
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Warning, ItemType + ":" + ItemId + ":" + Title + ":" + FilePath);
            return null;
            """);
        Attach();

        _events.Raise(new MissingFileConfirmedEvent(ReadingItemType.Comic, 42, @"D:\Comics\gone.cbz", "Gone"));
        await Settle();

        Assert.Equal(new[] { @"Comic:42:Gone:D:\Comics\gone.cbz" }, AlertTitles);
    }

    [Fact]
    public async Task A_reading_list_change_reaches_a_ReadingListChanged_plugin_with_its_flags_and_ids()
    {
        WritePlugin("mirror", PluginHooks.ReadingListChanged, """
            Environment.Activity.RaiseAlert(PluginAlertSeverity.Info,
                ListId + ":" + ListName + ":" + Kind + ":" + string.Join(",", AddedIssueIds) + ":" + string.Join(",", RemovedIssueIds));
            return null;
            """);
        Attach();

        _events.Raise(new ReadingListChangedEvent(
            5, "Signal War", ReadingListChangeKind.Added | ReadingListChangeKind.Removed, new[] { 1, 2 }, new[] { 3 }));
        await Settle();

        Assert.Equal(new[] { "5:Signal War:Added, Removed:1,2:3" }, AlertTitles);
    }

    [Fact]
    public async Task Each_hook_only_wakes_the_plugins_registered_on_it()
    {
        WritePlugin("only-scan", PluginHooks.LibraryScanCompleted, "Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, \"scan\"); return null;");
        WritePlugin("only-list", PluginHooks.ReadingListChanged, "Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, \"list\"); return null;");
        Attach();

        _events.Raise(new ReadingListChangedEvent(1, "x", ReadingListChangeKind.Added, new[] { 1 }, Array.Empty<int>()));
        await Settle();

        Assert.Equal(new[] { "list" }, AlertTitles);
    }

    // ---------------------------------------------------------------- lifecycle

    [Fact]
    public async Task With_no_plugins_registered_an_event_is_a_harmless_no_op()
    {
        Attach();

        _events.Raise(new ReadingListChangedEvent(1, "x", ReadingListChangeKind.Added, new[] { 1 }, Array.Empty<int>()));
        _recorder.RecordFinished(ReadingItemType.Comic, 1, null, null, null, pagesRead: 1);
        await Settle();

        Assert.Empty(AlertTitles);
    }

    [Fact]
    public async Task Detaching_stops_delivery()
    {
        WritePlugin("mirror", PluginHooks.ReadingListChanged, "Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, ListName); return null;");
        Attach();
        _host.DetachDomainEvents();

        _events.Raise(new ReadingListChangedEvent(1, "x", ReadingListChangeKind.Added, new[] { 1 }, Array.Empty<int>()));
        await Settle();

        Assert.Empty(AlertTitles);
    }

    [Fact]
    public async Task Attaching_twice_does_not_deliver_every_event_twice()
    {
        WritePlugin("mirror", PluginHooks.ReadingListChanged, "Environment.Activity.RaiseAlert(PluginAlertSeverity.Info, ListName); return null;");
        Attach();
        _host.AttachDomainEvents(_recorder, _events);

        _events.Raise(new ReadingListChangedEvent(1, "x", ReadingListChangeKind.Added, new[] { 1 }, Array.Empty<int>()));
        await Settle();

        Assert.Single(AlertTitles);
    }

    // ---------------------------------------------------------------- problems become plugin-scoped alerts

    [Fact]
    public async Task A_failing_plugin_raises_one_plugin_scoped_alert_however_often_it_fails()
    {
        WritePlugin("flaky", PluginHooks.ReadingListChanged, "throw new System.InvalidOperationException(\"kaboom\");");
        var activity = new ActivityService(dispatch: a => a(), recordRun: _ => { });
        _host.ActivityForAlerts = activity;
        Attach();

        for (int i = 0; i < 4; i++)
        {
            _events.Raise(new ReadingListChangedEvent(1, "x", ReadingListChangeKind.Added, new[] { 1 }, Array.Empty<int>()));
        }

        await Settle();

        var alert = Assert.Single(activity.Alerts);
        Assert.Equal("flaky plugin failed", alert.Title);   // the manifest name, not the key
        Assert.Equal(ActivityAlertSeverity.Error, alert.Severity);
        Assert.Contains("kaboom", alert.Detail);
        Assert.Equal("plugin:flaky:hook:flaky.cmd:Failed", alert.DedupeKey);
    }

    [Fact]
    public async Task A_failure_with_no_activity_service_available_does_not_throw()
    {
        WritePlugin("flaky", PluginHooks.ReadingListChanged, "throw new System.InvalidOperationException(\"kaboom\");");
        Attach();   // ActivityForAlerts is left null, as in any host with no main view-model

        _events.Raise(new ReadingListChangedEvent(1, "x", ReadingListChangeKind.Added, new[] { 1 }, Array.Empty<int>()));

        await Settle();
    }
}
