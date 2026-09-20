using System.Net;
using System.Net.Http;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>Fixture-based (no live network): the JSON shapes below are Metron's own serializer output (series list, issue list, issue detail).</summary>
public class MetronClientTests
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

    private static (MetronClient Client, FakeHandler Handler) Make(Func<HttpRequestMessage, (HttpStatusCode, string)> respond)
    {
        var handler = new FakeHandler(respond);
        return (new MetronClient("reader", "pw", ComicVineRequestPriority.High, new HttpClient(handler)), handler);
    }

    private static string Page(string results, string? next = null) =>
        $$"""{"count":1,"next":{{(next is null ? "null" : "\"" + next + "\"")}},"previous":null,"results":{{results}}}""";

    private const string SeriesRow = """
        {"id":{0},"series":"{1}","year_began":{2},"year_end":null,"volume":1,"issue_count":{3},"publisher":{"id":1,"name":"Marvel"},"series_type":{"id":1,"name":"Ongoing Series"},"cv_id":null,"gcd_id":null,"modified":"2026-01-01T00:00:00Z"}
        """;

    private static string Series(int id, string display, int year, int count) =>
        SeriesRow.Replace("{0}", id.ToString()).Replace("{1}", display).Replace("{2}", year.ToString()).Replace("{3}", count.ToString());

    [Fact]
    public async Task SearchVolumes_MapsSeriesRows_StripsTheYearFromTheName_AndSendsBasicAuth()
    {
        var (client, handler) = Make(_ => (HttpStatusCode.OK, Page("[" + Series(10, "Captain America (2018)", 2018, 30) + "," + Series(11, "Captain America (1968)", 1968, 355) + "]")));

        var volumes = await ((IComicVineVolumeSearch)client).SearchVolumesAsync("Captain America", 100, CancellationToken.None);

        Assert.Equal(2, volumes.Count);
        Assert.Equal(new ComicVineVolume(11, "Captain America", "Marvel", 1968, 355, null), volumes[0]);      // most issues first, as ComicVine's order
        Assert.Equal("Captain America", volumes[1].Name);
        Assert.Null(volumes[0].ImageUrl);                                                                      // a Metron series has no cover
        var request = handler.Requests[0];
        Assert.Contains("/series/?name=Captain%20America", request.RequestUri!.OriginalString);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String("reader:pw"u8.ToArray()), request.Headers.Authorization.Parameter);
        Assert.Equal(ComicProvider.Metron, client.Kind);
    }

    [Theory]
    [InlineData("Captain America (2018)", 2018, "Captain America")]
    [InlineData("Captain America TPB (2018)", 2018, "Captain America TPB")]
    [InlineData("Batman (2016) Digital", 2016, "Batman Digital")]
    [InlineData("Batman (2016)", 2020, "Batman (2016)")]            // a different year is part of the name: left alone
    [InlineData("Spawn", 1992, "Spawn")]
    public void CleanName_RemovesOnlyTheYearMetronAppended(string display, int year, string expected) =>
        Assert.Equal(expected, MetronClient.CleanName(display, year));

    [Fact]
    public async Task SearchVolumes_FollowsThePagination_UntilTheCap()
    {
        var (client, handler) = Make(request =>
        {
            bool second = request.RequestUri!.OriginalString.Contains("page=2");
            return (HttpStatusCode.OK, second
                ? Page("[" + Series(3, "Spawn Zero (1999)", 1999, 5) + "]")
                : Page("[" + Series(1, "Spawn (1992)", 1992, 300) + "," + Series(2, "Spawn (2016)", 2016, 20) + "]", "https://metron.cloud/api/series/?name=Spawn&page=2"));
        });

        var all = await ((IComicVineVolumeSearch)client).SearchVolumesAsync("Spawn", 300, CancellationToken.None);
        Assert.Equal(new[] { 1, 2, 3 }, all.Select(v => v.Id));
        Assert.Equal(2, handler.Requests.Count);

        var (capped, cappedHandler) = Make(_ => (HttpStatusCode.OK, Page("[" + Series(1, "A (2000)", 2000, 9) + "," + Series(2, "B (2001)", 2001, 8) + "]", "https://metron.cloud/api/series/?page=2")));
        var some = await ((IComicVineVolumeSearch)capped).SearchVolumesAsync("x", 2, CancellationToken.None);
        Assert.Equal(2, some.Count);
        Assert.Single(cappedHandler.Requests);                          // the cap stops the walk
    }

    [Fact]
    public async Task GetVolumeIssues_ReadsTheIssueList_WithNumbersDatesAndCovers()
    {
        var (client, handler) = Make(_ => (HttpStatusCode.OK, Page("""
            [{"id":900,"series":{"id":10,"name":"Captain America","volume":1,"year_began":2018},"number":"1","issue":"Captain America (2018) #1","cover_date":"2018-07-01","store_date":"2018-05-09","image":"https://x/1.jpg","cover_hash":"abc","modified":"2026-01-01T00:00:00Z"},
             {"id":901,"series":{"id":10,"name":"Captain America","volume":1,"year_began":2018},"number":"2","issue":"Captain America (2018) #2","cover_date":"2018-08-01","store_date":null,"image":null,"cover_hash":null,"modified":"2026-01-01T00:00:00Z"}]
            """)));

        var issues = await client.GetVolumeIssuesAsync(10, CancellationToken.None);

        Assert.Equal(2, issues.Count);
        Assert.Equal(new ComicVineIssue(900, "1", null, new DateTime(2018, 5, 9), new DateTime(2018, 7, 1), "https://x/1.jpg", 10), issues[0]);
        Assert.Null(issues[1].StoreDate);
        Assert.Contains("/series/10/issue_list/", handler.Requests[0].RequestUri!.OriginalString);
    }

    [Fact]
    public async Task GetIssueDetails_MapsCreditsRolesToPaperbunkrFields_AndEverythingElse()
    {
        var (client, handler) = Make(_ => (HttpStatusCode.OK, """
            {"id":900,"publisher":{"id":1,"name":"Marvel"},"series":{"id":10,"name":"Captain America","volume":1,"year_began":2018},"number":"1","title":"","name":["Winter Soldier","Coda"],
             "cover_date":"2018-07-01","store_date":"2018-05-09","desc":"<p>Steve &amp; Bucky.</p>","image":"https://x/1.jpg","resource_url":"https://metron.cloud/issue/900/",
             "arcs":[{"id":5,"name":"Winter Arc","modified":"x"}],
             "credits":[{"id":1,"creator":"Ta-Nehisi Coates","role":[{"id":1,"name":"Writer"}]},
                        {"id":2,"creator":"Leinil Yu","role":[{"id":2,"name":"Penciller"},{"id":3,"name":"Inker"}]},
                        {"id":3,"creator":"Someone","role":[{"id":9,"name":"Translator"}]}],
             "characters":[{"id":7,"name":"Captain America","modified":"x"}],"teams":[{"id":8,"name":"Avengers","modified":"x"}],"universes":[],"cv_id":null}
            """));

        var details = await client.GetIssueDetailsAsync(900, CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(900, details!.Id);
        Assert.Equal(10, details.VolumeId);
        Assert.Equal("Captain America", details.VolumeName);
        Assert.Equal("1", details.IssueNumber);
        Assert.Equal("Winter Soldier / Coda", details.Title);                     // an empty title falls back to the story titles
        Assert.Equal(new ComicVineDatePart(2018, 7, 1), details.PublishedDate);
        Assert.Equal(new ComicVineDatePart(2018, 5, 9), details.ReleasedDate);
        Assert.Equal("Steve & Bucky.", details.Summary);
        Assert.Equal(new[] { "Winter Arc" }, details.StoryArcs);
        Assert.Equal(new[] { "Captain America" }, details.Characters);
        Assert.Equal(new[] { "Avengers" }, details.Teams);
        Assert.Empty(details.Locations);
        Assert.Contains(new ComicVineCredit("Ta-Nehisi Coates", "Writer"), details.Credits);
        Assert.Contains(new ComicVineCredit("Leinil Yu", "Penciller"), details.Credits);
        Assert.Contains(new ComicVineCredit("Leinil Yu", "Inker"), details.Credits);   // two roles, two fields
        Assert.Contains(new ComicVineCredit("Someone", null), details.Credits);         // a role with no equivalent is kept, unmapped
        Assert.Contains("/issue/900/", handler.Requests[0].RequestUri!.OriginalString);
    }

    [Fact]
    public async Task Errors_ReuseComicVinesNumbering_SoCallersTreatBothProvidersAlike()
    {
        static async Task<ComicVineException> Failure(HttpStatusCode status)
        {
            var (client, _) = Make(_ => (status, "{}"));
            return await Assert.ThrowsAsync<ComicVineException>(() => client.GetVolumeIssuesAsync(1, CancellationToken.None));
        }

        Assert.Equal(100, (await Failure(HttpStatusCode.Unauthorized)).ApiStatusCode);        // the login was refused
        Assert.Equal(100, (await Failure(HttpStatusCode.Forbidden)).ApiStatusCode);
        Assert.Equal(107, (await Failure(HttpStatusCode.TooManyRequests)).ApiStatusCode);     // rate limited: retry later
        Assert.Null((await Failure(HttpStatusCode.InternalServerError)).ApiStatusCode);       // a plain failure

        var (notFound, _) = Make(_ => (HttpStatusCode.NotFound, "{}"));
        Assert.Null(await notFound.GetIssueDetailsAsync(1, CancellationToken.None));          // missing issue: null, as for ComicVine
        Assert.Null(await notFound.GetVolumeAsync(1, CancellationToken.None));
    }
}

