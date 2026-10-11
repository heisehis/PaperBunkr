using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.ViewModels.LibraryActions;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Library's "Complete count" and "First page is an ad" actions (docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §2, §3). Same temp-SQLite harness as
/// <see cref="LibraryBulkActionsTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SeriesBulkFixesTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_seriesfixes_{Guid.NewGuid():N}.db");
    private readonly List<(string Title, string Message)> _toasts = new();
    private readonly List<int> _writeBacks = new();

    public SeriesBulkFixesTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
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

    private sealed class FakeDialogs : IDialogService
    {
        public bool Answer { get; set; } = true;
        public string? LastMessage { get; private set; }

        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(Answer ? 0 : 1);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm", string cancelLabel = "Cancel", bool isDestructive = false)
        {
            LastMessage = message;
            return Task.FromResult(Answer);
        }
    }

    private LibraryScreenViewModel NewVm(FakeDialogs? dialogs = null)
    {
        var vm = new LibraryScreenViewModel(
            goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            showToast: (t, m) => _toasts.Add((t, m)),
            enqueueMetadataWriteBack: (id, _) => _writeBacks.Add(id));
        vm.History = new MetadataEditHistoryService();
        if (dialogs is not null)
        {
            vm.Dialogs = dialogs;
        }

        return vm;
    }

    private static (int SeriesId, int[] IssueIds) SeedSeries(string name, SeriesStatus status, params string[] numbers)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, Status = status };
        context.Series.Add(series);
        var issues = numbers.Select(n => new Issue { Series = series, Number = n, PageCount = 20, FilePath = $@"C:\lib\{name} {n}.cbz" }).ToList();
        context.Issues.AddRange(issues);
        context.SaveChanges();
        return (series.Id, issues.Select(i => i.Id).ToArray());
    }

    private static Issue Load(int id)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Issues.Single(i => i.Id == id);
    }

    // --- Complete count ---

    [Fact]
    public void CompleteCount_SetsTheCountOfACompletedSeriesFromItsLastIssue()
    {
        var (_, ids) = SeedSeries("Alpha", SeriesStatus.Completed, "1", "2", "12");
        var vm = NewVm();

        vm.CompleteSeriesCountCommand.Execute(LibraryTarget.Issues(new[] { ids[0], ids[1] }));
        TestDispatcher.Drain();

        Assert.Equal(12, Load(ids[0]).Count);
        Assert.Equal(12, Load(ids[1]).Count);
        Assert.Null(Load(ids[2]).Count);
        Assert.Contains(_toasts, t => t.Title == "Completed the count" && t.Message.Contains("2 books"));
        Assert.Contains(ids[0], _writeBacks);
    }

    [Fact]
    public void CompleteCount_OnASeriesTarget_CompletesEveryBookOfIt()
    {
        var (seriesId, ids) = SeedSeries("Alpha", SeriesStatus.Completed, "1", "2", "5");
        var vm = NewVm();

        vm.CompleteSeriesCountCommand.Execute(LibraryTarget.Series(new[] { seriesId }));
        TestDispatcher.Drain();

        Assert.All(ids, id => Assert.Equal(5, Load(id).Count));
    }

    [Fact]
    public void CompleteCount_LeavesAnOngoingSeriesAlone_AndSaysSo()
    {
        var (_, ids) = SeedSeries("Beta", SeriesStatus.Ongoing, "1", "2");
        var vm = NewVm();

        vm.CompleteSeriesCountCommand.Execute(LibraryTarget.Issues(ids));
        TestDispatcher.Drain();

        Assert.All(ids, id => Assert.Null(Load(id).Count));
        Assert.Contains(_toasts, t => t.Title == "Nothing to change");
        Assert.False(vm.History.CanUndo);
    }

    [Fact]
    public void CompleteCount_ABookThatAlreadyHasTheRightCount_IsNotTouched()
    {
        var (_, ids) = SeedSeries("Alpha", SeriesStatus.Completed, "1", "2");
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var issue in context.Issues)
            {
                issue.Count = 2;
            }

            context.SaveChanges();
        }

        var vm = NewVm();
        vm.CompleteSeriesCountCommand.Execute(LibraryTarget.Issues(ids));
        TestDispatcher.Drain();

        Assert.Contains(_toasts, t => t.Title == "Nothing to change");
        Assert.Empty(_writeBacks);
    }

    [Fact]
    public void CompleteCount_OneUndoPutsTheCountsBack()
    {
        var (_, ids) = SeedSeries("Alpha", SeriesStatus.Completed, "1", "9");
        var vm = NewVm();
        vm.CompleteSeriesCountCommand.Execute(LibraryTarget.Issues(ids));
        TestDispatcher.Drain();

        Assert.NotNull(vm.History.Undo(PaperbunkrDb.CreateContext));

        Assert.All(ids, id => Assert.Null(Load(id).Count));
    }

    // --- First page is an ad ---

    [Fact]
    public async Task FirstPageIsAnAd_AfterConfirming_TagsThePages()
    {
        var (_, ids) = SeedSeries("Alpha", SeriesStatus.Ongoing, "1", "2");
        var dialogs = new FakeDialogs();
        var vm = NewVm(dialogs);

        await vm.MarkFirstPageAsAdCommand.ExecuteAsync(LibraryTarget.Issues(ids));

        using var context = PaperbunkrDb.CreateContext();
        foreach (int id in ids)
        {
            Assert.Equal(PageType.Advertisement, context.IssuePages.Single(p => p.IssueId == id && p.PageNumber == 0).PageType);
            Assert.Equal(PageType.Cover, context.IssuePages.Single(p => p.IssueId == id && p.PageNumber == 1).PageType);
        }

        Assert.Contains("not part of Undo", dialogs.LastMessage);
        Assert.Contains(_toasts, t => t.Title == "Tagged first pages" && t.Message.Contains("2 books"));
    }

    [Fact]
    public async Task FirstPageIsAnAd_Declined_ChangesNothing()
    {
        var (_, ids) = SeedSeries("Alpha", SeriesStatus.Ongoing, "1");
        var vm = NewVm(new FakeDialogs { Answer = false });

        await vm.MarkFirstPageAsAdCommand.ExecuteAsync(LibraryTarget.Issues(ids));

        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.IssuePages);
    }

    [Fact]
    public async Task FirstPageIsAnAd_OnABookWithoutTwoKnownPages_SaysNothingWasTagged()
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.Add(new Issue { Series = new Series { Name = "One page" }, Number = "1", PageCount = 1 });
            context.SaveChanges();
        }

        int id = Load(1).Id;
        var vm = NewVm(new FakeDialogs());

        await vm.MarkFirstPageAsAdCommand.ExecuteAsync(LibraryTarget.Issues(new[] { id }));

        Assert.Contains(_toasts, t => t.Title == "Nothing tagged");
    }

    // --- Guess format from name (docs/superpowers/specs/2026-10-11-format-presets-design.md) ---

    [Fact]
    public void GuessFormat_SetsTheFormatTheNameGivesAway_AndLeavesBooksThatHaveOne()
    {
        int annual, already;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "Batman Annual" };
            var a = new Issue { Series = series, Number = "1", PageCount = 20 };
            var b = new Issue { Series = series, Number = "2", PageCount = 20, Format = "Special" };
            context.Issues.AddRange(a, b);
            context.SaveChanges();
            annual = a.Id;
            already = b.Id;
        }

        var vm = NewVm();
        vm.GuessFormatFromNameCommand.Execute(LibraryTarget.Issues(new[] { annual, already }));
        TestDispatcher.Drain();

        Assert.Equal("Annual", Load(annual).Format);
        Assert.Equal("Special", Load(already).Format);
        Assert.Contains(_toasts, t => t.Title == "Format set" && t.Message.Contains("1 book"));

        Assert.NotNull(vm.History.Undo(PaperbunkrDb.CreateContext));
        Assert.Null(Load(annual).Format);
    }

    [Fact]
    public void GuessFormat_WhenNoNameSaysAnything_SaysSo()
    {
        var (_, ids) = SeedSeries("Plain", SeriesStatus.Ongoing, "1");
        var vm = NewVm();

        vm.GuessFormatFromNameCommand.Execute(LibraryTarget.Issues(ids));
        TestDispatcher.Drain();

        Assert.Contains(_toasts, t => t.Title == "No format found");
        Assert.False(vm.History.CanUndo);
    }

    // --- Menu ---

    [Fact]
    public void TheMenus_OfferTheActions()
    {
        var (seriesId, ids) = SeedSeries("Alpha", SeriesStatus.Completed, "1");
        var vm = NewVm();
        var row = vm.IssueList.Rows.Single();

        var issueMenu = ((IContextMenuProvider)vm).BuildContextMenu(row) ?? Array.Empty<ContextMenuEntry>();

        var tidy = issueMenu.Single(e => e.Header == "Tidy up").Children!;
        Assert.Equal(
            ["Commit proposed values", "Complete count", "Guess format from name", "Find scanner from file name", "First page is an ad…"],
            tidy.Select(e => e.Header));
        Assert.NotEqual(0, seriesId);
        Assert.Single(ids);
    }
}
