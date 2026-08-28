using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="BookPropertiesScreenViewModel"/> (docs/superpowers/specs/2026-08-27-book-
/// properties-editor-design.md, Piece B2). Temp SQLite via
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/>, same pattern as
/// <see cref="BooksScreenViewModelTests"/>; <see cref="AvaloniaTestCollection"/> for cover-bitmap
/// construction.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookPropertiesScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public BookPropertiesScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookprops_test_{Guid.NewGuid():N}.db");
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

    private static BookPropertiesScreenViewModel CreateViewModel(
        Action? goBack = null, Action<string, string>? notify = null, MetadataEditHistoryService? history = null) =>
        new(goBack ?? (() => { }), PaperbunkrDb.CreateContext, notify, history ?? new MetadataEditHistoryService());

    private static int AddBook(string title, string? author = null, string? summary = null, string? series = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        int? seriesId = null;
        if (series is not null)
        {
            var bs = context.BookSeries.FirstOrDefault(s => s.Name == series) ?? new BookSeries { Name = series };
            if (bs.Id == 0) { context.BookSeries.Add(bs); context.SaveChanges(); }
            seriesId = bs.Id;
        }

        var book = new Book { Title = title, Author = author, Summary = summary, BookSeriesId = seriesId, Format = BookFormat.Epub, FilePath = $@"C:\b\{title}.epub" };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private static Book GetBook(int id)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Books.Single(b => b.Id == id);
    }

    [Fact]
    public void SaveRoundTripsScalarFields()
    {
        int id = AddBook("Old");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.Title = "New Title";
        vm.Author = "New Author";
        vm.Summary = "New summary.";
        vm.PublishedDate = new DateTimeOffset(new DateTime(2001, 2, 3, 0, 0, 0, DateTimeKind.Utc));
        vm.SaveCommand.Execute(null);

        var book = GetBook(id);
        Assert.Equal("New Title", book.Title);
        Assert.Equal("New Author", book.Author);
        Assert.Equal("New summary.", book.Summary);
        Assert.Equal(new DateTime(2001, 2, 3, 0, 0, 0, DateTimeKind.Utc), book.PublishedDate);
    }

    [Fact]
    public void BlankTitle_BlocksSave_AndNotifies()
    {
        int id = AddBook("Keep");
        bool notified = false;
        var vm = CreateViewModel(notify: (_, _) => notified = true);
        vm.Load(id);

        vm.Title = "   ";
        vm.SaveCommand.Execute(null);

        Assert.True(notified);
        Assert.Equal("Keep", GetBook(id).Title);
    }

    [Fact]
    public void Series_BlankName_DetachesToStandalone()
    {
        int id = AddBook("Bk", series: "Some Series");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.SeriesName = "";
        vm.SaveCommand.Execute(null);

        Assert.Null(GetBook(id).BookSeriesId);
    }

    [Fact]
    public void Series_NewName_CreatesRowAndAttaches()
    {
        int id = AddBook("Bk");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.SeriesName = "Brand New Series";
        vm.SaveCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        var book = context.Books.Single(b => b.Id == id);
        Assert.NotNull(book.BookSeriesId);
        Assert.Equal("Brand New Series", context.BookSeries.Single(s => s.Id == book.BookSeriesId).Name);
    }

    [Fact]
    public void Series_ExistingNameDifferentCase_ReusesRow_NoDuplicate()
    {
        AddBook("Sibling", series: "Dune Chronicles");
        int id = AddBook("Bk");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.SeriesName = "dune chronicles";
        vm.SaveCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Single(context.BookSeries.Where(s => s.Name == "Dune Chronicles"));
        var book = context.Books.Single(b => b.Id == id);
        Assert.Equal(context.BookSeries.Single(s => s.Name == "Dune Chronicles").Id, book.BookSeriesId);
    }

    [Fact]
    public void SeriesAuthorAndSortName_LandOnResolvedRow_VisibleToSibling()
    {
        int siblingId = AddBook("Sibling", series: "Trilogy");
        int id = AddBook("Bk");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.SeriesName = "Trilogy";
        vm.SeriesAuthor = "Shared Author";
        vm.SeriesSortName = "Trilogy, The";
        vm.SaveCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        int seriesId = context.Books.Single(b => b.Id == siblingId).BookSeriesId!.Value;
        var series = context.BookSeries.Single(s => s.Id == seriesId);
        Assert.Equal("Shared Author", series.Author);
        Assert.Equal("Trilogy, The", series.SortName);
    }

    [Fact]
    public void HasSeriesName_FalseWhenBlank()
    {
        int id = AddBook("Bk");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.SeriesName = "";
        Assert.False(vm.HasSeriesName);
        vm.SeriesName = "X";
        Assert.True(vm.HasSeriesName);
    }

    [Fact]
    public void HasUnsavedChanges_Transitions()
    {
        int id = AddBook("Bk", author: "A");
        var vm = CreateViewModel();
        vm.Load(id);

        Assert.False(vm.HasUnsavedChanges());
        vm.Author = "B";
        Assert.True(vm.HasUnsavedChanges());
        vm.Author = "A";
        Assert.False(vm.HasUnsavedChanges());

        vm.ResetCoverCommand.Execute(null);
        Assert.True(vm.HasUnsavedChanges());
    }

    [Fact]
    public void Cancel_WritesNothing()
    {
        int id = AddBook("Original");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.Title = "Changed";
        vm.CancelCommand.Execute(null);

        Assert.Equal("Original", GetBook(id).Title);
    }

    [Fact]
    public void Save_RecordsExactlyOneHistoryEntry_WithBookKeysOnly()
    {
        int id = AddBook("Old");
        var history = new MetadataEditHistoryService();
        var vm = CreateViewModel(history: history);
        vm.Load(id);

        vm.Title = "New";
        vm.SeriesName = "S";
        vm.SeriesAuthor = "SA";
        vm.SaveCommand.Execute(null);

        Assert.True(history.CanUndo);
        // Undo restores the book title but NOT the series author (out of undo scope).
        history.Undo(PaperbunkrDb.CreateContext);
        var book = GetBook(id);
        Assert.Equal("Old", book.Title);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal("SA", context.BookSeries.Single(s => s.Name == "S").Author);
        Assert.False(history.CanUndo); // only one entry was recorded
    }

    [Fact]
    public void Save_DetachingLastBookFromSeries_PrunesThatSeries()
    {
        int id = AddBook("Only", series: "Solo Series");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.SeriesName = "";
        vm.SaveCommand.Execute(null);

        Assert.Null(GetBook(id).BookSeriesId);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.BookSeries.Where(s => s.Name == "Solo Series"));
    }

    [Fact]
    public void Save_ReassigningLastBook_PrunesTheVacatedSeries_KeepsPopulatedOne()
    {
        AddBook("Sibling", series: "Destination");
        int id = AddBook("Mover", series: "Vacated");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.SeriesName = "Destination";
        vm.SaveCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.BookSeries.Where(s => s.Name == "Vacated"));
        Assert.Single(context.BookSeries.Where(s => s.Name == "Destination"));
    }

    [Fact]
    public void Load_MissingBook_CallsGoBack()
    {
        bool wentBack = false;
        var vm = CreateViewModel(goBack: () => wentBack = true);

        vm.Load(4242);

        Assert.True(wentBack);
    }
}
