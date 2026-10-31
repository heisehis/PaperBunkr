using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Opds;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>OPDS 1.2 client pieces (docs/superpowers/specs/2026-10-27-books-slice4c-opds-design.md), on hand-written feeds shaped like Calibre's content server.</summary>
public class OpdsTests : IDisposable
{
    private const string AcquisitionFeed = """
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:dc="http://purl.org/dc/terms/" xmlns:calibre="http://calibre.kovidgoyal.net/2009/metadata">
          <title>Sci-Fi</title>
          <link rel="start" href="/opds" type="application/atom+xml;profile=opds-catalog;kind=navigation"/>
          <link rel="up" href="/opds/navcatalog" type="application/atom+xml;profile=opds-catalog;kind=navigation"/>
          <link rel="next" href="/opds/page2" type="application/atom+xml"/>
          <link rel="search" href="/opds/search/{searchTerms}" type="application/atom+xml"/>
          <entry>
            <title>Dune</title><id>urn:uuid:book-1</id>
            <author><name>Frank Herbert</name></author>
            <summary>Spice &amp; worms.</summary>
            <calibre:series>Dune Saga</calibre:series><calibre:series_index>1.0</calibre:series_index>
            <category term="Sci-Fi" label="Science Fiction"/>
            <dc:language>eng</dc:language>
            <link rel="http://opds-spec.org/acquisition" type="application/pdf" href="/get/pdf/1"/>
            <link rel="http://opds-spec.org/acquisition" type="application/epub+zip" href="/get/epub/1"/>
            <link rel="http://opds-spec.org/image/thumbnail" type="image/jpeg" href="/get/thumb/1"/>
          </entry>
          <entry>
            <title>By Author</title><id>urn:uuid:nav-1</id>
            <link type="application/atom+xml;profile=opds-catalog" href="/opds/authors"/>
          </entry>
        </feed>
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_opds_{Guid.NewGuid():N}");
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_opds_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private static readonly Uri Feed = new("https://books.example/opds");

    public OpdsTests()
    {
        Directory.CreateDirectory(_dir);
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext Db() => new(_options);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Xml(string xml) => new(HttpStatusCode.OK) { Content = new StringContent(xml, Encoding.UTF8, "application/atom+xml") };

    [Fact]
    public void Parser_ReadsAnAcquisitionEntryAndResolvesRelativeLinks()
    {
        var feed = OpdsFeedParser.Parse(AcquisitionFeed, Feed);

        Assert.Equal("Sci-Fi", feed.Title);
        var book = feed.Entries[0];
        Assert.Equal("Dune", book.Title);
        Assert.Equal("Frank Herbert", book.Author);
        Assert.Equal("Spice & worms.", book.Summary);
        Assert.Equal("Dune Saga", book.Series);
        Assert.Equal(1f, book.SeriesIndex);
        Assert.Equal(["Science Fiction"], book.Categories);
        Assert.Equal("eng", book.Language);
        Assert.True(book.IsAcquisition);
        Assert.Equal("https://books.example/get/epub/1", book.Acquisitions.Single(a => a.Type!.Contains("epub")).Href);
        Assert.Equal("https://books.example/get/thumb/1", book.Thumbnail!.Href);
        Assert.Equal("https://books.example/opds/page2", feed.Next!.AbsoluteUri);
        Assert.Equal("https://books.example/opds", feed.Start!.AbsoluteUri);
    }

    [Fact]
    public void Parser_RecognisesNavigationEntriesAndSearchTemplates()
    {
        var feed = OpdsFeedParser.Parse(AcquisitionFeed, Feed);

        var nav = feed.Entries[1];
        Assert.False(nav.IsAcquisition);
        Assert.Equal("https://books.example/opds/authors", nav.NavigationLink!.Href);
        Assert.True(feed.SearchIsTemplate);
        Assert.Contains("{searchTerms}", Uri.UnescapeDataString(feed.Search!.OriginalString));
    }

    [Fact]
    public void Parser_RejectsNonAtomDocuments()
    {
        Assert.Throws<FormatException>(() => OpdsFeedParser.Parse("<html><body/></html>", Feed));
    }

    [Fact]
    public void Parser_ReadsXhtmlSummaries()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom"><title>t</title>
              <entry><title>B</title><id>x</id><content type="xhtml"><div xmlns="http://www.w3.org/1999/xhtml"><p>One</p><p>Two</p></div></content></entry>
            </feed>
            """;

