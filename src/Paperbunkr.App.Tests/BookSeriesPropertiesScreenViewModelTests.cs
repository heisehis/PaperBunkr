using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="BookSeriesPropertiesScreenViewModel"/> - the BookSeries rename + metadata editor
/// (docs/superpowers/specs/2026-08-27-books-bulk-series-editing-design.md, component c).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookSeriesPropertiesScreenViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public BookSeriesPropertiesScreenViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookseriesprops_test_{Guid.NewGuid():N}.db");
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

    private static BookSeriesPropertiesScreenViewModel CreateViewModel(Action? goBack = null, Action<string, string>? notify = null) =>
        new(goBack ?? (() => { }), PaperbunkrDb.CreateContext, notify);

    private static int AddSeries(string name, string? author = null, string? sortName = null, string? bookTitle = "Member")
    {
        using var context = PaperbunkrDb.CreateContext();
        var s = new BookSeries { Name = name, Author = author, SortName = sortName };
        context.BookSeries.Add(s);
        context.SaveChanges();
        if (bookTitle is not null)
        {
            context.Books.Add(new Book { Title = bookTitle, BookSeriesId = s.Id, Format = BookFormat.Epub, FilePath = $@"C:\b\{bookTitle}.epub" });
            context.SaveChanges();
        }
        return s.Id;
    }

    private static BookSeries GetSeries(int id)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.BookSeries.Single(s => s.Id == id);
    }

    [Fact]
    public void Rename_InPlace_KeepsMemberships()
    {
        int id = AddSeries("Old Name", bookTitle: "Book One");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.Name = "New Name";
        vm.SaveCommand.Execute(null);

        var series = GetSeries(id);
        Assert.Equal("New Name", series.Name);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal(id, context.Books.Single(b => b.Title == "Book One").BookSeriesId);
    }

    [Fact]
    public void BlankName_Blocked()
    {
        int id = AddSeries("Keep");
        bool notified = false;
        var vm = CreateViewModel(notify: (_, _) => notified = true);
        vm.Load(id);

        vm.Name = "   ";
        vm.SaveCommand.Execute(null);

        Assert.True(notified);
        Assert.Equal("Keep", GetSeries(id).Name);
    }

    [Fact]
    public void CollidingName_DifferentCase_Blocked()
    {
        AddSeries("Existing Series", bookTitle: "X");
        int id = AddSeries("Other", bookTitle: "Y");
        bool notified = false;
        var vm = CreateViewModel(notify: (_, _) => notified = true);
        vm.Load(id);

        vm.Name = "existing series";
        vm.SaveCommand.Execute(null);

        Assert.True(notified);
        Assert.Equal("Other", GetSeries(id).Name);
    }

    [Fact]
    public void SameName_DifferentCasingOnly_IsAllowed()
    {
        int id = AddSeries("The Series");
        var vm = CreateViewModel();
        vm.Load(id);

        vm.Name = "the series";
        vm.SaveCommand.Execute(null);

        Assert.Equal("the series", GetSeries(id).Name);
    }

    [Fact]
    public void SortNameAndAuthor_RoundTrip_NullIfEmpty()
    {
        int id = AddSeries("S", author: "A", sortName: "S, The");
        var vm = CreateViewModel();
        vm.Load(id);
        Assert.Equal("A", vm.Author);
        Assert.Equal("S, The", vm.SortName);

        vm.Author = "";
        vm.SortName = "Sorted";
        vm.SaveCommand.Execute(null);

        var series = GetSeries(id);
        Assert.Null(series.Author);
        Assert.Equal("Sorted", series.SortName);
    }

    [Fact]
    public void HasUnsavedChanges_Transitions()
    {
        int id = AddSeries("S", author: "A");
        var vm = CreateViewModel();
        vm.Load(id);

        Assert.False(vm.HasUnsavedChanges());
        vm.Author = "B";
        Assert.True(vm.HasUnsavedChanges());
        vm.Author = "A";
        Assert.False(vm.HasUnsavedChanges());
    }

    [Fact]
    public void Load_MissingRow_CallsGoBack()
    {
        bool wentBack = false;
        var vm = CreateViewModel(goBack: () => wentBack = true);
        vm.Load(123456);
        Assert.True(wentBack);
    }
}
