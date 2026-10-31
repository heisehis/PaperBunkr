using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>The Books bookshelf view (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §5).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BookshelfViewTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public BookshelfViewTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookshelf_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public void SpineWidth_GrowsWithLength_ClampsAtBothEnds_AndDefaultsForPdfOrUnparsed()
    {
        double thin = BookSpineWidth.For(null, BookFormat.Epub);
        double pdf = BookSpineWidth.For(900_000, BookFormat.Pdf);
        double mid = BookSpineWidth.For(180_000, BookFormat.Epub);   // ~100 pages
        double fat = BookSpineWidth.For(1_800_000, BookFormat.Epub); // ~1000 pages

        Assert.Equal(thin, pdf);
        Assert.True(mid > thin);
        Assert.True(fat > mid);
        Assert.Equal(54, fat); // the Library's issue-spine ceiling
        Assert.True(BookSpineWidth.For(0, BookFormat.Epub) >= SpineMetrics.MinWidth);
    }

    [Fact]
    public void Card_IsASpineSource_WithABookOnlyCoverKey()
    {
        var card = new BookCardSample
        {
            BookId = 42, Title = "Dune", Format = BookFormat.Epub, CharacterCount = 500_000, SeriesIndex = 2.5f,
            Status = BookReadingStatus.Reading, Progress = 0.4,
        };

        Assert.Equal("Dune", card.SpineTitle);
        Assert.Equal("#2.5", card.SpineNumber);
        Assert.Equal(BookSpineWidth.For(500_000, BookFormat.Epub), card.SpineWidth);
        Assert.Equal(SpineMetrics.HeightFor(42), card.SpineHeight);
        Assert.Equal("book:42", card.CoverKey); // never the bare number, which would read the comic cover with the same id
        Assert.Equal(0.4, card.ReadFraction);
        Assert.False(card.IsFinished);
        Assert.False(card.IsRightToLeft);

        Assert.Null(new BookCardSample { Title = "x" }.SpineNumber);
        Assert.True(new BookCardSample { Status = BookReadingStatus.Finished }.IsFinished);
    }

    [Fact]
    public void LayoutFlags_FollowViewModeAndGrouping()
    {
        var vm = new BooksScreenViewModel(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { });

        Assert.True(vm.IsGridUngrouped);
        Assert.False(vm.IsShelfUngrouped);

        vm.SetViewModeCommand.Execute(BooksViewMode.Bookshelf);
        Assert.True(vm.IsShelfUngrouped);
        Assert.False(vm.IsGridUngrouped);

        vm.SetGroupFieldCommand.Execute(BooksGroupField.Publisher);
        Assert.True(vm.IsShelfGrouped);
        Assert.False(vm.IsShelfUngrouped);

        vm.SetViewModeCommand.Execute(BooksViewMode.Grid);
        Assert.True(vm.IsGridGrouped);
        Assert.False(vm.IsShelfGrouped);
    }

    [Fact]
    public void BookCoverDecode_ForAMissingCover_IsNull()
    {
        Assert.Null(BookCoverImageCache.DecodeFromDisk("987654321"));
    }
}
