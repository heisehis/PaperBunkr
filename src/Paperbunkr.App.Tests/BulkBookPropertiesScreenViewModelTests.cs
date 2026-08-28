using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="BulkBookPropertiesScreenViewModel"/> - the multi-book editor
/// (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-design.md, component b). Only
/// ticked fields are written; series applies to every selected book; emptied old series prune;
/// one multi-id undo entry.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BulkBookPropertiesScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public BulkBookPropertiesScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bulkbook_test_{Guid.NewGuid():N}.db");
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

    private static BulkBookPropertiesScreenViewModel CreateViewModel(Action? goBack = null, MetadataEditHistoryService? history = null) =>
        new(goBack ?? (() => { }), PaperbunkrDb.CreateContext, null, history ?? new MetadataEditHistoryService());

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
    public void OnlyStagedFieldsAreWritten()
    {
        int a = AddBook("A", author: "OldA", summary: "SumA");
        int b = AddBook("B", author: "OldB", summary: "SumB");
        var vm = CreateViewModel();
        vm.Load(new[] { a, b });

        vm.Author = "NewAuthor"; // auto-stages Author only
        vm.SaveCommand.Execute(null);

        Assert.Equal("NewAuthor", GetBook(a).Author);
        Assert.Equal("NewAuthor", GetBook(b).Author);
        Assert.Equal("SumA", GetBook(a).Summary); // untouched
        Assert.Equal("SumB", GetBook(b).Summary);
    }

    [Fact]
    public void MixedValues_LoadBlank_AgreedValues_Prefill()
    {
        int a = AddBook("A", author: "Same");
        int b = AddBook("B", author: "Same");
        int c = AddBook("C", author: "Different");

        var agree = CreateViewModel();
        agree.Load(new[] { a, b });
        Assert.Equal("Same", agree.Author);
        Assert.Equal(string.Empty, agree.AuthorWatermark);
        Assert.False(agree.HasUnsavedChanges());

        var mixed = CreateViewModel();
        mixed.Load(new[] { a, c });
        Assert.Equal(string.Empty, mixed.Author);
        Assert.NotEqual(string.Empty, mixed.AuthorWatermark);
    }

    [Fact]
    public void UnstagedPrefilledField_LeftAloneOnSave()
    {
        int a = AddBook("A", author: "Same");
        int b = AddBook("B", author: "Same");
        var vm = CreateViewModel();
        vm.Load(new[] { a, b });

        vm.Summary = "only summary"; // stage summary, leave Author prefilled+unstaged
        vm.SaveCommand.Execute(null);

        Assert.Equal("Same", GetBook(a).Author);
        Assert.Equal("only summary", GetBook(a).Summary);
    }

    [Fact]
    public void StagedSeriesName_AppliedToAll_OldSeriesPruned()
    {
        int a = AddBook("A", series: "Old One");
        int b = AddBook("B", series: "Old Two");
        var vm = CreateViewModel();
        vm.Load(new[] { a, b });

        vm.SeriesName = "United";
        vm.SaveCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        int unitedId = context.BookSeries.Single(s => s.Name == "United").Id;
        Assert.Equal(unitedId, context.Books.Single(x => x.Id == a).BookSeriesId);
        Assert.Equal(unitedId, context.Books.Single(x => x.Id == b).BookSeriesId);
        Assert.Empty(context.BookSeries.Where(s => s.Name == "Old One" || s.Name == "Old Two"));
    }

    [Fact]
    public void BlankSeriesName_DetachesAll()
    {
        int a = AddBook("A", series: "S");
        int b = AddBook("B", series: "S");
        var vm = CreateViewModel();
        vm.Load(new[] { a, b });

        vm.SeriesName = ""; // OnSeriesNameChanged still stages it (a real edit from a prefilled "S")
        vm.SaveCommand.Execute(null);

        Assert.Null(GetBook(a).BookSeriesId);
        Assert.Null(GetBook(b).BookSeriesId);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.BookSeries.Where(s => s.Name == "S"));
    }

    [Fact]
    public void SeriesAuthorAndSortName_LandOnResolvedRow()
    {
        int a = AddBook("A");
        int b = AddBook("B");
        var vm = CreateViewModel();
        vm.Load(new[] { a, b });

        vm.SeriesName = "Fresh";
        vm.SeriesAuthor = "SA";
        vm.SeriesSortName = "Fresh, The";
        vm.SaveCommand.Execute(null);

        using var context = PaperbunkrDb.CreateContext();
        var series = context.BookSeries.Single(s => s.Name == "Fresh");
        Assert.Equal("SA", series.Author);
        Assert.Equal("Fresh, The", series.SortName);
    }

    [Fact]
    public void Save_RecordsOneMultiBookHistoryEntry_UndoRestoresAll()
    {
        int a = AddBook("A", author: "OrigA");
        int b = AddBook("B", author: "OrigB");
        var history = new MetadataEditHistoryService();
        var vm = CreateViewModel(history: history);
        vm.Load(new[] { a, b });

        vm.Author = "Bulk";
        vm.SaveCommand.Execute(null);

        Assert.True(history.CanUndo);
        history.Undo(PaperbunkrDb.CreateContext);
        Assert.Equal("OrigA", GetBook(a).Author);
        Assert.Equal("OrigB", GetBook(b).Author);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void NothingStaged_Save_NoHistoryEntry()
    {
        int a = AddBook("A", author: "Same");
        int b = AddBook("B", author: "Same");
        var history = new MetadataEditHistoryService();
        var vm = CreateViewModel(history: history);
        vm.Load(new[] { a, b });

        vm.SaveCommand.Execute(null);

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void HasUnsavedChanges_Transitions()
    {
        int a = AddBook("A");
        var vm = CreateViewModel();
        vm.Load(new[] { a });

        Assert.False(vm.HasUnsavedChanges());
        vm.ApplySummary = true;
        Assert.True(vm.HasUnsavedChanges());
    }

    [Fact]
    public void Cancel_WritesNothing()
    {
        int a = AddBook("A", author: "Keep");
        var vm = CreateViewModel();
        vm.Load(new[] { a });

        vm.Author = "Discarded";
        vm.CancelCommand.Execute(null);

        Assert.Equal("Keep", GetBook(a).Author);
    }
}
