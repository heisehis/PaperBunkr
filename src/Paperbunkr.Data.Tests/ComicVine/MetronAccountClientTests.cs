using System.Net;
using System.Net.Http;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>
/// <see cref="MetronAccountClient"/> and <see cref="MetronReadingListSource"/> against a fake handler - no live
/// network. The request and reply shapes are the ones Metron's <c>api/README.md</c> documents (docs/superpowers/
/// specs/2026-10-05-metron-account-sync-design.md); none of this has been run against a real account.
/// </summary>
public class MetronAccountClientTests
{
    private sealed class Handler(Func<HttpRequestMessage, string?, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!.OriginalString, body));
            var (status, reply) = respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(reply) };
        }
    }

    private static (MetronAccountClient Account, MetronClient Client, Handler Handler) Make(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> respond)
    {
        var handler = new Handler(respond);
        var client = new MetronClient("reader", "pw", ComicVineRequestPriority.High, new HttpClient(handler));
        return (new MetronAccountClient(client), client, handler);
    }

    private static string Page(string results, string? next = null) =>
        $$"""{"count":1,"next":{{(next is null ? "null" : "\"" + next + "\"")}},"previous":null,"results":{{results}}}""";

    [Fact]
    public async Task PullList_IsRead_AndSeriesAreAddedAndRemovedAtTheDocumentedPaths()
    {
        var (account, _, handler) = Make((request, _) => request.Method == HttpMethod.Get
            ? (HttpStatusCode.OK, Page("""[{"id":7,"series":{"id":123,"series":"Amazing Spider-Man (1963)","year_began":1963,"year_end":null,"volume":1,"modified":"2025-01-15T10:30:00Z"},"added_on":"2026-04-15T09:30:00Z"}]"""))
            : request.Method == HttpMethod.Delete ? (HttpStatusCode.NoContent, "") : (HttpStatusCode.Created, "{}"));

        var list = await account.GetPullListAsync(CancellationToken.None);
        await account.AddToPullListAsync(123, CancellationToken.None);
        await account.RemoveFromPullListAsync(123, CancellationToken.None);

        Assert.Equal(new MetronPullListSeries(123, "Amazing Spider-Man", 1963), Assert.Single(list));
        Assert.EndsWith("/pull_list/series/", handler.Requests[0].Url);
        Assert.Equal((HttpMethod.Post, "https://metron.cloud/api/pull_list/series/add/", """{"series_id":123}"""), handler.Requests[1]);
        Assert.Equal((HttpMethod.Delete, "https://metron.cloud/api/pull_list/series/123/remove/", null), handler.Requests[2]);
    }

    [Fact]
    public async Task RemovingWhatIsAlreadyGone_IsNotAnError()
    {
        var (account, _, _) = Make((_, _) => (HttpStatusCode.NotFound, """{"detail":"Not found."}"""));

        await account.RemoveFromPullListAsync(123, CancellationToken.None);
        await account.RemoveFromWishListAsync(42, CancellationToken.None);
    }

    [Fact]
    public async Task Scrobble_SendsTheReadDateAndRating_AndIsNeverRetried()
    {
        int calls = 0;
        var (account, _, handler) = Make((_, _) =>
        {
            calls++;
            return (HttpStatusCode.Created, """{"id":789,"issue":{"id":12345,"series_name":"Amazing Spider-Man","number":"300"},"is_read":true,"date_read":"2026-01-08T14:30:00Z","read_dates":["2026-01-08T14:30:00Z"],"read_count":1,"rating":4,"created":true}""");
        });

        var item = await account.ScrobbleAsync(12345, new DateTime(2026, 1, 8, 14, 30, 0, DateTimeKind.Utc), 4, CancellationToken.None);

        Assert.Equal(new MetronCollectionItem(789, 12345, true, new DateTime(2026, 1, 8, 14, 30, 0, DateTimeKind.Utc), 4), item);
        Assert.Equal((HttpMethod.Post, "https://metron.cloud/api/collection/scrobble/", """{"issue_id":12345,"date_read":"2026-01-08T14:30:00Z","rating":4}"""), handler.Requests[0]);

        // A dropped connection may have reached Metron: sending again would be a second read date.
        var (flaky, _, flakyHandler) = Make((_, _) => throw new HttpRequestException("connection reset"));
        await Assert.ThrowsAsync<ComicVineException>(() => flaky.ScrobbleAsync(1, DateTime.UtcNow, null, CancellationToken.None));
        Assert.Single(flakyHandler.Requests);
    }

    [Fact]
    public async Task Collection_AddIsDigital_RatingIsAPatch_AndPagesAreFollowed()
    {
        var (account, _, handler) = Make((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                bool second = request.RequestUri!.Query.Contains("page=2");
                return (HttpStatusCode.OK, Page(
                    """[{"id":1,"issue":{"id":5432,"number":"1"},"is_read":true,"read_dates":[{"id":1,"read_date":"2024-03-20T19:30:00Z"},{"id":2,"read_date":"2024-01-15T14:00:00Z"}],"read_count":2,"rating":5}]""",
                    second ? null : "https://metron.cloud/api/collection/?page=2"));
            }

            return request.Method == HttpMethod.Patch
                ? (HttpStatusCode.OK, """{"id":789,"rating":null}""")
                : (HttpStatusCode.Created, """{"id":789,"issue":{"id":12345},"is_read":false,"read_dates":[],"rating":null}""");
        });

        var added = await account.AddToCollectionAsync(12345, CancellationToken.None);
        await account.SetCollectionRatingAsync(789, null, CancellationToken.None);
        var first = await account.GetCollectionPageAsync(null, CancellationToken.None);
        var second = await account.GetCollectionPageAsync(first.Next, CancellationToken.None);

        Assert.Equal(new MetronCollectionItem(789, 12345, false, null, null), added);
        Assert.Equal((HttpMethod.Post, "https://metron.cloud/api/collection/add/", """{"issue_id":12345,"book_format":"DIGITAL"}"""), handler.Requests[0]);
        Assert.Equal((HttpMethod.Patch, "https://metron.cloud/api/collection/789/", """{"rating":null}"""), handler.Requests[1]);
        Assert.Equal(new MetronCollectionItem(1, 5432, true, new DateTime(2024, 3, 20, 19, 30, 0, DateTimeKind.Utc), 5), Assert.Single(first.Items));
        Assert.Equal("https://metron.cloud/api/collection/?page=2", first.Next);
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task WishList_AddAcquireRemove()
    {
        var (account, _, handler) = Make((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/acquire/")
            ? (HttpStatusCode.OK, """{"wish_list_item_id":42,"collection_item_id":987,"created":true,"status":"Acquired"}""")
            : request.Method == HttpMethod.Delete ? (HttpStatusCode.NoContent, "")
            : (HttpStatusCode.Created, """{"id":42,"issue":{"id":5001,"number":"15"},"status":"Wanted","priority":3}"""));

        var item = await account.AddToWishListAsync(5001, CancellationToken.None);
        int? collectionItem = await account.AcquireWishListItemAsync(42, CancellationToken.None);
        await account.RemoveFromWishListAsync(42, CancellationToken.None);

        Assert.Equal(new MetronWishListItem(42, 5001, "Wanted"), item);
        Assert.Equal(987, collectionItem);
        Assert.Equal((HttpMethod.Post, "https://metron.cloud/api/wish_list/items/add/", """{"issue_id":5001}"""), handler.Requests[0]);
        Assert.Equal("https://metron.cloud/api/wish_list/items/42/acquire/", handler.Requests[1].Url);
        Assert.Equal((HttpMethod.Delete, "https://metron.cloud/api/wish_list/items/42/remove/", null), handler.Requests[2]);
    }

    /// <summary>The bug of the first real run: a path without its trailing slash is redirected, and a followed redirect loses the login.</summary>
    [Fact]
    public async Task EveryRequest_GoesToAPathEndingInASlash_AndARedirectIsNeverMistakenForABadLogin()
    {
        var (account, _, handler) = Make((request, _) => request.Method == HttpMethod.Get
            ? (HttpStatusCode.OK, Page("[]"))
            : request.Method == HttpMethod.Delete ? (HttpStatusCode.NoContent, "")
            : (HttpStatusCode.Created, """{"id":1,"issue":{"id":2},"collection_item_id":3,"status":"Wanted"}"""));

        await account.GetPullListAsync(CancellationToken.None);
        await account.AddToPullListAsync(1, CancellationToken.None);
        await account.RemoveFromPullListAsync(1, CancellationToken.None);
        await account.ScrobbleAsync(2, DateTime.UtcNow, null, CancellationToken.None);
        await account.AddToCollectionAsync(2, CancellationToken.None);
        await account.SetCollectionRatingAsync(1, 3, CancellationToken.None);
        await account.GetCollectionPageAsync(null, CancellationToken.None);
        await account.AddToWishListAsync(2, CancellationToken.None);
        await account.AcquireWishListItemAsync(1, CancellationToken.None);
        await account.RemoveFromWishListAsync(1, CancellationToken.None);

        Assert.Equal(10, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.EndsWith("/", new Uri(r.Url).AbsolutePath));

        var (redirected, _, _) = Make((_, _) => (HttpStatusCode.MovedPermanently, ""));
        var ex = await Assert.ThrowsAsync<ComicVineException>(() => redirected.AddToPullListAsync(1, CancellationToken.None));
        Assert.Equal(301, ex.HttpStatus);
        Assert.Null(ex.ApiStatusCode);                                    // not 100: this is not the user's login
        Assert.Contains("redirect", ex.Message);
    }

    [Fact]
    public async Task ABadRequest_CarriesItsHttpStatus_SoSyncCanSkipTheItem()
    {
        var (account, _, _) = Make((_, _) => (HttpStatusCode.BadRequest, """{"issue_id":["Invalid pk"]}"""));

        var ex = await Assert.ThrowsAsync<ComicVineException>(() => account.AddToCollectionAsync(1, CancellationToken.None));

        Assert.Equal(400, ex.HttpStatus);
    }

    [Fact]
    public async Task ReadingLists_AreSearched_AndTheirItemsComeBackInOrderWithTheirRoles()
    {
        var (_, client, handler) = Make((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/items/")
            ? (HttpStatusCode.OK, Page("""[{"id":102,"issue":{"id":2,"series":{"id":789,"name":"Secret Wars","volume":1},"number":"2","cover_date":"2015-08-01"},"order":2,"issue_type":""},{"id":101,"issue":{"id":1,"series":{"id":789,"name":"Secret Wars","volume":1},"number":"1","cover_date":"2015-07-01"},"order":1,"issue_type":"Core Issue"}]"""))
            : request.RequestUri.AbsolutePath.EndsWith("/reading_list/")
                ? (HttpStatusCode.OK, Page("""[{"id":1,"name":"Secret Wars (2015)","slug":"secret-wars-2015","user":{"id":5,"username":"johndoe"},"list_type":"Event","is_private":false,"attribution_source":"CBRO","average_rating":4.5,"rating_count":12}]"""))
                : (HttpStatusCode.OK, """{"id":1,"name":"Secret Wars (2015)","desc":"The end of everything.","image":"https://static.metron.cloud/x.jpg"}"""));
        var source = new MetronReadingListSource(client);

        var found = Assert.Single(await source.SearchAsync("secret wars", CancellationToken.None));
        var issues = await source.GetArcIssuesInOrderAsync(found.Id, CancellationToken.None);
        var overview = await source.GetArcOverviewAsync(found.Id, CancellationToken.None);

        Assert.Equal(new ArcSearchResult("1", "Secret Wars (2015)", "Event · by johndoe · from CBRO", null, 0), found);
        Assert.Contains("/reading_list/?name=secret%20wars", handler.Requests[0].Url);
        Assert.Equal(new[] { "1", "2" }, issues.Select(i => i.Number));
        Assert.Equal("Core Issue", issues[0].Annotation);
        Assert.Null(issues[1].Annotation);
        Assert.Equal(2015, issues[0].Year);
        Assert.Equal("The end of everything.", overview!.Description);
    }

    [Fact]
    public async Task AReadingListFailure_SurfacesAsTheSourcesOwnException()
    {
        var (_, client, _) = Make((_, _) => (HttpStatusCode.Unauthorized, ""));

        var ex = await Assert.ThrowsAsync<ReadingListSourceException>(() => new MetronReadingListSource(client).SearchAsync("x", CancellationToken.None));

        Assert.Equal("Metron reading lists", ex.SourceDisplayName);
    }
}
