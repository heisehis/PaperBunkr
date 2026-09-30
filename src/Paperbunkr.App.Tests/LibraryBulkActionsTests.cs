using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.ViewModels.LibraryActions;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Library bulk actions (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md): the action catalog, selection pruning/invert,
/// and each new command. Same temp-SQLite harness as <see cref="LibraryContextMenuBuilderTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryBulkActionsTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly string _tempDir;
    private readonly List<(string Title, string Message)> _toasts = new();
    private readonly List<int> _writeBacks = new();

    public LibraryBulkActionsTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bulkactions_test_{Guid.NewGuid():N}.db");
        _tempDir = Path.Combine(Path.GetTempPath(), $"paperbunkr_bulkactions_files_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
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
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ===================== Harness =====================

    private static int SeedSeries(string name, ContentType contentType = ContentType.Comic, params (string Number, string? Format)[] issues)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name, ContentType = contentType };
        context.Series.Add(series);
        context.SaveChanges();
        foreach (var (number, format) in issues.Length == 0 ? new[] { ("1", (string?)null) } : issues)
        {
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = number, Format = format, PageCount = 10 });
        }

        context.SaveChanges();
        return series.Id;
    }

    private LibraryScreenViewModel NewVm()
    {
        var vm = new LibraryScreenViewModel(
            goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            showToast: (t, m) => _toasts.Add((t, m)),
            enqueueMetadataWriteBack: (id, _) => _writeBacks.Add(id));
        vm.History = new MetadataEditHistoryService();
        vm.MetadataClipboard = new MetadataClipboardService();
        return vm;
    }

    private static IEnumerable<ContextMenuEntry> Flatten(IEnumerable<ContextMenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            if (entry.Children is { } children)
            {
                foreach (var d in Flatten(children))
                {
                    yield return d;
                }
            }
        }
    }

    private static IReadOnlyList<ContextMenuEntry> Menu(LibraryScreenViewModel vm, object target) =>
        ((IContextMenuProvider)vm).BuildContextMenu(target) ?? Array.Empty<ContextMenuEntry>();

    private static void Run(ContextMenuEntry entry)
    {
        Assert.NotNull(entry.Command);
        entry.Command!.Execute(entry.CommandParameter);
        TestDispatcher.Drain();
    }

    private static Issue IssueOf(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Issues.Include(i => i.Series).Single(i => i.Id == issueId);
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

    // ===================== Catalog =====================

    [Fact]
    public void KeyMap_GesturesAreUniqueAndParse()
    {
        var parsed = LibraryActionCatalog.KeyMap.Select(k => LibraryActionCatalog.ParseGesture(k.Gesture)).ToList();
        Assert.Equal(parsed.Count, parsed.Select(g => (g.Key, g.KeyModifiers)).Distinct().Count());
        // Avalonia's own parser reads a bare "3" as Key.Tab - the catalog's must not.
        Assert.Equal(Key.D3, LibraryActionCatalog.ParseGesture("Alt+Shift+3").Key);
        Assert.DoesNotContain(parsed, g => g.Key == Key.Tab);
    }

    [Fact]
    public void Bar_IssueSelection_HasTheDesignedOrder()
    {
        SeedSeries("Alpha");
        var vm = NewVm();
        vm.SelectAllVisibleIssuesCommand.Execute(null);

        Assert.Equal(
            new[] { "edit", "rating", "|", "bar-mark-read", "bar-mark-unread", "add-to", "|", "scrape", "organize", "paste-data", "clear-data", "refresh", "|", "reveal", "copy-paths" },
            vm.BarLeadingItems.Select(i => i.IsSeparator ? "|" : i.ActionId));
        Assert.Equal(new[] { "delete", "clear-selection" }, vm.BarTrailingItems.Select(i => i.ActionId));
        Assert.All(vm.BarLeadingItems.Concat(vm.BarTrailingItems).Where(i => !i.IsSeparator), i =>
        {
            Assert.False(string.IsNullOrEmpty(i.Label));
            Assert.NotNull(i.Entry!.Icon);
        });
        Assert.Equal("1 selected", vm.SelectionCountLabel);
    }

    [Fact]
    public void Bar_SeriesSelection_SwapsRatingAndDataForMerge()
    {
        SeedSeries("Alpha");
        SeedSeries("Beta");
        var vm = NewVm();
        vm.Granularity = LibraryContentGranularity.Series;
        vm.SelectAllVisibleSeriesCommand.Execute(null);

        var ids = vm.BarLeadingItems.Where(i => !i.IsSeparator).Select(i => i.ActionId).ToList();
        Assert.Contains("merge", ids);
        Assert.DoesNotContain("rating", ids);
        Assert.DoesNotContain("paste-data", ids);
        Assert.DoesNotContain("clear-data", ids);
        Assert.Equal("2 series selected", vm.SelectionCountLabel);
    }

    [Fact]
    public void RemoteIssueMenu_OffersOnlyReadOnlyActions()
    {
        SeedSeries("Alpha");
        var vm = NewVm();
        var row = vm.IssueList.Rows.Single();
        var remote = new IssueListRow
        {
            Id = row.Id, SeriesId = row.SeriesId, SeriesName = row.SeriesName, Title = row.Title, CoverBrush = row.CoverBrush, IsRemote = true,
        };

        var headers = Menu(vm, remote).Where(e => !e.IsSeparator).Select(e => e.Header).ToList();

        Assert.Equal(new[] { "Open", "Mark as", "Go to Series", "Copy Data", "Select All", "Invert Selection", "Clear Selection" }, headers);
    }

    [Fact]
    public void Keyboard_AltShift3_RatesTheSelection()
    {
        SeedSeries("Alpha");
        var vm = NewVm();
        vm.SelectAllVisibleIssuesCommand.Execute(null);

        Assert.True(new LibraryActionCatalog(vm).TryGetKeyCommand(Key.D3, KeyModifiers.Alt | KeyModifiers.Shift, out var command, out var parameter));
        command!.Execute(parameter);
        TestDispatcher.Drain();

        Assert.Equal(3f, IssueOf(vm.IssueList.Rows.Single().Id).Rating);
    }

    [Fact]
    public void Keyboard_NothingSelected_DoesNothing()
    {
        SeedSeries("Alpha");
        var vm = NewVm();

        Assert.False(new LibraryActionCatalog(vm).TryGetKeyCommand(Key.G, KeyModifiers.Control, out _, out _));
    }

    // ===================== Selection =====================

    [Fact]
    public void Search_PrunesSelectedItemsItHides()
    {
        SeedSeries("Alpha");
        SeedSeries("Beta");
        var vm = NewVm();
        vm.SelectAllVisibleIssuesCommand.Execute(null);
        Assert.Equal(2, vm.SelectionCount);

        vm.SearchQuery = "Alpha";
        TestDispatcher.Drain();

        Assert.Equal(1, vm.SelectionCount);
        Assert.Equal(vm.IssueList.Rows.Single().Id, vm.Selection.SelectedIds.Single());
    }

    [Fact]
    public void GranularitySwitch_ClearsTheOtherSelection()
    {
        SeedSeries("Alpha");
        var vm = NewVm();
        vm.SelectAllVisibleIssuesCommand.Execute(null);

        vm.Granularity = LibraryContentGranularity.Series;

        Assert.Equal(0, vm.SelectionCount);
        Assert.False(vm.HasAnySelection);
    }

    [Fact]
    public void InvertSelection_FlipsWithinVisibleRows()
    {
        SeedSeries("Alpha", ContentType.Comic, ("1", null), ("2", null), ("3", null));
        var vm = NewVm();
        var first = vm.IssueList.Rows.First();
        vm.ToggleIssueSelection(first, isShiftHeld: false);

        vm.InvertSelectionCommand.Execute(null);

        Assert.Equal(2, vm.SelectionCount);
        Assert.DoesNotContain(first.Id, vm.Selection.SelectedIds);
        Assert.False(first.IsSelected);
    }

    // ===================== Rating =====================

    [Fact]
    public void MyRating_SetsEveryBook_OneUndoRestoresAll_AndQueuesWriteBack()
    {
        SeedSeries("Alpha", ContentType.Comic, ("1", null), ("2", null));
        var vm = NewVm();
        vm.SelectAllVisibleIssuesCommand.Execute(null);
        var ids = vm.Selection.SelectedIds.ToList();

        var rating = Flatten(Menu(vm, vm.IssueList.Rows.First())).Single(e => e.Header == "Rate 2");
        Run(rating.Children!.Single(c => c.Header == "4 Stars"));

        Assert.All(ids, id => Assert.Equal(4f, IssueOf(id).Rating));
        Assert.Equal(ids.OrderBy(i => i), _writeBacks.OrderBy(i => i));

        // The ✓ appears only once every targeted book shares the value.
        rating = Flatten(Menu(vm, vm.IssueList.Rows.First())).Single(e => e.Header == "Rate 2");
        Assert.True(rating.Children!.Single(c => c.Header == "4 Stars").IsChecked);

        Assert.NotNull(vm.History.Undo(PaperbunkrDb.CreateContext));
        Assert.All(ids, id => Assert.Null(IssueOf(id).Rating));
    }

    // ===================== Mark read up to here =====================

    [Fact]
    public void ReadUpToHere_MarksEarlierIssuesInRunOrder_NotSpecialsOrLater()
    {
        SeedSeries("Alpha", ContentType.Comic, ("1", null), ("2", null), ("3", null), ("1", "Annual"));
        var vm = NewVm();
        var two = vm.IssueList.Rows.Single(r => r.Number == "2");

        var markAs = Menu(vm, two).Single(e => e.Header == "Mark as");
        Run(markAs.Children!.Single(c => c.Header == "Read up to here"));

        using var context = PaperbunkrDb.CreateContext();
        var issues = context.Issues.ToList();
        bool IsRead(string number, string? format) => issues.Single(i => i.Number == number && i.Format == format).LastPageRead == 9;
        Assert.True(IsRead("1", null));
        Assert.True(IsRead("2", null));
        Assert.False(IsRead("3", null));
        Assert.False(IsRead("1", "Annual"));
    }

    // ===================== Series setters =====================

    [Fact]
    public void SeriesSetters_ApplyToEverySelectedSeries()
    {
        int a = SeedSeries("Alpha");
        int b = SeedSeries("Beta");
        var vm = NewVm();
        vm.Granularity = LibraryContentGranularity.Series;
        vm.SelectAllVisibleSeriesCommand.Execute(null);

        var contentType = Menu(vm, vm.Covers.First()).Single(e => e.Header == "Content Type");
        Run(contentType.Children!.Single(c => c.Header == "Manga"));

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(ContentType.Manga, context.Series.Find(a)!.ContentType);
        Assert.Equal(ContentType.Manga, context.Series.Find(b)!.ContentType);
    }

    // ===================== Files =====================

    [Fact]
    public void CopyFilePaths_OnePerLine_InDisplayOrder_SkippingFileless()
    {
        SeedSeries("Alpha", ContentType.Comic, ("1", null), ("2", null), ("3", null));
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var issue in context.Issues.Where(i => i.Number != "2"))
            {
                issue.FilePath = $@"C:\comics\alpha{issue.Number}.cbz";
            }

            context.SaveChanges();
        }

        var vm = NewVm();
        string? copied = null;
        vm.SetClipboardText = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };
        vm.SelectAllVisibleIssuesCommand.Execute(null);

        var entry = vm.BarLeadingItems.Single(i => i.ActionId == "copy-paths").Entry!;
        Run(entry);

        var expected = vm.IssueList.Rows.Where(r => r.Number != "2").Select(r => $@"C:\comics\alpha{r.Number}.cbz");
        Assert.Equal(string.Join(Environment.NewLine, expected), copied);
    }

    // ===================== Copy / Paste / Clear Data =====================

    [Fact]
    public void PasteData_TickedFieldsOnly_EmptyClears_ListsReplaced_OneUndo()
    {
        SeedSeries("Alpha", ContentType.Comic, ("1", null), ("2", null));
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var issue in context.Issues)
            {
                issue.Writer = "Old Writer, Other";
                issue.Summary = "Keep?";
                issue.Notes = "Untouched";
            }

            context.SaveChanges();
        }

        var vm = NewVm();
        var ids = vm.IssueList.Rows.Select(r => r.Id).ToList();

        vm.ApplyPastedData(ids, new Dictionary<string, string?> { ["Writer"] = "Alan Moore", ["Summary"] = null });
        TestDispatcher.Drain();

        Assert.All(ids, id =>
        {
            var issue = IssueOf(id);
            Assert.Equal("Alan Moore", issue.Writer);
            Assert.Null(issue.Summary);
            Assert.Equal("Untouched", issue.Notes);
        });

        vm.History.Undo(PaperbunkrDb.CreateContext);
        Assert.All(ids, id => Assert.Equal("Old Writer, Other", IssueOf(id).Writer));
    }

    [Fact]
    public void CopyData_InLibrary_PastesInTheEditor_SharedClipboard()
    {
        SeedSeries("Alpha", ContentType.Comic, ("1", null), ("2", null));
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.First(i => i.Number == "1").Writer = "Grant Morrison";
            context.SaveChanges();
        }

        var vm = NewVm();
        var one = vm.IssueList.Rows.Single(r => r.Number == "1");
        Run(Menu(vm, one).Single(e => e.Header == "Copy Data"));
        Assert.True(vm.HasMetadataClipboard);

        var editor = new IssuePropertiesScreenViewModel(() => { }) { Clipboard = vm.MetadataClipboard };
        editor.Load(vm.IssueList.Rows.Single(r => r.Number == "2").Id);
        editor.PasteFieldsCommand.Execute(null);

        Assert.Equal("Grant Morrison", editor.Writer);
    }

    [Fact]
    public async Task ClearData_ResetsBookFields_KeepsReadStateAndSeries()
    {
        int seriesId = SeedSeries("Alpha", ContentType.Manga);
        using (var context = PaperbunkrDb.CreateContext())
        {
            var issue = context.Issues.Single();
            issue.Writer = "Someone";
            issue.Rating = 5;
            issue.LastPageRead = 9;
            context.SaveChanges();
        }

        var vm = NewVm();
        var dialogs = new FakeDialogs();
        vm.Dialogs = dialogs;
        var row = vm.IssueList.Rows.Single();

        await vm.ClearDataCommand.ExecuteAsync(LibraryTarget.Issues(new[] { row.Id }));
        TestDispatcher.Drain();

        var cleared = IssueOf(row.Id);
        Assert.Null(cleared.Writer);
        Assert.Null(cleared.Rating);
        Assert.Equal(9, cleared.LastPageRead);
        Assert.Equal(ContentType.Manga, cleared.Series!.ContentType);
        Assert.Equal(seriesId, cleared.SeriesId);
        Assert.Contains("can be reverted with Undo", dialogs.LastMessage);
    }

    [Fact]
    public async Task ClearData_Declined_ChangesNothing()
    {
        SeedSeries("Alpha");
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Issues.Single().Writer = "Someone";
            context.SaveChanges();
        }

        var vm = NewVm();
        vm.Dialogs = new FakeDialogs { Answer = false };
        var row = vm.IssueList.Rows.Single();

        await vm.ClearDataCommand.ExecuteAsync(LibraryTarget.Issues(new[] { row.Id }));

        Assert.Equal("Someone", IssueOf(row.Id).Writer);
        Assert.False(vm.History.CanUndo);
    }

    [Fact]
    public void PasteDataDialog_MarkDefined_TicksOnlyFieldsWithValues_AndRemembersTicks()
    {
        var applied = new List<IReadOnlyDictionary<string, string?>>();
        var dialog = new PasteDataScreenViewModel(() => { }, (_, fields) => applied.Add(fields));
        var content = new MetadataClipboardContent("Saga #54", new Dictionary<string, string?> { ["Writer"] = "Brian K. Vaughan", ["Title"] = null });

        dialog.Load(new[] { 1, 2 }, content);
        Assert.Equal(0, dialog.CheckedCount);
        Assert.False(dialog.PasteCommand.CanExecute(null));

        dialog.MarkDefinedCommand.Execute(null);
        var ticked = dialog.Groups.SelectMany(g => g.Fields).Where(f => f.IsChecked).Select(f => f.Label).ToList();
        Assert.Equal(new[] { "Writer" }, ticked);
        Assert.Equal("Paste 1 field", dialog.PasteLabel);

        dialog.PasteCommand.Execute(null);
        TestDispatcher.Drain();
        Assert.Equal("Brian K. Vaughan", Assert.Single(applied)["Writer"]);

        var again = new PasteDataScreenViewModel(() => { }, (_, _) => { });
        again.Load(new[] { 1 }, content);
        Assert.Equal(new[] { "Writer" }, again.Groups.SelectMany(g => g.Fields).Where(f => f.IsChecked).Select(f => f.Label));
        Assert.DoesNotContain(again.Groups.SelectMany(g => g.Fields), f => f.Label is "Content Type" or "Status" or "Reading Status");
    }

    // ===================== Merge series =====================

    [Fact]
    public void MergeDialog_DefaultsToMostIssues_AndCountsDuplicates()
    {
        int small = SeedSeries("Saga", ContentType.Comic, ("2", null), ("3", null));
        int big = SeedSeries("Saga (2012)", ContentType.Comic, ("1", null), ("2", null), ("3", null), ("4", null));
        var dialog = new MergeSeriesScreenViewModel(() => { }, (_, _) => { });

        dialog.Load(new[] { small, big });

        Assert.Equal(big, dialog.Target!.SeriesId);
        Assert.Equal(2, dialog.DuplicateCount);
        Assert.Equal("Merge into Saga (2012)", dialog.MergeLabel);

        dialog.ChooseTargetCommand.Execute(dialog.Candidates.Single(c => c.SeriesId == small));
        Assert.Equal(small, dialog.Target!.SeriesId);
        Assert.Equal(2, dialog.DuplicateCount);
    }

    [Fact]
    public void ApplySeriesMerge_MovesIssues_RemovesDuplicates_KeepsTarget()
    {
        int target = SeedSeries("Alpha", ContentType.Comic, ("1", null), ("2", null));
        int source = SeedSeries("Alpha (dup)", ContentType.Comic, ("2", null), ("3", null));
        var vm = NewVm();

        vm.ApplySeriesMerge(target, new[] { source });

        using var context = PaperbunkrDb.CreateContext();
        Assert.Null(context.Series.Find(source));
        Assert.Equal(new[] { "1", "2", "3" }, context.Issues.Where(i => i.SeriesId == target).Select(i => i.Number!).OrderBy(n => n));
        Assert.Equal(3, context.Issues.Count());
    }

    // ===================== Show in List =====================

    [Fact]
    public void ShowInList_ListsReadingListsAndCollectionsHoldingTheBookOrItsSeries()
    {
        int seriesId = SeedSeries("Alpha");
        using (var context = PaperbunkrDb.CreateContext())
        {
            var issue = context.Issues.Single();
            var list = new ReadingList { Name = "Crossover", Type = ReadingListType.User, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.ReadingLists.Add(list);
            context.SaveChanges();
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = list.Id, IssueId = issue.Id });
            context.SaveChanges();
            var collection = Paperbunkr.Data.Collections.CollectionService.Create(context, "Favorites");
            context.CollectionItems.Add(new CollectionItem { CollectionId = collection.Id, SeriesId = seriesId });
            context.SaveChanges();
        }

        var vm = NewVm();
        var showInList = Menu(vm, vm.IssueList.Rows.Single()).Single(e => e.Header == "Show in List");

        Assert.Equal(new[] { "Crossover", "Favorites" }, showInList.Children!.Select(c => c.Header));
    }

    [Fact]
    public void ShowInList_NoLists_ShowsDisabledPlaceholder()
    {
        SeedSeries("Alpha");
        var vm = NewVm();

        var child = Assert.Single(Menu(vm, vm.IssueList.Rows.Single()).Single(e => e.Header == "Show in List").Children!);
        Assert.False(child.IsEnabled);
    }

    // ===================== Re-read from file =====================

    [Fact]
    public void Rescan_OverwritesFromEmbeddedComicInfo()
    {
        string path = CbzFixture.Create(Path.Combine(_tempDir, "with-info.cbz"), 2,
            new cYo.Projects.ComicRack.Engine.ComicInfo { Writer = "From File", Title = "File Title" });
        var issue = new Issue { Number = "1", Writer = "In Library", Summary = "Library only", FilePath = path, Tags = new List<IssueTag>() };

        Assert.Equal(IssueRescanOutcome.Updated, IssueFileRescanService.Rescan(issue));
        Assert.Equal("From File", issue.Writer);
        Assert.Equal("File Title", issue.Title);
        Assert.Null(issue.Summary);
        Assert.True(issue.FileSize > 0);
    }

    [Fact]
    public void Rescan_NoEmbeddedInfo_LeavesMetadataAlone()
    {
        string path = CbzFixture.Create(Path.Combine(_tempDir, "no-info.cbz"), 1);
        var issue = new Issue { Number = "1", Writer = "In Library", FilePath = path, Tags = new List<IssueTag>() };

        Assert.Equal(IssueRescanOutcome.NoEmbeddedInfo, IssueFileRescanService.Rescan(issue));
        Assert.Equal("In Library", issue.Writer);
    }

    [Fact]
    public void Rescan_MissingFile_Fails()
    {
        var issue = new Issue { Number = "1", FilePath = Path.Combine(_tempDir, "gone.cbz"), Tags = new List<IssueTag>() };

        Assert.Equal(IssueRescanOutcome.Failed, IssueFileRescanService.Rescan(issue));
    }
}
