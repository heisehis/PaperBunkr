using System.Net;
using System.Net.Http;
using Paperbunkr.Data.ComicVine;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>Fixture-based (no live network): parsing, pagination, error mapping and priority for <see cref="ComicVineClient"/>.</summary>
public class ComicVineClientTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var (status, body) = respond(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (ComicVineClient Client, FakeHandler Handler) Make(Func<HttpRequestMessage, (HttpStatusCode, string)> respond,
        ComicVineRequestPriority priority = ComicVineRequestPriority.High)
    {
        var handler = new FakeHandler(respond);
        return (new ComicVineClient("KEY", priority, new HttpClient(handler)), handler);
    }

    private static string Ok(string results, int total, int offset = 0) =>
        $$"""{"status_code":1,"error":"OK","number_of_total_results":{{total}},"offset":{{offset}},"results":{{results}}}""";

    [Fact]
    public async Task SearchVolumes_ParsesFields_IncludingStringYearsAndPublisher()
    {
        var (client, handler) = Make(_ => (HttpStatusCode.OK, Ok("""
            [{"id":91273,"name":"Spawn","publisher":{"name":"Image"},"start_year":"1992","count_of_issues":355,"image":{"medium_url":"http://x/m.jpg"}},
             {"id":5,"name":"Spawn Junk","publisher":null,"start_year":"1990?","count_of_issues":0,"image":null}]
            """, 2)));

        var volumes = await client.SearchVolumesAsync("Spawn & co", CancellationToken.None);

        Assert.Equal(2, volumes.Count);
        Assert.Equal(new ComicVineVolume(91273, "Spawn", "Image", 1992, 355, "http://x/m.jpg"), volumes[0]);
        Assert.Equal(1990, volumes[1].StartYear);
        Assert.Null(volumes[1].Publisher);
        var url = handler.Requests[0].RequestUri!.OriginalString;
        Assert.Contains("/volumes/?api_key=KEY", url);
        Assert.Contains("filter=name:Spawn%20%26%20co", url);   // query is escaped, not concatenated raw
    }

    [Fact]
    public async Task GetVolumeIssues_FollowsPagination_UntilTotalIsReached()
    {
        static string Page(int from, int count) =>
            "[" + string.Join(",", Enumerable.Range(from, count).Select(i =>
                "{\"id\":" + i + ",\"issue_number\":\"" + i + "\",\"name\":null,\"store_date\":\"2026-01-01\",\"cover_date\":\"2026-02-01\",\"image\":null,\"volume\":{\"id\":9}}")) + "]";

        var (client, handler) = Make(request =>
        {
            var offset = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["offset"]!);
            return (HttpStatusCode.OK, Ok(offset == 0 ? Page(1, 100) : Page(101, 30), total: 130, offset: offset));
        });

        var issues = await client.GetVolumeIssuesAsync(9, CancellationToken.None);

        Assert.Equal(130, issues.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("filter=volume:9", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("limit=100", handler.Requests[0].RequestUri!.Query);
        Assert.Contains("offset=100", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task GetVolumeIssues_ParsesDatesNumbersAndSkipsBadRows()
    {
        var (client, _) = Make(_ => (HttpStatusCode.OK, Ok("""
            [{"id":1,"issue_number":" 0 ","name":"Zero","store_date":"2026-09-16","cover_date":null,"image":{"small_url":"http://x/s.jpg"},"volume":{"id":9}},
             {"id":2,"issue_number":"Annual 1","name":null,"store_date":"0000-00-00","cover_date":"2026-10-01","image":null,"volume":{"id":9}},
             {"issue_number":"no id"}]
            """, 3)));

        var issues = await client.GetVolumeIssuesAsync(9, CancellationToken.None);

        Assert.Equal(2, issues.Count);                         // the row without an id is skipped
        Assert.Equal("0", issues[0].IssueNumber);              // trimmed, kept as text
        Assert.Equal(new DateTime(2026, 9, 16), issues[0].StoreDate);
        Assert.Equal("http://x/s.jpg", issues[0].ImageUrl);    // falls back to small_url
        Assert.Equal("Annual 1", issues[1].IssueNumber);
        Assert.Null(issues[1].StoreDate);                      // ComicVine's "0000-00-00" is not a date
        Assert.Equal(new DateTime(2026, 10, 1), issues[1].CoverDate);
    }

    [Fact]
    public async Task GetVolume_ReturnsNull_WhenComicVineSaysNotFound()
    {
        var (client, handler) = Make(_ => (HttpStatusCode.OK, """{"status_code":101,"error":"Object Not Found","results":[]}"""));

        Assert.Null(await client.GetVolumeAsync(123, CancellationToken.None));
        Assert.Contains("/volume/4050-123/", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task InvalidKey_MapsToAClearError()
    {
        var (client, _) = Make(_ => (HttpStatusCode.OK, """{"status_code":100,"error":"Invalid API Key","results":[]}"""));

        var ex = await Assert.ThrowsAsync<ComicVineException>(() => client.SearchVolumesAsync("x", CancellationToken.None));

        Assert.Equal(100, ex.ApiStatusCode);
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public async Task Status107_PausesTheSharedLimiter_AndThrows()
    {
        var handlerInner = new FakeHandler(_ => (HttpStatusCode.OK, """{"status_code":107,"error":"Rate limit exceeded. Slow down cowboy.","results":[]}"""));
        var limiter = new ComicVineRateLimitHandler(handlerInner);
        var client = new ComicVineClient("KEY", ComicVineRequestPriority.Low, new HttpClient(handlerInner), limiter);

        var ex = await Assert.ThrowsAsync<ComicVineException>(() => client.GetVolumeIssuesAsync(1, CancellationToken.None));

        Assert.Equal(107, ex.ApiStatusCode);
        Assert.NotNull(limiter.PausedUntil);
    }

    [Fact]
    public async Task HttpFailure_AndGarbageBody_BothBecomeComicVineException()
    {
        var (failing, _) = Make(_ => (HttpStatusCode.InternalServerError, "boom"));
        await Assert.ThrowsAsync<ComicVineException>(() => failing.SearchVolumesAsync("x", CancellationToken.None));

        var (garbage, _) = Make(_ => (HttpStatusCode.OK, "<html>not json</html>"));
        await Assert.ThrowsAsync<ComicVineException>(() => garbage.SearchVolumesAsync("x", CancellationToken.None));
    }

    [Fact]
    public async Task RequestsCarryTheConfiguredPriority()
    {
        var (low, lowHandler) = Make(_ => (HttpStatusCode.OK, Ok("[]", 0)), ComicVineRequestPriority.Low);
        var (high, highHandler) = Make(_ => (HttpStatusCode.OK, Ok("[]", 0)));

        await low.SearchVolumesAsync("x", CancellationToken.None);
        await high.SearchVolumesAsync("x", CancellationToken.None);

        Assert.True(lowHandler.Requests[0].Options.TryGetValue(ComicVineRateLimitHandler.PriorityKey, out var lowPriority));
        Assert.Equal(ComicVineRequestPriority.Low, lowPriority);
        Assert.True(highHandler.Requests[0].Options.TryGetValue(ComicVineRateLimitHandler.PriorityKey, out var highPriority));
        Assert.Equal(ComicVineRequestPriority.High, highPriority);
    }

    [Fact]
    public async Task GetIssueDetails_MapsCreditsPeopleAndDates_AndStripsTheHtmlSummary()
    {
        var (client, handler) = Make(_ => (HttpStatusCode.OK, Ok("""
            {"id":4321,"name":"Endgame","issue_number":"263","site_detail_url":"https://cv/263","cover_date":"2016-05-01","store_date":"2016-05-04 00:00:00",
             "description":"<p>Al &amp; Jim <b>fight</b>.</p>","volume":{"id":91273,"name":"Spawn"},
             "story_arc_credits":[{"name":"Endgame Arc"}],"character_credits":[{"name":"Spawn"},{"name":"Sam"}],
             "team_credits":[],"location_credits":[{"name":"Rat City"}],
             "person_credits":[{"name":"Todd","role":"writer, artist"},{"name":"Greg","role":"colorer"},{"name":"Nobody","role":"tea boy"}]}
            """, 1)));

        var details = await ((IComicVineIssueDetailsSource)client).GetIssueDetailsAsync(4321, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(4321, details!.Id);
        Assert.Equal(91273, details.VolumeId);
        Assert.Equal("Spawn", details.VolumeName);
        Assert.Equal("263", details.IssueNumber);
        Assert.Equal("Endgame", details.Title);
        Assert.Equal(new ComicVineDatePart(2016, 5, 1), details.PublishedDate);
        Assert.Equal(new ComicVineDatePart(2016, 5, 4), details.ReleasedDate);
        Assert.Equal("Al & Jim fight .", details.Summary);
        Assert.Equal(new[] { "Endgame Arc" }, details.StoryArcs);
        Assert.Equal(new[] { "Spawn", "Sam" }, details.Characters);
        Assert.Empty(details.Teams);
        Assert.Equal(new[] { "Rat City" }, details.Locations);
        Assert.Equal(new ComicVineCredit("Todd", "Writer"), details.Credits[0]);
        Assert.Equal(new ComicVineCredit("Greg", "Colorist"), details.Credits[1]);
        Assert.Null(details.Credits[2].Field);                                    // a role with no Paperbunkr equivalent is kept, unmapped
        Assert.Contains("/issue/4000-4321/", handler.Requests[0].RequestUri!.OriginalString);
    }

    [Fact]
    public async Task GetIssueDetails_AnObjectNotFoundStatus_IsNull_ButOtherErrorsStillThrow()
    {
        var (missing, _) = Make(_ => (HttpStatusCode.OK, """{"status_code":101,"error":"Object Not Found","results":[]}"""));
        Assert.Null(await ((IComicVineIssueDetailsSource)missing).GetIssueDetailsAsync(1, CancellationToken.None));

        var (badKey, _) = Make(_ => (HttpStatusCode.OK, """{"status_code":100,"error":"Invalid API Key","results":[]}"""));
        await Assert.ThrowsAsync<ComicVineException>(() => ((IComicVineIssueDetailsSource)badKey).GetIssueDetailsAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task SearchVolumes_Paged_WalksPastTheFirst25_UntilTheTotalOrTheCap()
    {
        static string Page(int from, int count) =>
            "[" + string.Join(",", Enumerable.Range(from, count).Select(i =>
                "{\"id\":" + i + ",\"name\":\"Captain America " + i + "\",\"publisher\":{\"name\":\"Marvel\"},\"start_year\":\"2018\",\"count_of_issues\":" + (300 - i) + ",\"image\":null}")) + "]";

        var (client, handler) = Make(request =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            int offset = int.Parse(query["offset"]!);
            int limit = int.Parse(query["limit"]!);
            return (HttpStatusCode.OK, Ok(Page(offset, Math.Min(limit, 250 - offset)), total: 250, offset: offset));
        });

        var all = await ((IComicVineVolumeSearch)client).SearchVolumesAsync("Captain America", 300, CancellationToken.None);
        Assert.Equal(250, all.Count);                                            // stops at ComicVine's own total
        Assert.Equal(3, handler.Requests.Count);                                 // 100 + 100 + 50
        Assert.All(handler.Requests, r => Assert.Contains("limit=", r.RequestUri!.OriginalString));

        var (capped, cappedHandler) = Make(request =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            int offset = int.Parse(query["offset"]!);
            return (HttpStatusCode.OK, Ok(Page(offset, int.Parse(query["limit"]!)), total: 1000, offset: offset));
        });
        var some = await ((IComicVineVolumeSearch)capped).SearchVolumesAsync("x", 150, CancellationToken.None);
        Assert.Equal(150, some.Count);                                           // the cap is honoured even when ComicVine has more
        Assert.Equal(2, cappedHandler.Requests.Count);
    }

    [Fact]
    public async Task GetReleases_FiltersByStoreDate_PagesThroughAll_AndSkipsUndatedRows()
    {
        var (client, handler) = Make(request =>
        {
            bool second = request.RequestUri!.OriginalString.Contains("offset=2");
            return (HttpStatusCode.OK, second
                ? Ok("""[{"id":903,"issue_number":"1","name":null,"store_date":"2026-10-07","cover_date":"2026-11-01","image":null,"volume":{"id":20,"name":"Batman"}}]""", total: 3, offset: 2)
                : Ok("""
                    [{"id":901,"issue_number":"350","name":"Endgame","store_date":"2026-09-30","cover_date":"2026-10-01","image":{"medium_url":"http://x/s.jpg"},"volume":{"id":10,"name":"Spawn"}},
                     {"id":902,"issue_number":"351","name":null,"store_date":null,"cover_date":null,"image":null,"volume":{"id":10,"name":"Spawn"}}]
                    """, total: 3));
        });

        var releases = await ((IPullListSource)client).GetReleasesAsync(new DateTime(2026, 9, 23), new DateTime(2026, 10, 21), CancellationToken.None);

        Assert.Equal(new[] { 901, 903 }, releases.Select(r => r.IssueId));                  // 902 has no store date
        Assert.Equal(new PullListEntry(901, 10, "Spawn", "350", new DateTime(2026, 9, 30), new DateTime(2026, 10, 1), "http://x/s.jpg"), releases[0]);
        var first = handler.Requests[0].RequestUri!.OriginalString;
        Assert.Contains("/issues/?api_key=KEY", first);
        Assert.Contains("filter=store_date:2026-09-23|2026-10-21", first);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(Paperbunkr.Data.Entities.ComicProvider.ComicVine, ((IPullListSource)client).Kind);
    }

    [Fact]
    public async Task GetSeriesInfo_ReadsTheVolumesPublisher_WithNoCrossReferenceId()
    {
        var handlerJson = """{"status_code":1,"error":"OK","number_of_total_results":1,"offset":0,"results":{"id":10,"name":"Spawn","publisher":{"name":"Image"},"start_year":"1992","count_of_issues":355,"image":null}}""";
        var (single, _) = Make(_ => (HttpStatusCode.OK, handlerJson));

        var info = await ((IPullListSource)single).GetSeriesInfoAsync(10, CancellationToken.None);

        Assert.Equal(new PullListSeriesInfo(10, "Spawn", "Image", 1992, null), info);
    }
}
