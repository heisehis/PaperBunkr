using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.History;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="HistoryTabViewModel"/> and <see cref="HistoryDayLabel"/> (docs/superpowers/specs/
/// 2026-09-29-insights-reading-history-design.md §3). Row computation is covered by
/// <see cref="ReadingHistoryResolverTests"/>; here the resolver runs synchronously (injected runner) and
/// dispatcher posts go into a queue the test drains by hand, so the "deferred past the click" rule is visible.
/// </summary>
public class HistoryTabViewModelTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc); // a Tuesday

    private readonly string _dbPath;
    private readonly Queue<Action> _posted = new();

    public HistoryTabViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_history_vm_{Guid.NewGuid():N}.db");
        using var ctx = NewContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

    private sealed class Nav
    {
        public List<string> Calls { get; } = new();
    }

    private sealed class FakeDialogs : IDialogService
    {
        public bool Answer { get; set; }
        public int Asked { get; private set; }

        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(0);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false)
        {
            Asked++;
            return Task.FromResult(Answer);
        }
    }

    private HistoryTabViewModel NewVm(ReadingEventRecorder recorder, Nav? nav = null, FakeDialogs? dialogs = null)
    {
        nav ??= new Nav();
        return new HistoryTabViewModel(
            id => nav.Calls.Add($"reader:{id}"),
            id => nav.Calls.Add($"series:{id}"),
            (id, format) => nav.Calls.Add($"book-reader:{id}:{format}"),
            id => nav.Calls.Add($"book:{id}"),
            dialogs ?? new FakeDialogs(),
            recorder,
            activity: null,
            contextFactory: NewContext,
            nowUtc: () => Now,
            timeZone: TimeZoneInfo.Utc,
            runInBackground: work => Task.FromResult(work()),
            post: _posted.Enqueue);
    }

    private void DrainPosted()
    {
        while (_posted.Count > 0)
        {
            _posted.Dequeue()();
        }
    }

    private int SeedIssue(string seriesName, ContentType type = ContentType.Comic)
    {
        using var ctx = NewContext();
        var series = new Series { Name = seriesName, ContentType = type };
        ctx.Series.Add(series);
        ctx.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", PageCount = 20 };
        ctx.Issues.Add(issue);
        ctx.SaveChanges();
        return issue.Id;
    }

    private void Read(ReadingEventRecorder recorder, int issueId, DateTime at)
    {
        int seriesId;
        using (var ctx = NewContext())
        {
            seriesId = ctx.Issues.Single(i => i.Id == issueId).SeriesId;
        }

        recorder.RecordOpened(ReadingItemType.Comic, issueId, seriesId, null, null);
        using var c = NewContext();
        c.ReadingEvents.OrderByDescending(e => e.Id).First().TimestampUtc = at;
        c.SaveChanges();
    }

    private static ReadingHistoryRow Row(
        ReadingHistoryAction action = ReadingHistoryAction.Resume,
        ReadingHistoryContentKind kind = ReadingHistoryContentKind.Comic,
        bool inLibrary = true,
        int? seriesId = 7,
        int? bookId = null,
        BookFormat? format = null) =>
        new(new ReadingHistoryGroupKey(ReadingHistoryGroupKind.ComicSeries, seriesId ?? 0), "Saga", "#4", Now, null, kind,
            inLibrary, IsFinished: false, IsCaughtUp: false, ProgressText: null, action,
            TargetId: action == ReadingHistoryAction.None ? null : 42, TargetLabel: "#4", format, seriesId, bookId);

    // ----- day labels -----

    [Theory]
    [InlineData("2026-09-29", "TODAY")]
    [InlineData("2026-09-28", "YESTERDAY")]
    [InlineData("2026-09-27", "SUNDAY")]
    [InlineData("2026-09-23", "WEDNESDAY")]
    [InlineData("2026-09-22", "SEP 22")]
    [InlineData("2026-01-05", "JAN 5")]
    [InlineData("2025-12-31", "DEC 31, 2025")]
    [InlineData("2026-09-30", "TODAY")] // clock skew: a future timestamp still reads as today
    public void DayLabel(string date, string expected) =>
        Assert.Equal(expected, HistoryDayLabel.For(DateTime.Parse(date), new DateTime(2026, 9, 29)));

    // ----- list shape -----

    [Fact]
    public async Task Items_InterleaveOneHeaderPerDay_NewestFirst()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        Read(recorder, SeedIssue("Saga"), Now.AddHours(-1));
        Read(recorder, SeedIssue("One Piece"), Now.AddHours(-2));
        Read(recorder, SeedIssue("Monstress"), Now.AddDays(-1));
        var vm = NewVm(recorder);

        await vm.RefreshAsync();

        var shape = vm.Items.Select(i => i switch
        {
            HistoryDayHeader h => h.Label,
            HistoryRowItem r => r.Title,
            _ => "?",
        }).ToList();
        Assert.Equal(new[] { "TODAY", "Saga", "One Piece", "YESTERDAY", "Monstress" }, shape);
        Assert.False(vm.IsEmpty);
        Assert.True(vm.HasRows);
    }

    [Fact]
    public async Task Search_AndTypeChips_FilterInMemory()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        Read(recorder, SeedIssue("Saga"), Now.AddHours(-1));
        Read(recorder, SeedIssue("One Piece", ContentType.Manga), Now.AddHours(-2));
        var vm = NewVm(recorder);
        await vm.RefreshAsync();

        vm.SetTypeFilterCommand.Execute(HistoryTypeFilter.Manga);
        Assert.Equal(new[] { "One Piece" }, vm.Items.OfType<HistoryRowItem>().Select(r => r.Title));
        Assert.True(vm.IsMangaFilter);

        vm.SetTypeFilterCommand.Execute(HistoryTypeFilter.All);
        vm.SearchText = "sag";
        Assert.Equal(new[] { "Saga" }, vm.Items.OfType<HistoryRowItem>().Select(r => r.Title));

        vm.SearchText = "zzz";
        Assert.Empty(vm.Items);
        Assert.True(vm.IsFilteredEmpty);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public async Task EmptyLog_IsEmpty_NotFilteredEmpty()
    {
        var vm = NewVm(new ReadingEventRecorder(NewContext));
        await vm.RefreshAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.IsFilteredEmpty);
        Assert.False(vm.ClearAllCommand.CanExecute(null));
    }

    // ----- commands -----

    [Fact]
    public void OpenRow_RoutesToSeriesOrBookDetail_AndIgnoresGreyedRows()
    {
        var nav = new Nav();
        var vm = NewVm(new ReadingEventRecorder(NewContext), nav);

        vm.OpenRowCommand.Execute(new HistoryRowItem(Row(seriesId: 7), TimeZoneInfo.Utc));
        vm.OpenRowCommand.Execute(new HistoryRowItem(Row(kind: ReadingHistoryContentKind.Book, seriesId: null, bookId: 9), TimeZoneInfo.Utc));
        vm.OpenRowCommand.Execute(new HistoryRowItem(Row(inLibrary: false, seriesId: null), TimeZoneInfo.Utc));

        Assert.Equal(new[] { "series:7", "book:9" }, nav.Calls);
    }

    [Fact]
    public void PlayRow_RoutesComicsToTheIssueReader_BooksToTheBookReader_AndNoneIsANoOp()
    {
        var nav = new Nav();
        var vm = NewVm(new ReadingEventRecorder(NewContext), nav);

        vm.PlayRowCommand.Execute(new HistoryRowItem(Row(ReadingHistoryAction.ReadNext), TimeZoneInfo.Utc));
        vm.PlayRowCommand.Execute(new HistoryRowItem(Row(ReadingHistoryAction.Open, ReadingHistoryContentKind.Book, format: BookFormat.Pdf), TimeZoneInfo.Utc));
        vm.PlayRowCommand.Execute(new HistoryRowItem(Row(ReadingHistoryAction.None), TimeZoneInfo.Utc));

        Assert.Equal(new[] { "reader:42", "book-reader:42:Pdf" }, nav.Calls);
    }

    [Fact]
    public async Task RemoveRow_HidesTheSeries_AndTheReloadIsDeferredPastTheClick()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        Read(recorder, SeedIssue("Saga"), Now.AddHours(-1));
        Read(recorder, SeedIssue("One Piece"), Now.AddHours(-2));
        var vm = NewVm(recorder);
        vm.IsActive = true;
        await vm.RefreshAsync();
        DrainPosted(); // the reads above queued reloads of their own

        var saga = vm.Items.OfType<HistoryRowItem>().Single(r => r.Title == "Saga");
        vm.RemoveRowCommand.Execute(saga);

        // Still inside the "click": the list hasn't been touched yet.
        Assert.Contains(saga, vm.Items);
        Assert.NotEmpty(_posted);

        DrainPosted();
        Assert.Equal(new[] { "One Piece" }, vm.Items.OfType<HistoryRowItem>().Select(r => r.Title));
    }

    [Fact]
    public async Task ClearAll_AsksFirst_AndDoesNothingOnCancel()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        Read(recorder, SeedIssue("Saga"), Now.AddHours(-1));
        var dialogs = new FakeDialogs { Answer = false };
        var vm = NewVm(recorder, dialogs: dialogs);
        vm.IsActive = true;
        await vm.RefreshAsync();

        await vm.ClearAllCommand.ExecuteAsync(null);
        using (var ctx = NewContext())
        {
            Assert.False(ctx.ReadingEvents.Any(e => e.HiddenFromHistory));
        }

        dialogs.Answer = true;
        await vm.ClearAllCommand.ExecuteAsync(null);
        DrainPosted();

        Assert.Equal(2, dialogs.Asked);
        Assert.Empty(vm.Items);
        Assert.True(vm.IsEmpty);
        using (var ctx = NewContext())
        {
            Assert.All(ctx.ReadingEvents, e => Assert.True(e.HiddenFromHistory));
        }
    }

    [Fact]
    public async Task NewReadingEvents_ReloadOnlyWhileActive()
    {
        var recorder = new ReadingEventRecorder(NewContext);
        Read(recorder, SeedIssue("Saga"), Now.AddHours(-2));
        var vm = NewVm(recorder);
        await vm.RefreshAsync();
        DrainPosted();

        Read(recorder, SeedIssue("One Piece"), Now.AddHours(-1));
        DrainPosted();
        Assert.Single(vm.Items.OfType<HistoryRowItem>()); // inactive: marked stale, not reloaded

        vm.IsActive = true;
        vm.Refresh(); // what selecting the tab does
        Assert.Equal(2, vm.Items.OfType<HistoryRowItem>().Count());
    }

    [Fact]
    public void ContextMenu_GreyedRowOffersOnlyRemove()
    {
        var vm = NewVm(new ReadingEventRecorder(NewContext));

        var live = vm.BuildContextMenu(new HistoryRowItem(Row(ReadingHistoryAction.ReadNext), TimeZoneInfo.Utc))!;
        Assert.Equal(new[] { "Read next: #4", "Open series", null, "Remove from history" }, live.Select(e => e.Header));

        var gone = vm.BuildContextMenu(new HistoryRowItem(Row(ReadingHistoryAction.None, inLibrary: false, seriesId: null), TimeZoneInfo.Utc))!;
        Assert.Equal(new[] { "Remove from history" }, gone.Select(e => e.Header));

        Assert.Null(vm.BuildContextMenu(new HistoryDayHeader("TODAY")));
    }

    [Fact]
    public void RowItem_SubtitleAndTags()
    {
        var pdf = new HistoryRowItem(
            Row(ReadingHistoryAction.Open, ReadingHistoryContentKind.Book, format: BookFormat.Pdf) with { ItemLabel = null, LastReadUtc = new DateTime(2026, 9, 29, 16, 52, 0, DateTimeKind.Utc) },
            TimeZoneInfo.Utc);
        Assert.Equal("4:52 PM · PDF", pdf.Subtitle);
        Assert.True(pdf.ShowOpen);
        Assert.False(pdf.ShowPlay);
        Assert.True(pdf.ShowBookCover);

        var gone = new HistoryRowItem(Row(inLibrary: false) with { IsFinished = true }, TimeZoneInfo.Utc);
        Assert.Equal("NO LONGER IN LIBRARY", gone.TagText);
        Assert.True(gone.IsWarningTag);
        Assert.True(gone.IsGone);
        Assert.False(gone.ShowIssueCover);

        var caughtUp = new HistoryRowItem(Row(ReadingHistoryAction.None) with { IsFinished = true, IsCaughtUp = true }, TimeZoneInfo.Utc);
        Assert.Equal("CAUGHT UP", caughtUp.TagText);
        Assert.True(caughtUp.IsSuccessTag);
    }
}
