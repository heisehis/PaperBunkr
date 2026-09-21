using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Paperbunkr.Sharing.Protocol;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.Sharing.Tests;

public sealed class FakeCatalog : IShareCatalogSource
{
    public string Version { get; set; } = "v1";
    public HashSet<int> Shared { get; } = new() { 1 };
    public Exception? ThrowOnVersion { get; set; }

    public Task<string> GetCatalogVersionAsync(CancellationToken ct) =>
        ThrowOnVersion is null ? Task.FromResult(Version) : Task.FromException<string>(ThrowOnVersion);

    public Task<CatalogPage> GetCatalogPageAsync(string? cursor, int pageSize, CancellationToken ct)
    {
        var series = new[] { new CatalogSeriesDto(1, "Saga", null, "Comic", "LeftToRight", "Ongoing", null, null, null, null) };
        var issues = cursor is null
            ? new[] { Issue(1, "one") }
            : new[] { Issue(2, "two") };
        return Task.FromResult(new CatalogPage("ignored", series, issues, cursor is null ? "page2" : null));
    }

    public Task<IReadOnlyList<SharedListDto>> GetListsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SharedListDto>>(new[] { new SharedListDto(7, "Favorites", "ReadingList") });

    public Task<bool> IsIssueSharedAsync(int issueId, CancellationToken ct) => Task.FromResult(Shared.Contains(issueId));

    private static CatalogIssueDto Issue(int id, string title) => new(
        id, 1, title, id.ToString(), null, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        null, null, null, null, null, null, null, null, null, null, "Unknown", null, null, Array.Empty<IssueTagDto>());
}

public sealed class FakePages : ISharePageSource
{
    public static readonly byte[] PageBytes = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
    public int? LastRequestedWidth { get; private set; }

    public Task<PagesResponse?> GetPagesAsync(int issueId, CancellationToken ct) =>
        Task.FromResult<PagesResponse?>(new PagesResponse(2, new[] { new PageInfoDto(0, 100, 150, "Story"), new PageInfoDto(1, 100, 150, "Story") }));

    public Task<PageContent?> GetPageAsync(int issueId, int pageIndex, int? maxWidth, CancellationToken ct)
    {
        LastRequestedWidth = maxWidth;
        return Task.FromResult<PageContent?>(pageIndex > 1 ? null : new PageContent(new MemoryStream(PageBytes), "image/jpeg"));
    }

    public Task<PageContent?> GetCoverAsync(int issueId, int? maxWidth, CancellationToken ct)
    {
        LastRequestedWidth = maxWidth;
        return Task.FromResult<PageContent?>(new PageContent(new MemoryStream(PageBytes), "image/jpeg"));
    }
}

public sealed class ShareServerTests : IAsyncLifetime
{
    private const string Password = "s3cret-pass";

