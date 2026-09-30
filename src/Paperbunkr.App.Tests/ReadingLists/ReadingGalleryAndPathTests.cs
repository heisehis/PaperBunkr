using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.Tests.ReadingLists;

/// <summary>
/// The Reading Lists redesign's new pieces (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md): the Continue row, the
/// gallery (§2), the journey path and cover wall (§4-§5), Edit mode (§8) and the drawer (§9).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadingGalleryAndPathTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public ReadingGalleryAndPathTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_redesign_test_{Guid.NewGuid():N}.db");
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

    private sealed class FakeFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private static ReadingListsScreenViewModel NewScreen(Action<int, int>? read = null) =>
        new(new FakeFilePicker(), read ?? ((_, _) => { }), loadOnConstruction: false);

    private static int[] Issues(string series, int count, int readCount = 0, bool owned = true)
    {
        using var context = PaperbunkrDb.CreateContext();
        var s = new Series { Name = series };
        var issues = Enumerable.Range(1, count).Select(n => new Issue
        {
            Series = s, Number = n.ToString(), FilePath = owned ? $"c:/{series}{n}.cbz" : null, FileIsMissing = !owned, IsPlaceholder = !owned,
            PageCount = 10, LastPageRead = n <= readCount ? 9 : null,
        }).ToList();
        context.Issues.AddRange(issues);
        context.SaveChanges();
        return issues.Select(i => i.Id).ToArray();
    }

    private static int List(string name, int? folder, IEnumerable<int> issues, Func<int, string?>? label = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = new ReadingList { Name = name };
        int k = 0;
        foreach (int id in issues)
        {
            list.Items.Add(new ReadingListItem { IssueId = id, SortOrder = k, GroupLabel = label?.Invoke(k) });
            k++;
        }

        context.ReadingLists.Add(list);
        ReadingListFolders.PlaceNewList(context, list, folder);
        context.SaveChanges();
        return list.Id;
    }

    private static int Folder(string name, int? parent = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var folder = ReadingListFolders.Create(context, name, parent);
        context.SaveChanges();
        return folder.Id;
    }

    // --- Continue row (pure) ---

    [Fact]
    public void ContinueRow_KeepsStartedListsWithSomethingLeft_MostRecentFirst()
    {
        var now = new DateTime(2026, 9, 28);
        var picked = ReadingContinueRow.Select(new[]
        {
            new ContinueCandidate(1, 3, 2, now.AddDays(-5)),
            new ContinueCandidate(2, 0, 5, now),                // not started
            new ContinueCandidate(3, 4, 0, now),                // nothing owned left to read
            new ContinueCandidate(4, 1, 1, now.AddDays(-1)),
            new ContinueCandidate(5, 2, 2, null),               // no logged event - last
        });
        Assert.Equal(new[] { 4, 1, 5 }, picked);
        Assert.Equal(ReadingContinueRow.MaxCards,
            ReadingContinueRow.Select(Enumerable.Range(1, 20).Select(i => new ContinueCandidate(i, 1, 1, now.AddDays(-i)))).Count);
    }

    // --- Gallery ---

    [Fact]
    public void Gallery_ShowsFoldersFirst_ThenLists_WithFolderTotalsAndBreadcrumb()
    {
        int crisis = Folder("Crisis Events");
        int inner = Folder("Tie-ins", crisis);
        List("Saga", null, Issues("Saga", 4, readCount: 1));
        List("Infinite Crisis", crisis, Issues("IC", 4, readCount: 2));
        List("Legends", inner, Issues("L", 4, readCount: 4));

        var screen = NewScreen();
        screen.Gallery.Refresh();
        Assert.Equal(new[] { "Crisis Events", "Saga" }, screen.Gallery.Tiles.Select(t => t.Name));
        var folderTile = screen.Gallery.Tiles[0];
        Assert.True(folderTile.IsFolder);
        Assert.Equal("2 lists · 75%", folderTile.SubLine);

        screen.Gallery.OpenTileCommand.Execute(folderTile);
        Assert.Equal(new[] { "Tie-ins", "Infinite Crisis" }, screen.Gallery.Tiles.Select(t => t.Name));
        Assert.Equal(new[] { "Reading Lists", "Crisis Events" }, screen.Gallery.Breadcrumb.Select(b => b.Name));
        Assert.False(screen.Gallery.ShowContinueRow);          // top level only

        screen.Gallery.OpenCrumbCommand.Execute(screen.Gallery.Breadcrumb[0]);
        Assert.Null(screen.Gallery.CurrentFolderId);
    }

    [Fact]
    public void Gallery_ContinueRow_ListsStartedLists_ByLatestReadingEvent()
    {
        var first = Issues("A", 3, readCount: 1);
        var second = Issues("B", 3, readCount: 1);
        int older = List("Older", null, first);
        int newer = List("Newer", null, second);
        List("Untouched", null, Issues("C", 2));
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = first[0], Kind = ReadingEventKind.Finished, TimestampUtc = new DateTime(2026, 9, 1) });
            context.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = second[0], Kind = ReadingEventKind.Finished, TimestampUtc = new DateTime(2026, 9, 20) });
            context.SaveChanges();
        }

        var screen = NewScreen();
        screen.Gallery.Refresh();

        Assert.True(screen.Gallery.ShowContinueRow);
        Assert.Equal(new[] { newer, older }, screen.Gallery.ContinueCards.Select(c => c.ListId));
        Assert.Equal("next: B #2 · 1/3", screen.Gallery.ContinueCards[0].NextLine);

        screen.Gallery.OpenContinueCommand.Execute(screen.Gallery.ContinueCards[0]);
        Assert.True(screen.IsListMode);
        Assert.Equal(newer, screen.List.ActiveListId);
    }

    [Fact]
    public void Gallery_TagChips_FilterEveryListFlat()
    {
        int folder = Folder("F");
        int a = List("A", folder, Issues("A", 1));
        List("B", null, Issues("B", 1));
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ReadingLists.Find(a)!.Tags.Add(new ReadingListTag { Value = "crisis" });
            context.SaveChanges();
        }

        var screen = NewScreen();
        screen.Gallery.Refresh();
        Assert.Equal(new[] { "All", "#crisis" }, screen.Gallery.TagChips.Select(c => c.Label));

        screen.Gallery.SelectTagCommand.Execute(screen.Gallery.TagChips[1]);
        Assert.Equal(new[] { "A" }, screen.Gallery.Tiles.Select(t => t.Name));   // found inside its folder
        Assert.False(screen.Gallery.ShowContinueRow);
    }

    [Fact]
    public void Gallery_DropReordersAndMovesIntoFolders()
    {
        int folder = Folder("F");
        List("One", null, Issues("O", 1));
        List("Two", null, Issues("T", 1));
        var screen = NewScreen();
        screen.Gallery.Refresh();

        var two = screen.Gallery.Tiles.Single(t => t.Name == "Two");
        var one = screen.Gallery.Tiles.Single(t => t.Name == "One");
        Assert.True(screen.Gallery.ApplyDrop(two, one, SidebarDropZone.Before));
        TestDispatcher.Drain();
        Assert.Equal(new[] { "F", "Two", "One" }, screen.Gallery.Tiles.Select(t => t.Name));

        var folderTile = screen.Gallery.Tiles.Single(t => t.IsFolder);
        Assert.True(screen.Gallery.ApplyDrop(screen.Gallery.Tiles.Single(t => t.Name == "One"), folderTile, SidebarDropZone.Into));
        TestDispatcher.Drain();
        Assert.Equal(new[] { "F", "Two" }, screen.Gallery.Tiles.Select(t => t.Name));
        Assert.False(screen.Gallery.CanDrop(folderTile, folderTile, SidebarDropZone.Into));
    }

    [Fact]
    public void NewListFromTheDialog_LandsInTheFolderBeingViewed()
    {
        int folder = Folder("F");
        var screen = NewScreen();
        screen.Gallery.OpenFolder(folder);
        int id = List("Fresh", null, Array.Empty<int>());

        screen.PlaceCreatedList(id);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(folder, context.ReadingLists.Find(id)!.FolderId);
    }

    // --- Path ---

    [Fact]
    public void Path_ChaptersCollapseWhenFullyRead_UpNextExpands_AndSpineLightsBeforeIt()
    {
        var ids = Issues("IC", 6, readCount: 3);
        int list = List("IC", null, ids, k => k < 2 ? "Prelude" : "Main");
        var screen = NewScreen();
        screen.LoadReadingList(list);

        var items = screen.List.PathItems;
        // Prelude (2 read) collapsed, Main open with read #3, Up next #4, then #5 #6.
        Assert.IsType<ChapterHeaderItem>(items[0]);
        Assert.True(((ChapterHeaderItem)items[0]).IsCollapsed);
        Assert.Equal("▸ Prelude · 2 · all read", ((ChapterHeaderItem)items[0]).Caption);
        var main = Assert.IsType<ChapterHeaderItem>(items[1]);
        Assert.True(main.IsCurrent);
        Assert.IsType<PathRowItem>(items[2]);
        var next = Assert.IsType<UpNextItem>(items[3]);
        Assert.Equal("Up next · 4 of 6", next.Caption);
        Assert.True(((PathRowItem)items[2]).SpineLit);
        Assert.False(((PathRowItem)items[4]).SpineLit);
        Assert.Same(next, screen.List.ScrollTarget);

        screen.List.ToggleChapterCommand.Execute(items[0]);
        TestDispatcher.Drain();
        Assert.Equal(8, screen.List.PathItems.Count);          // Prelude opened: 2 headers + 6 issues
        screen.List.ToggleChapterCommand.Execute(screen.List.PathItems.OfType<ChapterHeaderItem>().Single(h => h.IsCurrent));
        TestDispatcher.Drain();
        Assert.Contains(screen.List.PathItems, i => i is UpNextItem);   // the current chapter never collapses
    }

    [Fact]
    public void Path_UnlabelledList_HasNoChapters_AndAnEmptyListShowsTheEmptyCard()
    {
        int plain = List("Plain", null, Issues("P", 3));
        int empty = List("Empty", null, Array.Empty<int>());
        var screen = NewScreen();

        screen.LoadReadingList(plain);
        Assert.DoesNotContain(screen.List.PathItems, i => i is ChapterHeaderItem);
        Assert.IsType<UpNextItem>(screen.List.PathItems[0]);

        screen.LoadReadingList(empty);
        Assert.IsType<EmptyListItem>(Assert.Single(screen.List.PathItems));
    }

    [Fact]
    public void Covers_CarryChapterCaptionOnTheFirstTile_AndTheModeIsRemembered()
    {
        int list = List("IC", null, Issues("IC", 4, readCount: 1), k => k < 2 ? "A" : "B");
        var screen = NewScreen();
        screen.LoadReadingList(list);

        Assert.Equal(new[] { "A", null, "B", null }, screen.List.CoverTiles.Select(t => t.ChapterCaption));
        Assert.Equal("1 ✓", screen.List.CoverTiles[0].Caption);
        Assert.Equal("2 ▶ next", screen.List.CoverTiles[1].Caption);

        screen.List.SetViewModeCommand.Execute(ReadingListPageViewModel.CoversMode);
        Assert.True(screen.List.IsCoversMode);
        Assert.True(NewScreen().List.IsCoversMode);            // remembered app-wide
    }

    [Fact]
    public void Hero_MetaLine_Rail_AndFolderLink()
    {
        int folder = Folder("Crisis");
        int a = List("A", folder, Issues("A", 4, readCount: 1));
        List("B", folder, Issues("B", 2));
        List("Elsewhere", null, Issues("E", 1));
        var screen = NewScreen();
        screen.LoadReadingList(a);

        Assert.Equal("4 issues · 1 read", screen.List.MetaLine);
        Assert.Equal("Crisis", screen.List.FolderName);
        Assert.Equal(new[] { "A", "B" }, screen.List.RailLists.Select(r => r.Name));
        Assert.True(screen.List.RailLists[0].IsActive);

        screen.List.OpenFolderCommand.Execute(null);
        Assert.True(screen.IsGalleryMode);
        Assert.Equal(folder, screen.Gallery.CurrentFolderId);
    }

    // --- Edit mode ---

    [Fact]
    public void EditMode_ShowsEveryRow_DragMovesTheSelection_AndAdoptsTheChapter()
    {
        var ids = Issues("IC", 5, readCount: 2);
        int list = List("IC", null, ids, k => k < 2 ? "A" : "B");
        var screen = NewScreen();
        screen.LoadReadingList(list);
        screen.List.BeginEditCommand.Execute(null);

        Assert.DoesNotContain(screen.List.PathItems, i => i is UpNextItem);
        Assert.Equal(7, screen.List.PathItems.Count);          // chapters expanded while editing

        var rows = screen.List.Rows;
        screen.List.ToggleMemberSelectionCommand.Execute(rows[3]);
        screen.List.ToggleMemberSelectionCommand.Execute(rows[4]);
        Assert.True(screen.List.MoveRows(screen.List.DragSet(rows[3]), rows[1]));   // before #2, inside chapter A
        TestDispatcher.Drain();

        Assert.Equal(new[] { ids[0], ids[3], ids[4], ids[1], ids[2] }, screen.List.Rows.Select(r => r.Item.IssueId));
        Assert.Equal(new[] { "A", "A", "A", "A", "B" }, screen.List.Rows.Select(r => r.Item.GroupLabel));
        Assert.True(screen.List.IsEditing);

        screen.List.EndEditCommand.Execute(null);
        Assert.Contains(screen.List.PathItems, i => i is UpNextItem);
    }

    [Fact]
    public void EditMode_NewChapter_Rename_AndKeyboardMove()
    {
        var ids = Issues("IC", 4);
        int list = List("IC", null, ids);
        var screen = NewScreen();
        screen.LoadReadingList(list);
        screen.List.BeginEditCommand.Execute(null);

        screen.List.ToggleMemberSelectionCommand.Execute(screen.List.Rows[2]);
        screen.List.ToggleMemberSelectionCommand.Execute(screen.List.Rows[3]);
        screen.List.AddChapterCommand.Execute(null);
        TestDispatcher.Drain();
        var header = screen.List.PathItems.OfType<ChapterHeaderItem>().Single();
        Assert.Equal(ReadingListPageViewModel.NewChapterLabel, header.Label);
        Assert.True(header.IsRenaming);

        header.RenameText = "Finale";
        screen.List.CommitChapterRenameCommand.Execute(header);
        TestDispatcher.Drain();
        Assert.Equal(new[] { null, null, "Finale", "Finale" }, screen.List.Rows.Select(r => r.Item.GroupLabel));

        Assert.True(screen.List.MoveSelectionBy(screen.List.Rows[0], 1));
        TestDispatcher.Drain();
        Assert.Equal(new[] { ids[1], ids[0], ids[2], ids[3] }, screen.List.Rows.Select(r => r.Item.IssueId));
    }

    [Fact]
    public void Drawer_OpensOnLibrary_AndRelinkUsesIt()
    {
        var owned = Issues("Real", 1);
        var missing = Issues("Real", 1, owned: false);
        int list = List("L", null, missing);
        var screen = NewScreen();
        screen.LoadReadingList(list);

        screen.List.Rows[0].LinkCommand.Execute(null);
        Assert.True(screen.List.IsDrawerOpen);
        Assert.True(screen.List.IsLibraryTab);
        Assert.True(screen.List.IsLinking);

        screen.List.SearchQuery = "Real";
        screen.List.AddIssueCommand.Execute(screen.List.SearchResults.Single(r => r.IssueId == owned[0]));
        Assert.Equal(owned[0], screen.List.Rows.Single().Item.IssueId);

        screen.List.CloseDrawerCommand.Execute(null);
        Assert.False(screen.List.IsDrawerOpen);
        screen.List.ToggleArcSearchCommand.Execute(null);
        Assert.True(screen.List.IsArcTab);
        Assert.True(screen.List.IsDrawerOpen);
    }
}