public class ComicProviderFactoryTests : Acquisition.AcquisitionTestBase
{
    [Fact]
    public void ProvidersAreBuiltFromTheSavedCredentials_AndSayWhatIsMissing()
    {
        Assert.Null(ComicProviderFactory.Create(Context, ComicProvider.ComicVine));
        Assert.Null(ComicProviderFactory.Create(Context, ComicProvider.Metron));
        Assert.Contains("Metron login", ComicProviderFactory.MissingCredentialsMessage(ComicProvider.Metron));
        Assert.Contains("ComicVine API key", ComicProviderFactory.MissingCredentialsMessage(ComicProvider.ComicVine));

        Paperbunkr.Data.Credentials.CredentialStore.Set(Context, "ComicVine", CredentialKind.ApiKey, "KEY");
        Paperbunkr.Data.Credentials.CredentialStore.Set(Context, "Metron", CredentialKind.Username, "reader");
        Assert.Null(ComicProviderFactory.Create(Context, ComicProvider.Metron));              // a username alone isn't a login
        Paperbunkr.Data.Credentials.CredentialStore.Set(Context, "Metron", CredentialKind.Password, "pw");

        Assert.Equal(ComicProvider.ComicVine, ComicProviderFactory.Create(Context, ComicProvider.ComicVine)!.Kind);
        Assert.Equal(ComicProvider.Metron, ComicProviderFactory.Create(Context, ComicProvider.Metron)!.Kind);
    }

    [Theory]
    [InlineData("Metron", ComicProvider.Metron)]
    [InlineData(" metron ", ComicProvider.Metron)]
    [InlineData("ComicVine", ComicProvider.ComicVine)]
    [InlineData("", ComicProvider.ComicVine)]
    [InlineData(null, ComicProvider.ComicVine)]
    public void Parse_DefaultsToComicVine(string? text, ComicProvider expected) => Assert.Equal(expected, ComicProviderFactory.Parse(text));
}
