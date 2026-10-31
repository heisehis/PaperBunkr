using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>Book Detail's jacket and Series mode (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §4), and Home's book rows (§7.4).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookDetailJacketTests : IDisposable
{
    private readonly string? _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_jacket_test_{Guid.NewGuid():N}.db");

    public BookDetailJacketTests()
    {
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static (int SeriesId, int[] Books) SeedSeries()
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new BookSeries { Name = "The Orchard Cycle", Author = "Maarja Ilves" };
        context.BookSeries.Add(series);
        context.SaveChanges();

        Book B(string title, float index, BookReadingStatus status, DateTime? opened = null, double? fraction = null) => new()
        {
            Title = title, Author = "Maarja Ilves", Format = BookFormat.Pdf, FilePath = $@"C:\books\{title}.pdf", AddedTime = DateTime.UtcNow,
            BookSeriesId = series.Id, SeriesIndex = index, Status = status, LastOpenedTime = opened, LastProgressionFraction = fraction,
            PublishedDate = new DateTime(2019, 3, 14), Publisher = "Harrow & Finch",
        };

        var books = new[]
        {
            B("The Salt Year", 1, BookReadingStatus.Finished),
            B("The Glass Orchard", 2, BookReadingStatus.Reading, DateTime.UtcNow, 0.4),
            B("An Atlas of Quiet", 4, BookReadingStatus.WantToRead),
        };
        context.Books.AddRange(books);
        context.SaveChanges();
        return (series.Id, books.Select(b => b.Id).ToArray());
    }

    private static BookDetailScreenViewModel Create(Action<int, BookFormat, BookPosition?>? reader = null) =>
        new(() => { }, reader ?? ((_, _, _) => { }), _ => { }, _ => { }, _ => { });

    [Fact]
    public void Book_JacketHasOverlineSeriesPartEditionFactsAndSpines()
    {
        var (_, books) = SeedSeries();
        var vm = Create();

        vm.LoadBook(books[1]);

        Assert.Equal("PDF · 2019", vm.Overline);
        Assert.Equal("The Orchard Cycle #2", vm.SeriesPart);
        Assert.Contains(vm.EditionFacts, f => f.Label == "Publisher" && f.Value == "Harrow & Finch");
        Assert.True(vm.HasRibbon); // a PDF gets one bar for the whole book
        Assert.Equal(4, vm.SeriesSpines.Count); // #1, #2, ghost #3, #4
        Assert.True(vm.SeriesSpines.Single(s => s.BookId == books[1]).IsSelected);
        Assert.Single(vm.SeriesSpines, s => s.IsGhost);
        Assert.Equal(2, vm.MoreByAuthor.Count);
        Assert.Equal("book-cover:" + books[1], vm.JacketFlightKey);
    }

    [Fact]
    public void Series_HasStackRibbonAndContinueAtTheBookInProgress()
    {
        var (seriesId, books) = SeedSeries();
        (int Id, BookFormat Format)? opened = null;
        var vm = Create((id, format, _) => opened = (id, format));

        vm.LoadSeries(seriesId);

        Assert.Equal(3, vm.SeriesStack.Count);
        Assert.Equal(4, vm.RibbonSegments.Count);
        Assert.Equal(1, vm.RibbonSegments[0].Fill);
        Assert.True(vm.RibbonSegments[2].IsGhost);
        Assert.StartsWith("SERIES · 4 BOOKS · 1 MISSING", vm.SeriesOverline);
        Assert.StartsWith("Continue · Book 2", vm.SeriesContinueLabel);
        Assert.Contains("NEXT: AN ATLAS OF QUIET", vm.RibbonCaption);

        vm.ContinueSeriesCommand.Execute(null);
        Assert.Equal((books[1], BookFormat.Pdf), opened);
    }

    [Fact]
    public void RibbonSegment_OpensTheBookInSeriesMode()
    {
        var (seriesId, books) = SeedSeries();
        var vm = Create();
        vm.LoadSeries(seriesId);

        vm.OpenRibbonSegmentCommand.Execute(vm.RibbonSegments[0].Tag);

        Assert.True(vm.IsBookMode);
        Assert.Equal("The Salt Year", vm.Title);
    }

    [Fact]
    public void HomeBookRow_OpensDetailWhenGiven()
    {
        int? detail = null;
        bool reader = false;
        var home = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => reader = true,
            loadOnConstruction: false, goBookDetail: id => detail = id);

        home.OpenBookRowCommand.Execute(new HomeResumeCard { Book = new HomeBookCard { BookId = 42 } });

        Assert.Equal(42, detail);
        Assert.False(reader);
    }
}
