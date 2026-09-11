using System.Diagnostics;
using ClusterLibraryManager.ComicVine;

namespace ClusterLibraryManager.Tests;

/// <summary>
/// Implementation plan Phase 3 Step 3.2 verification: endpoint URL shapes, throttle timing, retry-once
/// behavior, and DTO deserialization against fixture JSON - all against a fake
/// <see cref="HttpMessageHandler"/>, no live network call.
/// </summary>
public sealed class ComicVineServiceTests : IDisposable
{
    private const string VolumeSearchJson =
        """
        {"status_code":1,"error":"OK","results":[{"id":12345,"name":"Batman","start_year":"1940","publisher":{"name":"DC Comics"},"count_of_issues":800,"image":{"small_url":"https://example/small.jpg","medium_url":"https://example/medium.jpg"}}]}
        """;

    private const string VolumeDetailsJson =
        """
        {"status_code":1,"error":"OK","results":{"id":12345,"name":"Batman","start_year":"1940","publisher":{"name":"DC Comics"},"count_of_issues":800,"image":{"small_url":"https://example/small.jpg"}}}
        """;

    private const string IssueSearchJson =
        """
        {"status_code":1,"error":"OK","results":[{"id":555,"name":"The Beginning","issue_number":"1","image":{"small_url":"https://example/issue.jpg"}}]}
        """;

    private const string IssueDetailsJson =
        """
        {"status_code":1,"error":"OK","results":{"id":555,"name":"The Beginning","issue_number":"1","site_detail_url":"https://comicvine.gamespot.com/batman-1/4000-555/","cover_date":"1940-04-25","store_date":"1940-03-15","description":"<p>Batman <b>fights</b> crime.</p>","volume":{"id":12345,"name":"Batman"},"story_arc_credits":[{"id":1,"name":"Zero Year"}],"character_credits":[{"id":2,"name":"Batman"},{"id":3,"name":"Joker"}],"team_credits":[],"location_credits":[{"id":4,"name":"Gotham City"}],"person_credits":[{"name":"Bob Kane","role":"writer"},{"name":"Bill Finger","role":"writer, artist"}]}}
        """;

    private const string BadApiKeyJson = """{"status_code":100,"error":"Invalid API Key"}""";

    private readonly int _originalThrottle = ComicVineService.ThrottleMilliseconds;
    private readonly int _originalRetryDelay = ComicVineService.RetryDelayMilliseconds;

    public void Dispose()
    {
        ComicVineService.ThrottleMilliseconds = _originalThrottle;
        ComicVineService.RetryDelayMilliseconds = _originalRetryDelay;
    }