        Assert.Equal("One Two", OpdsFeedParser.Parse(xml, Feed).Entries[0].Summary);
    }

    [Fact]
    public void ChooseAcquisition_PrefersEpubThenFb2ThenMobiThenPdfAndIgnoresOthers()
    {
        static OpdsLink L(string type, string href = "/x") => new("http://opds-spec.org/acquisition", "https://h" + href, type, null);

        Assert.Equal("application/epub+zip", OpdsClient.ChooseAcquisition([L("application/pdf"), L("application/epub+zip")])!.Type);
        Assert.Equal("application/x-mobipocket-ebook", OpdsClient.ChooseAcquisition([L("application/pdf"), L("application/x-mobipocket-ebook")])!.Type);
        Assert.Null(OpdsClient.ChooseAcquisition([L("application/zip", "/a.cbz"), L("text/html")]));
        Assert.Equal(".epub", OpdsClient.ExtensionFor(L("application/octet-stream", "/dl/book.epub")));
        Assert.Equal(".fb2.zip", OpdsClient.ExtensionFor(L("application/fb2+zip")));
    }

    [Fact]
    public async Task Client_SendsBasicAuthToTheCatalogHostOnly()
    {
        var handler = new FakeHandler(r => r.RequestUri!.Host == "books.example"
            ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://cdn.elsewhere.example/opds") } }
            : Xml(AcquisitionFeed));
        var client = new OpdsClient(() => new HttpClient(handler));

        await client.GetFeedAsync(Feed, new OpdsCredentials("books.example", "ann", "secret"));

        Assert.Equal("Basic", handler.Requests[0].Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("ann:secret")), handler.Requests[0].Headers.Authorization!.Parameter);
        Assert.Equal("cdn.elsewhere.example", handler.Requests[1].RequestUri!.Host);
        Assert.Null(handler.Requests[1].Headers.Authorization);
    }

    [Fact]
    public async Task Client_ReportsRejectedCredentialsAndErrors()
    {
        var denied = new OpdsClient(() => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        var broken = new OpdsClient(() => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.GetFeedAsync(Feed, null));
        await Assert.ThrowsAsync<HttpRequestException>(() => broken.GetFeedAsync(Feed, null));
    }

    [Fact]
    public async Task Client_StopsAfterTooManyRedirects()
    {
        var loop = new OpdsClient(() => new HttpClient(new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://books.example/again") } })));

        await Assert.ThrowsAsync<HttpRequestException>(() => loop.GetFeedAsync(Feed, null));
    }

    [Fact]
    public async Task Client_FillsASearchTemplateAndFetchesAnOpenSearchDescription()
    {
        var feed = OpdsFeedParser.Parse(AcquisitionFeed, Feed);
        var client = new OpdsClient(() => new HttpClient(new FakeHandler(_ => Xml(string.Empty))));

        var url = await client.ResolveSearchAsync(feed, "dune messiah", null);

        Assert.Equal("https://books.example/opds/search/dune%20messiah", url!.AbsoluteUri);

        const string description = """<OpenSearchDescription xmlns="http://a9.com/-/spec/opensearch/1.1/"><Url type="application/atom+xml" template="/search?query={searchTerms}&amp;page={startPage?}"/></OpenSearchDescription>""";
        var describedFeed = feed with { Search = new Uri("https://books.example/opensearch.xml"), SearchIsTemplate = false };
        var described = new OpdsClient(() => new HttpClient(new FakeHandler(_ => Xml(description))));

        var described2 = await described.ResolveSearchAsync(describedFeed, "x y", null);

        Assert.Equal("https://books.example/search?query=x%20y&page=", described2!.AbsoluteUri);
    }

    [Fact]
    public async Task Download_WritesTheFileAndLeavesNoPartFile()
    {
        var bytes = Encoding.UTF8.GetBytes("epub bytes");
        var client = new OpdsClient(() => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
        var link = new OpdsLink("http://opds-spec.org/acquisition", "https://books.example/get/epub/1", "application/epub+zip", null);

        string path = await client.DownloadAsync(link, _dir, "Dune: Part/One?", null);

        Assert.Equal(".epub", Path.GetExtension(path));
        Assert.DoesNotContain(':', Path.GetFileName(path));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));

        string second = await client.DownloadAsync(link, _dir, "Dune: Part/One?", null);
        Assert.NotEqual(path, second);
    }

    [Fact]
    public async Task Download_CancelledOrFailedLeavesNothingBehind()
    {
        var failing = new OpdsClient(() => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))));
        var link = new OpdsLink("http://opds-spec.org/acquisition", "https://books.example/get/epub/1", "application/epub+zip", null);

        await Assert.ThrowsAsync<HttpRequestException>(() => failing.DownloadAsync(link, _dir, "Gone", null));

        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Download_RefusesAFormatItCannotRead()
    {
        var client = new OpdsClient(() => new HttpClient(new FakeHandler(_ => Xml(string.Empty))));
        var link = new OpdsLink("http://opds-spec.org/acquisition", "https://books.example/get/cbz/1", "application/zip", null);

        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(link, _dir, "Comic", null));
    }

    [Theory]
    [InlineData("books.example/opds", "https://books.example/opds")]
    [InlineData("http://192.168.1.5:8080/opds", "http://192.168.1.5:8080/opds")]
    [InlineData("  https://x.test/o  ", "https://x.test/o")]
    public void Store_NormalizesAddresses(string raw, string expected) =>
        Assert.Equal(expected, OpdsCatalogStore.NormalizeUrl(raw));

    [Theory]
    [InlineData("")]
    [InlineData("ftp://x.test/o")]
    [InlineData("not a url")]
    public void Store_RejectsBadAddresses(string raw) => Assert.Null(OpdsCatalogStore.NormalizeUrl(raw));

    [Fact]
    public void Store_SavesProtectsThePasswordAndKeepsItOnAnEmptyUpdate()
    {
        var store = new OpdsCatalogStore(() => Db());

        var saved = store.Save(null, "Home", "http://nas.local:8080/opds", "ann", "pw1");
        Assert.NotEqual("pw1", saved.ProtectedPassword);
        Assert.Equal("pw1", OpdsCatalogStore.PasswordOf(store.List().Single()));

        store.Save(saved.Id, "Home NAS", "http://nas.local:8080/opds", "ann", "");
        Assert.Equal("pw1", OpdsCatalogStore.PasswordOf(store.List().Single()));
        Assert.Equal("Home NAS", store.List().Single().Name);

        store.Save(saved.Id, "Home NAS", "http://nas.local:8080/opds", "", "");
        Assert.Null(store.List().Single().ProtectedPassword);
        Assert.True(OpdsCatalogStore.IsPlainHttp(saved.Url));
    }

    [Fact]
    public void Store_RejectsMissingNamesAndRemovesWithItsDownloads()
    {
        var store = new OpdsCatalogStore(() => Db());
        Assert.Throws<ArgumentException>(() => store.Save(null, " ", "https://x.test/o", null, null));
        Assert.Throws<ArgumentException>(() => store.Save(null, "N", "ftp://x.test/o", null, null));

        var catalog = store.Save(null, "N", "https://x.test/o", null, null);
        using (var db = Db())
        {
            db.OpdsDownloads.Add(new OpdsDownload { CatalogId = catalog.Id, EntryId = "e", Title = "T", FilePath = "p", DownloadedUtc = DateTime.UtcNow });
            db.SaveChanges();
        }

        store.Remove(catalog.Id);

        Assert.Empty(store.List());
        Assert.Empty(Db().OpdsDownloads);
    }

    [Fact]
    public async Task DownloadService_ImportsTheFileAppliesCatalogMetadataAndRecordsTheDownload()
    {
        var bytes = Encoding.UTF8.GetBytes("x");
        var client = new OpdsClient(() => new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
        var entry = OpdsFeedParser.Parse(AcquisitionFeed, Feed).Entries[0];
        var catalog = new OpdsCatalogStore(() => Db()).Save(null, "Home", "https://books.example/opds", null, null);
        var service = new OpdsDownloadService(() => Db(), client, async path =>
        {
            using var db = Db();
            var book = new Book { Title = "Dune", FilePath = path, Format = BookFormat.Epub, AddedTime = DateTime.UtcNow };
            db.Books.Add(book);
            await db.SaveChangesAsync();
            return book.Id;
        });

        var (path, bookId) = await service.DownloadAsync(catalog, entry, _dir);

        Assert.EndsWith(".epub", path);
        using var check = Db();
        var book2 = check.Books.Include(b => b.BookSeries).Include(b => b.Tags).Include(b => b.FieldSources).Single();
        Assert.Equal(bookId, book2.Id);
        Assert.Equal("Spice & worms.", book2.Summary);
        Assert.Equal("Dune Saga", book2.BookSeries!.Name);
        Assert.Equal("en", book2.LanguageISO);
        Assert.Contains(book2.Tags, t => t.Value == "Science Fiction");
        Assert.All(book2.FieldSources.Where(s => s.Field == "Summary"), s => Assert.Equal(BookFieldSourceKind.Opds, s.Source));
        var download = check.OpdsDownloads.Single();
        Assert.Equal("urn:uuid:book-1", download.EntryId);
        Assert.Equal(bookId, download.BookId);
    }

    [Fact]
    public void InLibrary_MatchesPreviousDownloadsAndSameTitleAndAuthor()
    {
        var feed = OpdsFeedParser.Parse(AcquisitionFeed, Feed);
        var service = new OpdsDownloadService(() => Db());
        Assert.Empty(service.InLibrary(1, feed.Entries));

        using (var db = Db())
        {
            db.Books.Add(new Book { Title = "dune", Author = "FRANK HERBERT", FilePath = "a.epub", Format = BookFormat.Epub, AddedTime = DateTime.UtcNow });
            db.SaveChanges();
        }

        Assert.Equal(["urn:uuid:book-1"], service.InLibrary(1, feed.Entries));
    }
}
