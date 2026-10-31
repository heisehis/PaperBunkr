using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Books;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.BooksRedesign;

/// <summary>The spread and nightstand on <see cref="BooksScreenViewModel"/> (docs/superpowers/specs/2026-10-27-books-section-redesign-design.md §3.1).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class BooksSpreadViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_books_spread_test_{Guid.NewGuid():N}.db");

    public BooksSpreadViewModelTests()
    {
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

    private static int Seed(string title, BookReadingStatus status, DateTime? opened = null, string? excerpt = null, string? author = null,
        int chapterIndex = 0, int chapterCount = 0)
    {
        using var context = PaperbunkrDb.CreateContext();
        var book = new Book
        {
            Title = title,
            Author = author,
            Format = BookFormat.Epub,
            FilePath = $@"C:\books\{title}.epub",
            AddedTime = DateTime.UtcNow,
            Status = status,
            LastOpenedTime = opened,
            LastExcerpt = excerpt,
            LastChapterIndex = chapterIndex,
            ChapterCount = chapterCount,
        };
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private static BooksScreenViewModel Create(Action<int>? goDetail = null) =>
        new(goDetail ?? (_ => { }), _ => { }, _ => { }, _ => { }, _ => { }, () => { });

    [Fact]
    public void ReadingBook_FillsTheSpreadWithItsQuoteAndChapter()
    {
        int id = Seed("The Glass Orchard", BookReadingStatus.Reading, DateTime.UtcNow, "They only remembered how.", "Maarja Ilves", 8, 24);

        var vm = Create();

        Assert.NotNull(vm.Spread);
        Assert.Equal(id, vm.Spread!.Card.BookId);
        Assert.Equal(BookSpreadMode.NowReading, vm.Spread.Mode);
        Assert.Equal("They only remembered how.", vm.Spread.Quote);
        Assert.StartsWith("Ch. 9 of 24", vm.Spread.MetaLine);
        Assert.Equal("Continue reading", vm.Spread.ActionLabel);
        Assert.True(vm.ShowSpread);
    }

    [Fact]
    public void AnyFilter_HidesTheSpread()
    {
        Seed("The Glass Orchard", BookReadingStatus.Reading, DateTime.UtcNow, author: "Maarja Ilves");
        var vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SearchQuery = "glass";
        Assert.False(vm.ShowSpread);
        Assert.Contains(nameof(BooksScreenViewModel.ShowSpread), changed);

        vm.SearchQuery = string.Empty;
        Assert.True(vm.ShowSpread);

        vm.ActiveStatusTab = BooksStatusTab.Reading;
        Assert.False(vm.ShowSpread);

        vm.ActiveStatusTab = BooksStatusTab.All;
        vm.AuthorFilter = "Maarja Ilves";
        Assert.False(vm.ShowSpread);
    }

    [Fact]
    public void EmptyLibrary_HasNoSpread()
    {
        var vm = Create();

        Assert.Null(vm.Spread);
        Assert.False(vm.ShowSpread);
    }

    [Fact]
    public void Nightstand_HoldsTheOtherReadingBooks()
    {
        Seed("Newest", BookReadingStatus.Reading, DateTime.UtcNow);
        int older = Seed("Older", BookReadingStatus.Reading, DateTime.UtcNow.AddDays(-2));

        var vm = Create();

        Assert.True(vm.HasNightstand);
        Assert.Equal(older, Assert.Single(vm.Nightstand).BookId);
    }

    [Fact]
    public void Continue_OpensTheReader_CoverOpensDetail()
    {
        int id = Seed("The Glass Orchard", BookReadingStatus.Reading, DateTime.UtcNow);
        int? detail = null;
        (int Id, BookFormat Format)? reader = null;
        var vm = Create(goDetail: b => detail = b);
        vm.GoReaderForBook = (b, f) => reader = (b, f);

        vm.ContinueSpreadCommand.Execute(null);
        vm.OpenSpreadBookCommand.Execute(null);

        Assert.Equal((id, BookFormat.Epub), reader);
        Assert.Equal(id, detail);
    }

    [Fact]
    public void ThisWeek_ComesFromNovelReadingEvents()
    {
        int id = Seed("The Glass Orchard", BookReadingStatus.Reading, DateTime.UtcNow);
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Novel, ItemId = id, Kind = ReadingEventKind.Opened,
                TimestampUtc = DateTime.UtcNow.AddHours(-3), ActiveSeconds = 1800, PagesRead = 20,
            });
            context.SaveChanges();
        }

        var vm = Create();

        Assert.Equal("This week · 30m read · 20 pages", vm.ThisWeekLine);
        Assert.True(vm.HasThisWeek);
    }
}