    [Fact]
    public async Task SearchVolumesAsync_page1_omits_the_page_parameter()
    {
        var handler = new FakeHttpMessageHandler(VolumeSearchJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        await service.SearchVolumesAsync("batman");

        string url = Assert.Single(handler.RequestedUrls);
        Assert.Contains("resources=volume", url);
        Assert.Contains("field_list=name%2Cstart_year%2Cpublisher%2Cid%2Cimage%2Ccount_of_issues", url);
        Assert.Contains("query=batman", url);
        Assert.Contains("api_key=KEY", url);
        Assert.Contains("format=json", url);
        Assert.DoesNotContain("page=", url);
    }

    [Fact]
    public async Task SearchVolumesAsync_page2_includes_the_page_parameter()
    {
        var handler = new FakeHttpMessageHandler(VolumeSearchJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        await service.SearchVolumesAsync("batman", page: 2);

        Assert.Contains("page=2", Assert.Single(handler.RequestedUrls));
    }

    [Fact]
    public async Task GetVolumeDetailsAsync_uses_the_4050_resource_prefix()
    {
        var handler = new FakeHttpMessageHandler(VolumeDetailsJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        await service.GetVolumeDetailsAsync(12345);

        Assert.Contains("/volume/4050-12345/", Assert.Single(handler.RequestedUrls));
    }

    [Fact]
    public async Task SearchIssuesAsync_filters_by_volume_id()
    {
        var handler = new FakeHttpMessageHandler(IssueSearchJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        await service.SearchIssuesAsync(12345);

        Assert.Contains("filter=volume%3A12345", Assert.Single(handler.RequestedUrls));
    }

    [Fact]
    public async Task GetIssueDetailsAsync_uses_the_4000_resource_prefix()
    {
        var handler = new FakeHttpMessageHandler(IssueDetailsJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        await service.GetIssueDetailsAsync(555);

        Assert.Contains("/issue/4000-555/", Assert.Single(handler.RequestedUrls));
    }

    [Fact]
    public async Task TestConnectionAsync_returns_true_on_a_successful_response()
    {
        var handler = new FakeHttpMessageHandler(VolumeSearchJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        Assert.True(await service.TestConnectionAsync());
    }

    [Fact]
    public async Task TestConnectionAsync_returns_false_on_a_bad_api_key_after_its_one_retry()
    {
        ComicVineService.RetryDelayMilliseconds = 10;
        var handler = new FakeHttpMessageHandler(BadApiKeyJson, BadApiKeyJson);
        var service = new ComicVineService("BAD", new HttpClient(handler));

        Assert.False(await service.TestConnectionAsync());
        Assert.Equal(2, handler.RequestedUrls.Count);
    }

    [Fact]
    public async Task A_failed_first_response_retries_once_and_succeeds()
    {
        ComicVineService.RetryDelayMilliseconds = 10;
        var handler = new FakeHttpMessageHandler(BadApiKeyJson, VolumeSearchJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        var results = await service.SearchVolumesAsync("batman");

        Assert.Single(results);
        Assert.Equal(2, handler.RequestedUrls.Count);
    }

    [Fact]
    public async Task Two_failures_in_a_row_propagate_after_exactly_one_retry_not_more()
    {
        ComicVineService.RetryDelayMilliseconds = 10;
        var handler = new FakeHttpMessageHandler(BadApiKeyJson, BadApiKeyJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<ComicVineException>(() => service.SearchVolumesAsync("batman"));

        Assert.Equal(100, ex.StatusCode);
        Assert.Equal(2, handler.RequestedUrls.Count);
    }

    [Fact]
    public async Task Consecutive_requests_are_throttled_at_least_the_configured_interval_apart()
    {
        ComicVineService.ThrottleMilliseconds = 150;
        var handler = new FakeHttpMessageHandler(VolumeSearchJson, VolumeSearchJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        var stopwatch = Stopwatch.StartNew();
        await service.SearchVolumesAsync("batman");
        await service.SearchVolumesAsync("robin");
        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds >= 150, $"Expected at least 150ms between two calls, took {stopwatch.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task Volume_search_result_maps_every_field_including_the_smallest_available_image_url()
    {
        var handler = new FakeHttpMessageHandler(VolumeSearchJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        var result = Assert.Single(await service.SearchVolumesAsync("batman"));

        Assert.Equal(12345, result.Id);
        Assert.Equal("Batman", result.Name);
        Assert.Equal("1940", result.StartYear);
        Assert.Equal("DC Comics", result.Publisher);
        Assert.Equal(800, result.CountOfIssues);
        Assert.Equal("https://example/small.jpg", result.ImageUrl);
    }

    [Fact]
    public async Task Issue_details_maps_dates_credits_and_strips_html_from_the_description()
    {
        var handler = new FakeHttpMessageHandler(IssueDetailsJson);
        var service = new ComicVineService("KEY", new HttpClient(handler));

        var result = await service.GetIssueDetailsAsync(555);

        Assert.NotNull(result);
        Assert.Equal(555, result!.Id);
        Assert.Equal(12345, result.VolumeId);
        Assert.Equal("Batman", result.VolumeName);
        Assert.Equal("1", result.IssueNumber);
        Assert.Equal(new ComicVineDatePart(1940, 4, 25), result.PublishedDate);
        Assert.Equal(new ComicVineDatePart(1940, 3, 15), result.ReleasedDate);
        Assert.Equal("Batman fights crime.", result.Summary);
        Assert.Equal(new[] { "Zero Year" }, result.StoryArcs);
        Assert.Equal(new[] { "Batman", "Joker" }, result.Characters);
        Assert.Equal(new[] { "Gotham City" }, result.Locations);
        Assert.Contains(result.Credits, c => c.Name == "Bob Kane" && c.Field == "Writer");
        Assert.Contains(result.Credits, c => c.Name == "Bill Finger" && c.Field == "Writer");
    }
}
