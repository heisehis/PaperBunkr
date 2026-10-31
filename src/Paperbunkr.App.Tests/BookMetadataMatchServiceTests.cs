using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Books;

namespace Paperbunkr.App.Tests;

/// <summary>Match dialog + bulk job logic (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §5) against a fake catalogue.</summary>
public class BookMetadataMatchServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public BookMetadataMatchServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bookmatch_db_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private sealed class FakeProvider : IBookMetadataProvider
    {
        public Func<string?, string, string?, IReadOnlyList<BookProviderRecord>> Search { get; set; } = (_, _, _) => Array.Empty<BookProviderRecord>();

        public Func<string, BookProviderRecord?> Get { get; set; } = _ => null;

        public BookFieldSourceKind Kind => BookFieldSourceKind.OpenLibrary;

        public string DisplayName => "Fake";

        public Task<IReadOnlyList<BookProviderRecord>> SearchAsync(string? isbn, string title, string? author, CancellationToken ct) =>
            Task.FromResult(Search(isbn, title, author));

        public Task<BookProviderRecord?> GetAsync(string id, CancellationToken ct) => Task.FromResult(Get(id));
    }

    private BookMetadataMatchService Service(FakeProvider provider, List<int>? writeBacks = null, Func<string, CancellationToken, Task<byte[]?>>? cover = null) =>
        new(() => new PaperbunkrDbContext(_options), () => new IBookMetadataProvider[] { provider }, cover,
            ids => { writeBacks?.AddRange(ids); return Task.CompletedTask; });

    private static BookProviderRecord Rec(string id, string title, string author, string? isbn = null) =>
        new(BookFieldSourceKind.OpenLibrary, id, title, null, new[] { author }, "Ace", 1965, "en", isbn,
            new[] { "Science Fiction" }, null, "Dune Chronicles", 1f, 4.2f, 100, "https://example.invalid/c.jpg");

    private int AddBook(string title, string? author = null, Action<Book>? configure = null)
    {
        var book = new Book { Title = title, Author = author, Format = BookFormat.Epub, FilePath = $@"C:\b\{title}.epub", AddedTime = DateTime.UtcNow };
        configure?.Invoke(book);
        using var context = new PaperbunkrDbContext(_options);
        context.Books.Add(book);
        context.SaveChanges();
        return book.Id;
    }

    private Book Get(int id)
    {
        using var context = new PaperbunkrDbContext(_options);
        return context.Books.Include(b => b.Tags).Include(b => b.FieldSources).Include(b => b.BookSeries).Single(b => b.Id == id);
    }

    [Fact]
    public async Task Search_RanksCandidates_AndUsesOverrides()
    {
        int id = AddBook("Dune", "Frank Herbert");
        string? seenTitle = null;
        var provider = new FakeProvider
        {
            Search = (_, title, _) =>
            {
                seenTitle = title;
                return new[] { Rec("b", "Dune Messiah", "Frank Herbert"), Rec("a", "Dune", "Frank Herbert") };
            },
        };

        var results = await Service(provider).SearchAsync(id, null, null, CancellationToken.None);
        Assert.Equal("Dune", seenTitle);
        Assert.Equal("a", results[0].Record.Id);

        await Service(provider).SearchAsync(id, "Children of Dune", null, CancellationToken.None);
        Assert.Equal("Children of Dune", seenTitle);
    }

    [Fact]
    public void BuildDiff_ListsOnlyDifferingNonEmptyFields_AndPreTicksEmptyOnes()
    {
        int id = AddBook("Dune", "Frank Herbert", b => { b.Publisher = "My Pub"; b.Tags.Add(new BookTag { Field = BookTagField.Genre, Value = "SF" }); });
        var record = Rec("a", "Dune", "Frank Herbert", "9780441172719");

        var diff = Service(new FakeProvider()).BuildDiff(id, record).ToDictionary(d => d.Field);

        Assert.False(diff[BookFields.Publisher].DefaultChecked); // had a value: not pre-ticked
        Assert.Equal("My Pub", diff[BookFields.Publisher].Current);
        Assert.True(diff[BookFields.Isbn].DefaultChecked);
        Assert.True(diff[BookFields.Series].DefaultChecked);
        Assert.Equal("Science Fiction", diff[BookFields.Genre].Proposed);
        Assert.DoesNotContain(BookFields.Summary, diff.Keys); // the record has no description
    }

    [Fact]
    public async Task Apply_WritesOnlyTheAcceptedFields_WithProviderSources_AndAsksForWriteBack()
    {
        int id = AddBook("Dune", "Frank Herbert", b => b.Publisher = "My Pub");
        var writeBacks = new List<int>();
        var service = Service(new FakeProvider(), writeBacks);

        var changed = await service.ApplyAsync(id, Rec("OL1W", "Dune", "Frank Herbert", "9780441172719"),
            new HashSet<string> { BookFields.Publisher, BookFields.Isbn, BookFields.Series }, applyCover: false, CancellationToken.None);

        var book = Get(id);
        Assert.Equal("Ace", book.Publisher); // explicit override of an existing value
        Assert.Equal("9780441172719", book.ISBN);
        Assert.Equal("Dune Chronicles", book.BookSeries!.Name);
        Assert.Null(book.LanguageISO); // not accepted
        Assert.Equal(3, changed.Count);
        var source = book.FieldSources.Single(s => s.Field == BookFields.Publisher);
        Assert.Equal(BookFieldSourceKind.OpenLibrary, source.Source);
        Assert.Equal("OL1W", source.SourceRef);
        Assert.Equal(new[] { id }, writeBacks);
    }

    [Fact]
    public async Task GetFull_MergesTheDetailRecord()
    {
        var provider = new FakeProvider
        {
            Get = _ => Rec("a", "Dune", "x") with { Description = "A desert planet.", Subjects = new[] { "Deserts" } },
        };

        var full = await Service(provider).GetFullAsync(Rec("a", "Dune", "x"), CancellationToken.None);

        Assert.Equal("A desert planet.", full.Description);
        Assert.Equal(new[] { "Deserts" }, full.Subjects);
    }

    [Fact]
    public async Task Bulk_AppliesAClearWinner_FillOnly_AndCountsTheRest()
    {
        int clear = AddBook("Dune", "Frank Herbert", b => b.Publisher = "Mine");
        int vague = AddBook("Common Title", "Someone");
        int nothing = AddBook("Obscure Thing", "Nobody");
        int skip = AddBook("Skipped", "X", b => b.SkipMetadataMatch = true);
        var provider = new FakeProvider
        {
            Search = (_, title, _) => title switch
            {
                "Dune" => new[] { Rec("a", "Dune", "Frank Herbert") },
                "Common Title" => new[] { Rec("c1", "Common Title", "Someone"), Rec("c2", "Common Title", "Someone") },
                _ => Array.Empty<BookProviderRecord>(),
            },
            Get = _ => null,
        };

        var result = await Service(provider).RunBulkAsync(new[] { clear, vague, nothing, skip }, null, CancellationToken.None);

        Assert.Equal(1, result.Matched);
        Assert.Equal(1, result.NeedsReview);
        Assert.Equal(1, result.NoMatch);
        Assert.Equal(1, result.Skipped);
        Assert.False(result.RateLimited);
        var dune = Get(clear);
        Assert.Equal("Mine", dune.Publisher); // fill-only: the existing value survives
        Assert.Equal("en", dune.LanguageISO);
        Assert.Null(Get(vague).LanguageISO);
        Assert.Contains("matched", result.Describe());
    }

    [Fact]
    public async Task Bulk_StopsWhenRateLimited_AndTellsYou()
    {
        int a = AddBook("A", "x");
        int b = AddBook("B", "x");
        int calls = 0;
        var provider = new FakeProvider
        {
            Search = (_, _, _) =>
            {
                calls++;
                throw new BookProviderException("slow down", isRateLimited: true);
            },
        };

        var result = await Service(provider).RunBulkAsync(new[] { a, b }, null, CancellationToken.None);

        Assert.True(result.RateLimited);
        Assert.Equal(1, calls);
        Assert.Contains("slow down", result.Describe());
    }

    [Fact]
    public async Task Search_AllProvidersFailing_Throws_ButOneFailingIsTolerated()
    {
        int id = AddBook("Dune", "Frank Herbert");
        var failing = new FakeProvider { Search = (_, _, _) => throw new BookProviderException("down") };

        await Assert.ThrowsAsync<BookProviderException>(() => Service(failing).SearchAsync(id, null, null, CancellationToken.None));

        var working = new FakeProvider { Search = (_, _, _) => new[] { Rec("a", "Dune", "Frank Herbert") } };
        var both = new BookMetadataMatchService(() => new PaperbunkrDbContext(_options), () => new IBookMetadataProvider[] { failing, working });
        Assert.Single(await both.SearchAsync(id, null, null, CancellationToken.None));
    }

    [Fact]
    public void SkipForever_Persists_AndCanBeUndone()
    {
        int id = AddBook("Dune");
        var service = Service(new FakeProvider());

        service.SetSkipForever(id, true);
        Assert.True(Get(id).SkipMetadataMatch);

        service.SetSkipForever(id, false);
        Assert.False(Get(id).SkipMetadataMatch);
    }
}