    private readonly string _certDir = Path.Combine(Path.GetTempPath(), $"paperbunkr_srv_{Guid.NewGuid():N}");
    private readonly FakeCatalog _catalog = new();
    private readonly FakePages _pages = new();
    private readonly List<string> _log = new();
    private ShareServer _server = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        _server = Create(o => { });
        await _server.StartAsync();
        _http = NewClient();
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
        if (Directory.Exists(_certDir)) Directory.Delete(_certDir, recursive: true);
    }

    private ShareServer Create(Action<ShareServerOptions> configure)
    {
        var options = new ShareServerOptions
        {
            Port = 0,
            PasswordHash = PasswordHasher.Hash(Password, 1_000),
            DisplayName = "Test Host",
            InstanceId = "11111111-1111-1111-1111-111111111111",
            MaxFailedAttempts = 3,
            Log = _log.Add,
        };
        configure(options);
        return new ShareServer(options, _catalog, _pages, new CertificateManager(_certDir).GetOrCreate());
    }

    private HttpClient NewClient()
    {
        var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        return new HttpClient(handler) { BaseAddress = new Uri($"https://127.0.0.1:{_server.Port}") };
    }

    private async Task<string> LoginAsync()
    {
        var response = await _http.PostAsJsonAsync("/v1/session", new SessionRequest(Password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SessionResponse>())!.Token;
    }

    private async Task<HttpRequestMessage> AuthedAsync(string url, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync());
        return request;
    }

    [Fact]
    public async Task Hello_IsOpen_ReportsIdentity_AndSetsProtocolHeader()
    {
        var response = await _http.GetAsync("/v1/hello");
        var hello = await response.Content.ReadFromJsonAsync<HelloResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues(ProtocolVersion.HeaderName).Single());
        Assert.Equal("Test Host", hello!.DisplayName);
        Assert.Equal("11111111-1111-1111-1111-111111111111", hello.InstanceId);
        Assert.True(hello.RequiresPassword);
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Start_WithoutAPassword_Refuses()
    {
        await using var server = Create(o => o.PasswordHash = "");

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());
    }

    [Theory]
    [InlineData("/v1/catalog")]
    [InlineData("/v1/lists")]
    [InlineData("/v1/issues/1/pages")]
    [InlineData("/v1/issues/1/pages/0")]
    [InlineData("/v1/issues/1/cover")]
    public async Task ProtectedEndpoints_Return401_WithoutAToken(string url)
    {
        var response = await _http.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoints_Return401_ForAForgedToken()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/v1/catalog");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "forged");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Session_RejectsWrongPassword_AndIssuesATokenForTheRightOne()
    {
        var bad = await _http.PostAsJsonAsync("/v1/session", new SessionRequest("nope"));
        var good = await _http.PostAsJsonAsync("/v1/session", new SessionRequest(Password));

        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
        Assert.False(string.IsNullOrEmpty((await good.Content.ReadFromJsonAsync<SessionResponse>())!.Token));
    }

    [Fact]
    public async Task Session_RepeatedFailures_LockTheClientOut_With429AndRetryAfter()
    {
        var alerts = new List<string>();
        _server.AuthFailed += alerts.Add;

        for (int i = 0; i < 3; i++)
        {
            await _http.PostAsJsonAsync("/v1/session", new SessionRequest("nope"));
        }

        // Even the correct password is refused while locked out.
        var blocked = await _http.PostAsJsonAsync("/v1/session", new SessionRequest(Password));

        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter is not null);
        Assert.Equal(3, alerts.Count);
    }

    [Fact]
    public async Task Session_MalformedBody_IsAFailedAttempt_NotACrash()
    {
        var response = await _http.PostAsync("/v1/session", new StringContent("{not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Catalog_ReturnsPagesWithCursor_AndSetsETag()
    {
        var first = await _http.SendAsync(await AuthedAsync("/v1/catalog"));
        var page = await first.Content.ReadFromJsonAsync<CatalogPage>(new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("\"v1\"", first.Headers.ETag!.Tag);
        Assert.Equal("v1", page!.CatalogVersion);
        Assert.Equal("one", page.Issues.Single().Title);
        Assert.Equal("page2", page.NextCursor);

        var second = await _http.SendAsync(await AuthedAsync("/v1/catalog?cursor=page2"));
        var last = await second.Content.ReadFromJsonAsync<CatalogPage>();
        Assert.Equal("two", last!.Issues.Single().Title);
        Assert.Null(last.NextCursor);
    }

    [Fact]
    public async Task Catalog_IfNoneMatch_ReturnsNotModified_ForTheCurrentVersion_Only()
    {
        var unchanged = await AuthedAsync("/v1/catalog");
        unchanged.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"v1\""));
        Assert.Equal(HttpStatusCode.NotModified, (await _http.SendAsync(unchanged)).StatusCode);

        _catalog.Version = "v2";
        var changed = await AuthedAsync("/v1/catalog");
        changed.Headers.IfNoneMatch.Add(new EntityTagHeaderValue("\"v1\""));
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(changed)).StatusCode);
    }

    [Fact]
    public async Task Lists_ReturnsSharedLists()
    {
        var response = await _http.SendAsync(await AuthedAsync("/v1/lists"));
        var lists = await response.Content.ReadFromJsonAsync<List<SharedListDto>>();

        Assert.Equal("Favorites", lists!.Single().Name);
    }

    [Fact]
    public async Task Pages_ReturnsPageInfo()
    {
        var response = await _http.SendAsync(await AuthedAsync("/v1/issues/1/pages"));
        var pages = await response.Content.ReadFromJsonAsync<PagesResponse>();

        Assert.Equal(2, pages!.PageCount);
    }

    [Fact]
    public async Task Page_ReturnsOriginalBytes_WithContentType()
    {
        var response = await _http.SendAsync(await AuthedAsync("/v1/issues/1/pages/0"));

        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(FakePages.PageBytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Null(_pages.LastRequestedWidth);
    }

    [Fact]
    public async Task Page_SupportsRangeRequests()
    {
        var request = await AuthedAsync("/v1/issues/1/pages/0");
        request.Headers.Range = new RangeHeaderValue(10, 19);

        var response = await _http.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(FakePages.PageBytes[10..20], await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("?w=1", 16)]          // clamped up
    [InlineData("?w=500", 500)]
    [InlineData("?w=999999", 4096)]   // clamped down
    public async Task Page_WidthParameter_IsClampedBeforeReachingTheProvider(string query, int expected)
    {
        await _http.SendAsync(await AuthedAsync("/v1/issues/1/pages/0" + query));

        Assert.Equal(expected, _pages.LastRequestedWidth);
    }

    [Fact]
    public async Task Cover_IsServed_WithWidth()
    {
        var response = await _http.SendAsync(await AuthedAsync("/v1/issues/1/cover?w=300"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(300, _pages.LastRequestedWidth);
    }

    [Theory]
    [InlineData("/v1/issues/99/pages")]
    [InlineData("/v1/issues/99/pages/0")]
    [InlineData("/v1/issues/99/cover")]
    public async Task IssuesOutsideTheSharingScope_Return404_NotForbidden(string url)
    {
        var response = await _http.SendAsync(await AuthedAsync(url));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MissingPage_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(await AuthedAsync("/v1/issues/1/pages/5"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(await AuthedAsync("/v1/issues/1/pages/-1"))).StatusCode);
    }

    [Fact]
    public async Task UnhandledFailures_ReachTheClientAsAGenericBody_AndTheHostLog()
    {
        _catalog.ThrowOnVersion = new InvalidOperationException("secret C:\\Users\\host\\comics\\path");

        var response = await _http.SendAsync(await AuthedAsync("/v1/catalog"));
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("internal_error", body);
        Assert.DoesNotContain("secret", body);
        Assert.DoesNotContain("C:\\", body);
        Assert.Contains(_log, l => l.Contains("secret"));
    }

    [Fact]
    public async Task Stop_InvalidatesEverySession()
    {
        await LoginAsync();
        Assert.Equal(1, _server.Sessions.ActiveCount);

        await _server.StopAsync();

        Assert.Equal(0, _server.Sessions.ActiveCount);
    }
}
