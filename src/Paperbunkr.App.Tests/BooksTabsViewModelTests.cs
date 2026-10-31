using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Status tabs, view mode, author filter and the new menus' state on the Books screen (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BooksTabsViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public BooksTabsViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_books_tabs_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private static BooksScreenViewModel Create() =>
        new(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { });

    private static void Seed(string title, BookReadingStatus status, string? author = null, float? rating = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        context.Books.Add(new Book
        {
            Title = title, Author = author, Status = status, Rating = rating, Format = BookFormat.Epub,
            FilePath = $@"C:\b\{title}.epub", AddedTime = DateTime.UtcNow,
        });
        context.SaveChanges();
    }

    [Fact]
    public void Tabs_CountEveryStatus_AndFilterTheGrid()
    {
        Seed("A", BookReadingStatus.Reading);
        Seed("B", BookReadingStatus.Reading);
        Seed("C", BookReadingStatus.Finished);
        Seed("D", BookReadingStatus.Unread);
        var vm = Create();
        vm.LoadFromDatabase();

        Assert.Equal(new[] { 4, 2, 0, 1, 1, 0 }, vm.StatusTabs.Select(t => t.Count));
        Assert.True(vm.StatusTabs[0].IsActive);

        vm.SetStatusTabCommand.Execute(BooksStatusTab.Reading);

        Assert.Equal(new[] { "A", "B" }, vm.Books.Select(b => b.Title).OrderBy(t => t));
        Assert.True(vm.StatusTabs[1].IsActive);
        Assert.False(vm.StatusTabs[0].IsActive);
    }

    [Fact]
    public void TabCounts_IgnoreTheSearchBox_ButTheGridDoesNot()
    {
        Seed("Dune", BookReadingStatus.Reading);
        Seed("Emma", BookReadingStatus.Reading);
        var vm = Create();
        vm.LoadFromDatabase();

        vm.SearchQuery = "dune";

        Assert.Equal(2, vm.StatusTabs[1].Count);
        Assert.Single(vm.Books);
    }

    [Fact]
    public void ActiveTabAndViewMode_Persist_ButNotTheWorkspaceLabel()
    {
        Seed("A", BookReadingStatus.Finished);
        var vm = Create();
        vm.LoadFromDatabase();

        vm.SetStatusTabCommand.Execute(BooksStatusTab.Finished);
        vm.SetViewModeCommand.Execute(BooksViewMode.Bookshelf);

        var again = Create();
        Assert.Equal(BooksStatusTab.Finished, again.ActiveStatusTab);
        Assert.Equal(BooksViewMode.Bookshelf, again.ViewMode);
        Assert.True(again.IsBookshelfView);
        Assert.False(again.IsGridView);
    }

    [Fact]
    public void EmptyTab_ExplainsItself_AndSearchOverridesTheMessage()
    {
        Seed("A", BookReadingStatus.Unread);
        var vm = Create();
        vm.LoadFromDatabase();

        vm.SetStatusTabCommand.Execute(BooksStatusTab.Abandoned);

        Assert.True(vm.ShowNoMatches);
        Assert.Contains("abandoned", vm.NoMatchesMessage);
        Assert.False(vm.HasSearch);

        vm.SearchQuery = "zzz";
        Assert.Equal("No books match “zzz”.", vm.NoMatchesMessage);
        Assert.True(vm.HasSearch);
    }

    [Fact]
    public void AuthorFilter_NarrowsTheGrid_AndClears()
    {
        Seed("Dune", BookReadingStatus.Unread, "Frank Herbert");
        Seed("Emma", BookReadingStatus.Unread, "Jane Austen");
        var vm = Create();
        vm.LoadFromDatabase();

        vm.AuthorFilter = "frank herbert";

        Assert.True(vm.HasAuthorFilter);
        Assert.Equal("Author: frank herbert", vm.AuthorFilterLabel);
        Assert.Equal(new[] { "Dune" }, vm.Books.Select(b => b.Title));

        vm.ClearAuthorFilterCommand.Execute(null);
        Assert.Equal(2, vm.Books.Count);
        Assert.False(vm.HasAuthorFilter);
    }

    [Fact]
    public void NewSortAndGroupAxes_HaveLabels_AndApply()
    {
        Seed("Low", BookReadingStatus.Unread, rating: 1f);
        Seed("High", BookReadingStatus.Unread, rating: 5f);
        Seed("Unrated", BookReadingStatus.Unread);
        var vm = Create();
        vm.LoadFromDatabase();

        vm.SetSortFieldCommand.Execute(BooksSortField.Rating);
        vm.SetSortFieldCommand.Execute(BooksSortField.Rating); // idempotent
        vm.ToggleSortDirectionCommand.Execute(null);

        Assert.Equal("Rating", vm.SortLabel);
        Assert.Equal(new[] { "High", "Low", "Unrated" }, vm.Books.Select(b => b.Title));

        vm.SetGroupFieldCommand.Execute(BooksGroupField.Status);
        Assert.Equal("Status", vm.GroupLabel);
        Assert.Equal(new[] { "Unread" }, vm.Groups.Select(g => g.Header));
    }

    private static void SeedInSeries(string title, string series, float? index, BookReadingStatus status = BookReadingStatus.Unread)
    {
        using var context = PaperbunkrDb.CreateContext();
        var bookSeries = context.BookSeries.FirstOrDefault(x => x.Name == series) ?? new BookSeries { Name = series };
        context.Books.Add(new Book
        {
            Title = title, BookSeries = bookSeries, SeriesIndex = index, Status = status, Format = BookFormat.Epub,
            FilePath = $@"C:\b\{title}.epub", AddedTime = DateTime.UtcNow,
        });
        context.SaveChanges();
    }

    [Fact]
    public void GroupedBySeries_ShowsGhosts_OnlyWhenNothingFiltersTheBooks()
    {
        SeedInSeries("Dune 1", "Dune", 1, BookReadingStatus.Finished);
        SeedInSeries("Dune 2", "Dune", 2);
        SeedInSeries("Dune 4", "Dune", 4);
        var vm = Create();
        vm.LoadFromDatabase();
        vm.SetGroupFieldCommand.Execute(BooksGroupField.Series);

        Assert.Contains(vm.Groups.Single().Items, c => c.IsGhost && c.GhostLabel == "#3");
        Assert.Equal(4, vm.Groups.Single().Items.Count);

        vm.SetStatusTabCommand.Execute(BooksStatusTab.Finished);
        Assert.DoesNotContain(vm.Groups.Single().Items, c => c.IsGhost);

        vm.SetStatusTabCommand.Execute(BooksStatusTab.All);
        vm.SearchQuery = "dune";
        Assert.DoesNotContain(vm.Groups.Single().Items, c => c.IsGhost);

        vm.SearchQuery = string.Empty;
        Assert.Contains(vm.Groups.Single().Items, c => c.IsGhost);
    }

    [Fact]
    public void Ghosts_AreInertToClicksAndNeverSelectable()
    {
        SeedInSeries("Dune 1", "Dune", 1);
        SeedInSeries("Dune 3", "Dune", 3);
        int opened = -1;
        var vm = new BooksScreenViewModel(id => opened = id, _ => { }, _ => { }, _ => { }, _ => { }, () => { });
        vm.LoadFromDatabase();
        vm.SetGroupFieldCommand.Execute(BooksGroupField.Series);
        var ghost = vm.Groups.Single().Items.Single(c => c.IsGhost);

        vm.CardClickCommand.Execute(ghost);

        Assert.Equal(-1, opened);
        Assert.False(vm.HasSelection);
        Assert.False(vm.ShowNoMatches);
    }

    [Fact]
    public void SeriesMode_ListsBooksInSeriesOrder_WithGhostsInSlot_AndBulkEditSkipsGhosts()
    {
        SeedInSeries("Zeta 2", "Saga", 2);
        SeedInSeries("Alpha 4", "Saga", 4);
        SeedInSeries("Beta 1", "Saga", 1);
        SeedInSeries("Extra", "Saga", null);
        int seriesId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            seriesId = context.BookSeries.Single().Id;
        }

        IReadOnlyList<int>? bulk = null;
        var detail = new BookDetailScreenViewModel(() => { }, (_, _, _) => { }, null, ids => bulk = ids, null);
        detail.LoadSeries(seriesId);

        // An unnumbered book in the series means we cannot place any ghost: no guessing.
        Assert.DoesNotContain(detail.SeriesBooks, c => c.IsGhost);
        Assert.Equal(new[] { "Beta 1", "Zeta 2", "Alpha 4", "Extra" }, detail.SeriesBooks.Select(c => c.Title));

        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Books.Remove(context.Books.Single(b => b.Title == "Extra"));
            context.SaveChanges();
        }

        detail.LoadSeries(seriesId);
        Assert.Equal(new[] { "Beta 1", "Zeta 2", "#3 missing", "Alpha 4" }, detail.SeriesBooks.Select(c => c.Title));

        detail.EditAllSeriesBooksCommand.Execute(null);
        Assert.Equal(3, bulk!.Count);
        Assert.DoesNotContain(0, bulk);
    }

    [Fact]
    public void Rail_ShowsForTitleSortInCoversView_AndHidesOtherwise()
    {
        Seed("Dune", BookReadingStatus.Unread, "Frank Herbert");
        Seed("Emma", BookReadingStatus.Unread, "Jane Austen");
        var vm = Create();
        vm.LoadFromDatabase();

        Assert.True(vm.ShowAlphabetIndex);
        Assert.Equal(27, vm.AlphabetIndex.Count);
        Assert.True(vm.AlphabetIndex.Single(e => e.Letter == "D").HasItems);
        Assert.False(vm.AlphabetIndex.Single(e => e.Letter == "Q").HasItems);

        vm.SetSortFieldCommand.Execute(BooksSortField.Rating);
        Assert.False(vm.ShowAlphabetIndex);

        vm.SetSortFieldCommand.Execute(BooksSortField.Author);
        Assert.True(vm.ShowAlphabetIndex);
        Assert.True(vm.AlphabetIndex.Single(e => e.Letter == "F").HasItems); // Frank Herbert's first name... filed by the author string as written

        // Books redesign §3.4: the rail shows in the bookshelf too (it jumps with ScrollIntoView, which a virtualized list supports).
        vm.SetViewModeCommand.Execute(BooksViewMode.Bookshelf);
        Assert.True(vm.ShowAlphabetIndex);
    }

    [Fact]
    public void AuthorStrip_CountsAllTheirBooks_NotJustTheVisibleOnes()
    {
        Seed("Dune", BookReadingStatus.Finished, "Frank Herbert");
        Seed("Messiah", BookReadingStatus.Finished, "Frank Herbert");
        Seed("Children", BookReadingStatus.Reading, "Frank Herbert");
        Seed("Emma", BookReadingStatus.Finished, "Jane Austen");
        var vm = Create();
        vm.LoadFromDatabase();

        Assert.Equal(string.Empty, vm.AuthorSummary);

        vm.AuthorFilter = "Frank Herbert";
        vm.SetStatusTabCommand.Execute(BooksStatusTab.Reading);

        Assert.Equal("3 books · 2 finished", vm.AuthorSummary);
        Assert.Equal(2d / 3d, vm.AuthorFinishedFraction, 3);
        Assert.Single(vm.Books); // the tab still narrows the grid

        vm.ClearAuthorFilterCommand.Execute(null);
        Assert.Equal(string.Empty, vm.AuthorSummary);
        Assert.Equal(0d, vm.AuthorFinishedFraction);
    }
}
