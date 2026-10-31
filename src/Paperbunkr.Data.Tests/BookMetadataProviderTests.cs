using System.Net;
using System.Text;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.Books;

namespace Paperbunkr.Data.Tests;

/// <summary>Open Library / Google Books parsing and ranking against recorded-shape JSON; no live network (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §5).</summary>
public class BookMetadataProviderTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public List<string> Urls { get; } = new();

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string OpenLibrarySearch = """
        {"numFound":2,"docs":[
          {"key":"/works/OL893415W","title":"Dune","author_name":["Frank Herbert"],"first_publish_year":1965,
           "publisher":["Chilton Books","Ace"],"isbn":["0441172717","9780441172719"],"language":["eng","fre"],
           "subject":["Science fiction","Deserts","Fiction","a","b","c","d","e","f","g"],"ratings_average":4.31,"ratings_count":1250,"cover_i":8231856},
          {"key":"/works/OL1W","title":"Dune Messiah","author_name":["Frank Herbert"]}
        ]}
        """;

    [Fact]
    public async Task OpenLibrary_Search_ByTitleAndAuthor_ParsesCandidates()
    {
        var handler = new FakeHandler(_ => Json(OpenLibrarySearch));
        var provider = new OpenLibraryBookProvider(new HttpClient(handler));

        var results = await provider.SearchAsync(null, "Dune", "Frank Herbert", CancellationToken.None);

        Assert.Contains("title=Dune", handler.Urls.Single());
        Assert.Contains("author=Frank%20Herbert", handler.Urls.Single());
        Assert.Equal(2, results.Count);
        var dune = results[0];
        Assert.Equal(BookFieldSourceKind.OpenLibrary, dune.Source);
        Assert.Equal("/works/OL893415W", dune.Id);
        Assert.Equal("Dune", dune.Title);
        Assert.Equal("Frank Herbert", dune.AuthorLine);
        Assert.Equal("Chilton Books", dune.Publisher);
        Assert.Equal(1965, dune.Year);
        Assert.Equal("en", dune.LanguageISO);
        Assert.Equal("9780441172719", dune.Isbn);
        Assert.Equal(8, dune.Subjects.Count);
        Assert.Equal(4.31f, dune.CommunityRating);
        Assert.Equal(1250, dune.CommunityRatingCount);
        Assert.Equal("https://covers.openlibrary.org/b/id/8231856-L.jpg", dune.CoverUrl);
        Assert.Null(results[1].Publisher);
    }

    [Fact]
    public async Task OpenLibrary_Search_ByIsbn_UsesTheIsbnParameterOnly()
    {
        var handler = new FakeHandler(_ => Json(OpenLibrarySearch));
        var provider = new OpenLibraryBookProvider(new HttpClient(handler));

        await provider.SearchAsync("978-0-441-17271-9", "ignored", "ignored", CancellationToken.None);

        string url = handler.Urls.Single();
        Assert.Contains("isbn=9780441172719", url);
        Assert.DoesNotContain("title=", url);
    }

    [Fact]
    public async Task OpenLibrary_Get_ReadsDescriptionAsStringOrObject()
    {
        string body = """{"title":"Dune","description":{"type":"/type/text","value":" A desert planet. "},"subjects":["Deserts"]}""";
        var provider = new OpenLibraryBookProvider(new HttpClient(new FakeHandler(_ => Json(body))));

        var record = await provider.GetAsync("/works/OL893415W", CancellationToken.None);

        Assert.Equal("A desert planet.", record!.Description);
        Assert.Equal(new[] { "Deserts" }, record.Subjects);

        var plain = new OpenLibraryBookProvider(new HttpClient(new FakeHandler(_ => Json("""{"title":"Dune","description":"Plain."}"""))));
        Assert.Equal("Plain.", (await plain.GetAsync("/works/OL1W", CancellationToken.None))!.Description);
    }

    [Fact]
    public async Task OpenLibrary_NotFound_ReturnsNull_AndBadKeysAreRejectedWithoutARequest()
    {
        var handler = new FakeHandler(_ => Json("{}", HttpStatusCode.NotFound));
        var provider = new OpenLibraryBookProvider(new HttpClient(handler));

        Assert.Null(await provider.GetAsync("/works/OL9W", CancellationToken.None));
        Assert.Null(await provider.GetAsync("../etc/passwd", CancellationToken.None));
        Assert.Single(handler.Urls);
    }

    private const string GoogleSearch = """
        {"items":[
          {"id":"B1","volumeInfo":{"title":"Dune","subtitle":"Book One","authors":["Frank Herbert"],"publisher":"Ace","publishedDate":"2005-08-02",
            "description":"<p>A <b>desert</b> planet.</p>","industryIdentifiers":[{"type":"ISBN_10","identifier":"0441172717"},{"type":"ISBN_13","identifier":"9780441172719"}],
            "categories":["Fiction / Science Fiction / General","Fiction / Classics"],"averageRating":4.5,"ratingsCount":88,"language":"en",
            "imageLinks":{"thumbnail":"http://books.google.com/x.jpg"}}},
          {"id":"B2","volumeInfo":{"authors":["No Title"]}}
        ]}
        """;

    [Fact]
    public async Task GoogleBooks_Search_ParsesVolumes_AndSkipsOnesWithoutATitle()
    {
        var handler = new FakeHandler(_ => Json(GoogleSearch));
        var provider = new GoogleBooksProvider(() => "KEY 1", new HttpClient(handler));

        var results = await provider.SearchAsync(null, "Dune", "Frank Herbert", CancellationToken.None);

        string url = handler.Urls.Single();
        Assert.Contains("intitle", url);
        Assert.Contains("inauthor", url);
        Assert.Contains("&key=KEY%201", url);
        var dune = Assert.Single(results);
        Assert.Equal("B1", dune.Id);
        Assert.Equal("Book One", dune.Subtitle);
        Assert.Equal("Ace", dune.Publisher);
        Assert.Equal(2005, dune.Year);
        Assert.Equal("9780441172719", dune.Isbn);
        Assert.Equal("A desert planet.", dune.Description);
        Assert.Equal(new[] { "Fiction", "Science Fiction", "Classics" }, dune.Subjects);
        Assert.Equal(4.5f, dune.CommunityRating);
        Assert.Equal(88, dune.CommunityRatingCount);
        Assert.Equal("https://books.google.com/x.jpg", dune.CoverUrl);
    }

    [Fact]
    public async Task GoogleBooks_NoKey_SendsNoKeyParameter_AndIsbnQueryIsUsed()
    {
        var handler = new FakeHandler(_ => Json("""{"totalItems":0}"""));
        var provider = new GoogleBooksProvider(null, new HttpClient(handler));

        var results = await provider.SearchAsync("0441172717", "x", null, CancellationToken.None);

        Assert.Empty(results);
        Assert.DoesNotContain("key=", handler.Urls.Single());
        Assert.Contains("isbn%3A0441172717", handler.Urls.Single());
    }

    [Fact]
    public async Task RateLimit_And_Errors_ThrowADistinctException()
    {
        var limited = new GoogleBooksProvider(null, new HttpClient(new FakeHandler(_ => Json("{}", HttpStatusCode.TooManyRequests))));
        var ex = await Assert.ThrowsAsync<BookProviderException>(() => limited.SearchAsync(null, "Dune", null, CancellationToken.None));
        Assert.True(ex.IsRateLimited);

        var broken = new OpenLibraryBookProvider(new HttpClient(new FakeHandler(_ => Json("{}", HttpStatusCode.InternalServerError))));
        var ex2 = await Assert.ThrowsAsync<BookProviderException>(() => broken.SearchAsync(null, "Dune", null, CancellationToken.None));
        Assert.False(ex2.IsRateLimited);

        var garbage = new OpenLibraryBookProvider(new HttpClient(new FakeHandler(_ => Json("not json"))));
        await Assert.ThrowsAsync<BookProviderException>(() => garbage.SearchAsync(null, "Dune", null, CancellationToken.None));
    }

    [Fact]
    public async Task BlankQuery_ReturnsNothing_WithoutARequest()
    {
        var handler = new FakeHandler(_ => Json("{}"));
        var provider = new OpenLibraryBookProvider(new HttpClient(handler));

        Assert.Empty(await provider.SearchAsync(null, "  ", null, CancellationToken.None));
        Assert.Empty(handler.Urls);
    }

    private static BookProviderRecord Rec(string title, string author, string? isbn = null, string? lang = null) =>
        new(BookFieldSourceKind.OpenLibrary, "id-" + title, title, null, new[] { author }, null, null, lang, isbn,
            Array.Empty<string>(), null, null, null, null, null, null);

    [Fact]
    public void Ranker_IsbnMatchWins_ThenTitleAndAuthor()
    {
        var ranked = BookMatchRanker.Rank(
            new[] { Rec("Dune Messiah", "Frank Herbert"), Rec("Dune", "Frank Herbert", "9780441172719"), Rec("Dune", "Someone Else") },
            "Dune", "Herbert, Frank", "978-0-441-17271-9", null);

        Assert.Equal("id-Dune", ranked[0].Record.Id);
        Assert.Equal(1.0, ranked[0].Score);
        Assert.True(ranked[1].Score >= ranked[2].Score);
    }

    [Fact]
    public void Ranker_ClearWinner_NeedsConfidenceAndAMargin()
    {
        var clear = BookMatchRanker.Rank(new[] { Rec("The Hobbit", "J. R. R. Tolkien"), Rec("The Lord of the Rings", "Tolkien") }, "The Hobbit", "Tolkien", null, null);
        Assert.NotNull(BookMatchRanker.ClearWinner(clear));

        var tie = BookMatchRanker.Rank(new[] { Rec("The Hobbit", "Tolkien"), Rec("The Hobbit", "Tolkien") }, "The Hobbit", "Tolkien", null, null);
        Assert.Null(BookMatchRanker.ClearWinner(tie));

        var weak = BookMatchRanker.Rank(new[] { Rec("Something Else", "Nobody") }, "The Hobbit", "Tolkien", null, null);
        Assert.Null(BookMatchRanker.ClearWinner(weak));
    }

    [Fact]
    public void Ranker_PenalisesAWrongLanguage_AndTreatsUnknownAuthorAsNeutral()
    {
        double english = BookMatchRanker.Score(Rec("Dune", "Frank Herbert", lang: "en"), "Dune", "Frank Herbert", null, "en");
        double french = BookMatchRanker.Score(Rec("Dune", "Frank Herbert", lang: "fr"), "Dune", "Frank Herbert", null, "en");
        Assert.True(english > french);

        double noAuthor = BookMatchRanker.Score(Rec("Dune", "Frank Herbert"), "Dune", null, null, null);
        Assert.InRange(noAuthor, 0.8, 0.9);
    }
}
