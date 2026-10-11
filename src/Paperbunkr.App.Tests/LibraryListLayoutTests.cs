using System;
using System.IO;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>
/// List layouts on the Library and Books view models (docs/superpowers/specs/2026-10-04-list-layouts-design.md): every
/// list remembers its own layout, a named layout is copied onto a list rather than linked, and the List Options draft
/// writes back columns, caption lines and tile elements. Same temp-database isolation as <see cref="LibraryWorkspaceTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LibraryListLayoutTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public LibraryListLayoutTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_library_listlayout_test_{Guid.NewGuid():N}.db");
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

    private static LibraryScreenViewModel CreateVm(string enteredName = "My layout") =>
        new(
            goDetail: _ => { },
            goReaderForIssue: _ => { },
            goToNewIssueProperties: (_, _, _) => { },
            promptForName: (_, cb) => cb(enteredName));

    private static ContentTypeSummary Manga => new() { ContentType = ContentType.Manga, Name = "Manga" };

    private static string? Assignment(string key) => new ListLayoutService().GetAssignment(WorkspaceScreen.Library, key);

    [Fact]
    public void Construction_CapturesTheDefaultOnce_AndCreatesNoRowForTheList()
    {
        var vm = CreateVm();

        Assert.NotNull(Assignment(ListLayoutAssignment.DefaultKey));
        Assert.Null(Assignment("all"));
        Assert.Equal("all", vm.LayoutSelectionKey);
    }

    [Fact]
    public void AChange_CreatesTheCurrentListsRow_AndLeavesTheDefaultAlone()
    {
        var vm = CreateVm();
        string defaultJson = Assignment(ListLayoutAssignment.DefaultKey)!;

        vm.IssueList.SortField = IssueListSortField.Title;

        Assert.Equal(IssueListSortField.Title, ListLayoutStateJson.Deserialize(Assignment("all")).SortField);
        Assert.Equal(defaultJson, Assignment(ListLayoutAssignment.DefaultKey));
    }

    [Fact]
    public void AFilterChange_IsNotALayoutChange()
    {
        var vm = CreateVm();

        vm.FilterUnreadOnly = true;

        Assert.Null(Assignment("all"));
    }

    [Fact]
    public void EachListRemembersItsOwnLayout()
    {
        var vm = CreateVm();
        vm.SetViewModeCommand.Execute(LibraryViewMode.List);
        vm.IssueList.SortField = IssueListSortField.Title;

        vm.SelectContentTypeCommand.Execute(Manga);

        // A list with no layout of its own shows the default, not whatever the previous list had.
        Assert.Equal("content:Manga", vm.LayoutSelectionKey);
        Assert.Equal(LibraryViewMode.PosterGrid, vm.ViewMode);
        Assert.Equal(IssueListSortField.Added, vm.IssueList.SortField);
        Assert.Null(Assignment("content:Manga"));

        vm.SetViewModeCommand.Execute(LibraryViewMode.DetailsTable);
        vm.SelectAllSeriesCommand.Execute(null);

        Assert.Equal(LibraryViewMode.List, vm.ViewMode);
        Assert.Equal(IssueListSortField.Title, vm.IssueList.SortField);

        vm.SelectContentTypeCommand.Execute(Manga);
        Assert.Equal(LibraryViewMode.DetailsTable, vm.ViewMode);
    }

    [Fact]
    public void TheListOnScreenAtLaunch_ShowsItsOwnLayout_IncludingColumnWidths()
    {
        var vm = CreateVm();
        var title = vm.DetailsColumns.First(c => c.Field == IssueListSortField.Title);
        vm.SetDetailsColumnWidth(title, 333, commit: true);

        var restarted = CreateVm();

        Assert.Equal(333, restarted.DetailsColumns.First(c => c.Field == IssueListSortField.Title).Width);
    }

    [Fact]
    public void ColumnWidth_IsWrittenOnlyOnCommit_AndClamped()
    {
        var vm = CreateVm();
        var title = vm.DetailsColumns.First(c => c.Field == IssueListSortField.Title);

        vm.SetDetailsColumnWidth(title, 400, commit: false);
        Assert.Equal(400, title.Width);
        Assert.Null(Assignment("all"));

        vm.SetDetailsColumnWidth(title, 1, commit: true);
        Assert.Equal(ListLayoutStateJson.MinColumnWidth, title.Width);
        Assert.NotNull(Assignment("all"));
    }

    [Fact]
    public void MovingAColumn_ChangesTheOrder_AndIsRemembered()
    {
        var vm = CreateVm();
        var visible = vm.DetailsColumns.Where(c => c.IsVisible).ToList();
        var first = visible[0];
        var third = visible[2];

        vm.MoveDetailsColumnTo(first, third);

        Assert.Equal(2, vm.DetailsColumns.IndexOf(first));
        var stored = ListLayoutStateJson.Deserialize(Assignment("all")).Columns!;
        Assert.Equal(first.Field, stored[2].Field);

        var restarted = CreateVm();
        Assert.Equal(first.Field, restarted.DetailsColumns[2].Field);
    }

    [Fact]
    public void HeaderHitTest_FindsTheColumnUnderThePointer()
    {
        var vm = CreateVm();
        var visible = vm.DetailsColumns.Where(c => c.IsVisible).ToList();

        Assert.Same(visible[0], LibraryScreen.DetailsHeaderColumnAt(vm, 1));
        Assert.Same(visible[1], LibraryScreen.DetailsHeaderColumnAt(vm, visible[0].Width + 11));
        Assert.Same(visible[^1], LibraryScreen.DetailsHeaderColumnAt(vm, 100000));
    }

    [Fact]
    public void ANamedLayout_IsCopiedOntoAList_NotLinked()
    {
        var vm = CreateVm("Reading order");
        vm.SetViewModeCommand.Execute(LibraryViewMode.DetailsTable);
        vm.IssueList.SortField = IssueListSortField.Title;
        vm.ListLayouts.SaveLayoutAsCommand.Execute(null);
        int id = vm.ListLayouts.Layouts.Single().Id;

        vm.SelectContentTypeCommand.Execute(Manga);
        vm.ListLayouts.ApplyLayoutCommand.Execute(id);

        Assert.Equal(LibraryViewMode.DetailsTable, vm.ViewMode);
        Assert.Equal(IssueListSortField.Title, vm.IssueList.SortField);
        Assert.NotNull(Assignment("content:Manga"));

        // Changing the list afterwards leaves the named layout as it was saved.
        vm.IssueList.SortField = IssueListSortField.Writer;
        var named = new ListLayoutService().ListNamed(WorkspaceScreen.Library).Single();
        Assert.Equal(IssueListSortField.Title, ListLayoutStateJson.Deserialize(named.StateJson).SortField);

        // Deleting the named layout leaves the list as it is.
        vm.ListLayouts.DeleteLayoutCommand.Execute(id);
        Assert.Equal(IssueListSortField.Writer, ListLayoutStateJson.Deserialize(Assignment("content:Manga")).SortField);
    }

    [Fact]
    public void SaveLayoutAs_ReusingAName_ReplacesIt()
    {
        var vm = CreateVm("Same");
        vm.ListLayouts.SaveLayoutAsCommand.Execute(null);
        vm.IssueList.SortField = IssueListSortField.Title;
        vm.ListLayouts.SaveLayoutAsCommand.Execute(null);

        var named = new ListLayoutService().ListNamed(WorkspaceScreen.Library).Single();
        Assert.Equal(IssueListSortField.Title, ListLayoutStateJson.Deserialize(named.StateJson).SortField);
    }

    [Fact]
    public void ResetToDefault_ForgetsTheListsOwnLayout()
    {
        var vm = CreateVm();
        vm.SetViewModeCommand.Execute(LibraryViewMode.List);
        Assert.NotNull(Assignment("all"));

        vm.ListLayouts.ResetToDefaultCommand.Execute(null);

        Assert.Null(Assignment("all"));
        Assert.Equal(LibraryViewMode.PosterGrid, vm.ViewMode);
    }

    [Fact]
    public async System.Threading.Tasks.Task SetOnAllLists_AsksFirst_ThenEveryListShowsIt()
    {
        var vm = CreateVm("Everywhere");
        vm.SelectContentTypeCommand.Execute(Manga);
        vm.SetViewModeCommand.Execute(LibraryViewMode.List);
        vm.SelectAllSeriesCommand.Execute(null);
        vm.SetViewModeCommand.Execute(LibraryViewMode.DetailsTable);
        vm.ListLayouts.SaveLayoutAsCommand.Execute(null);
        int id = vm.ListLayouts.Layouts.Single().Id;

        vm.ListLayouts.Confirm = (_, _, _, _) => System.Threading.Tasks.Task.FromResult(false);
        await vm.ListLayouts.SetLayoutOnAllListsCommand.ExecuteAsync(id);
        Assert.NotNull(Assignment("content:Manga"));

        vm.ListLayouts.Confirm = (_, _, _, _) => System.Threading.Tasks.Task.FromResult(true);
        await vm.ListLayouts.SetLayoutOnAllListsCommand.ExecuteAsync(id);

        Assert.Null(Assignment("content:Manga"));
        vm.SelectContentTypeCommand.Execute(Manga);
        Assert.Equal(LibraryViewMode.DetailsTable, vm.ViewMode);
    }

    [Fact]
    public void ApplyingAWorkspace_MakesItsLookTheTargetListsLayout()
    {
        var vm = CreateVm("Manga table");
        vm.SelectContentTypeCommand.Execute(Manga);
        vm.SetViewModeCommand.Execute(LibraryViewMode.DetailsTable);
        vm.SaveWorkspaceAsCommand.Execute(null);
        int workspaceId = vm.Workspaces.Single(w => !w.IsBuiltIn).Id;
        vm.SetViewModeCommand.Execute(LibraryViewMode.List);
        vm.SelectAllSeriesCommand.Execute(null);

        vm.ApplyWorkspaceCommand.Execute(workspaceId);

        Assert.Equal("content:Manga", vm.LayoutSelectionKey);
        Assert.Equal(LibraryViewMode.DetailsTable, ListLayoutStateJson.Deserialize(Assignment("content:Manga")).ViewMode);
    }

    // --- List Options ---

    [Fact]
    public void ListOptions_Apply_WritesColumnsCaptionsAndTileElements()
    {
        var vm = CreateVm();
        var options = vm.CreateListOptions(() => { });

        options.HideAllColumnsCommand.Execute(null);
        var writer = options.Columns.First(c => c.Field == IssueListSortField.Writer);
        writer.IsChecked = true;
        writer.Width = 210;
        options.SelectedColumn = writer;
        while (options.MoveColumnUpCommand.CanExecute(null))
        {
            options.MoveColumnUpCommand.Execute(null);
        }

        options.UseBuiltInCaptions = false;
        options.Caption1 = options.CaptionChoices.First(c => c == IssueListFieldCatalog.SortFields[IssueListSortField.Publisher].DisplayName);
        options.TileElements.First(t => t.Element == TileTextElement.Writer).IsChecked = true;
        options.ApplyCommand.Execute(null);

        Assert.Equal(IssueListSortField.Writer, vm.DetailsColumns[0].Field);
        Assert.Equal(new[] { IssueListSortField.Writer }, vm.DetailsColumns.Where(c => c.IsVisible).Select(c => c.Field));
        Assert.Equal(210, vm.DetailsColumns[0].Width);
        Assert.True(vm.HasCustomCaptions);
        Assert.Equal(IssueListSortField.Publisher, vm.CaptionField1);
        Assert.Null(vm.CaptionField2);
        Assert.Contains(TileTextElement.Writer, vm.TileElements);

        var stored = ListLayoutStateJson.Deserialize(Assignment("all"));
        Assert.Equal(new[] { IssueListSortField.Publisher }, stored.CaptionFields);
        Assert.Contains(TileTextElement.Writer, stored.TileElements!);
    }

    [Fact]
    public void ListOptions_HidingEveryColumn_FallsBackToTheDefaultSet()
    {
        var vm = CreateVm();
        var options = vm.CreateListOptions(() => { });

        options.HideAllColumnsCommand.Execute(null);
        options.ApplyCommand.Execute(null);

        Assert.Equal(IssueListFieldCatalog.DefaultDetailsColumns, vm.DetailsColumns.Where(c => c.IsVisible).Select(c => c.Field));
    }

    [Fact]
    public void ListOptions_Cancel_ChangesNothing_AndUntouchedDraft_StaysDefault()
    {
        var vm = CreateVm();
        bool closed = false;
        var options = vm.CreateListOptions(() => closed = true);
        options.ShowAllColumnsCommand.Execute(null);

        options.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Null(Assignment("all"));
        Assert.False(vm.HasCustomCaptions);
        Assert.Equal(ListLayoutState.DefaultTileElements, vm.TileElements);
    }

    [Fact]
    public void CustomCaptions_SizeThePosterCardForTheirLineCount()
    {
        var vm = CreateVm();
        double twoLines = vm.PosterCardHeight;

        vm.ApplyListOptions(new ListLayoutState(CaptionFields: new[] { IssueListSortField.Title, IssueListSortField.Writer, IssueListSortField.Publisher }));
        Assert.Equal(twoLines + 17, vm.PosterCardHeight);

        vm.ApplyListOptions(new ListLayoutState(CaptionFields: Array.Empty<IssueListSortField>()));
        Assert.False(vm.EffectiveShowTileTitles);
    }

    [Fact]
    public void TileSecondLine_JoinsTheCheckedElements_AndSkipsEmptyOnes()
    {
        var row = new IssueListRow { Id = 1, SeriesName = "Saga", Title = "Chapter One", Number = "1", Writer = "Brian K. Vaughan", CoverBrush = Avalonia.Media.Brushes.Transparent };

        Assert.Equal("Saga", ListLayoutText.TileSecondLine(ListLayoutState.DefaultTileElements, row));
        Assert.Equal("Saga · Brian K. Vaughan",
            ListLayoutText.TileSecondLine(new[] { TileTextElement.Title, TileTextElement.Series, TileTextElement.Publisher, TileTextElement.Writer }, row));
    }

    // --- Books ---

    private static BooksScreenViewModel CreateBooksVm() =>
        new(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { }, promptForName: (_, cb) => cb("By author"));

    [Fact]
    public void Books_ALayoutIsItsSortAndGroup_AndItIsRemembered()
    {
        var vm = CreateBooksVm();
        vm.SortField = BooksSortField.Author;
        vm.ListLayouts.SaveLayoutAsCommand.Execute(null);
        vm.SortField = BooksSortField.Title;

        vm.ListLayouts.ApplyLayoutCommand.Execute(vm.ListLayouts.Layouts.Single().Id);
        Assert.Equal(BooksSortField.Author, vm.SortField);

        // Books layouts and Library layouts are separate lists.
        Assert.Empty(new ListLayoutService().ListNamed(WorkspaceScreen.Library));

        var restarted = CreateBooksVm();
        Assert.Equal(BooksSortField.Author, restarted.SortField);
    }

    [Fact]
    public void Books_ListOptions_EditsSortAndGroup()
    {
        var vm = CreateBooksVm();
        var options = vm.CreateListOptions(() => { });
        Assert.True(options.IsBooks);
        Assert.False(options.IsDetailsTab);

        options.BooksSort = "Last opened";
        options.BooksGroup = "Author";
        options.BooksSortDescending = true;
        options.OkCommand.Execute(null);

        Assert.Equal(BooksSortField.LastOpened, vm.SortField);
        Assert.Equal(BooksGroupField.Author, vm.GroupField);
        Assert.Equal(SortDirection.Descending, vm.SortDirection);
    }
}
