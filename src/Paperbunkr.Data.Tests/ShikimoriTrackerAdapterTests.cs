using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Tracking;
using Paperbunkr.Data.Tracking.Adapters;
using Xunit;

namespace Paperbunkr.Data.Tests;

public class ShikimoriTrackerAdapterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ShikimoriTrackerAdapterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_shikimori_tracker_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public async Task SearchAsync_ParsesMangaListResults_PreferringNameOverRussian()
    {
        const string json = """
            [ { "id": 30013, "name": "One Piece", "russian": null, "url": "/mangas/30013-one-piece" } ]
            """;
        var adapter = new ShikimoriTrackerAdapter(new HttpClient(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, json))));

        var results = await adapter.SearchAsync("one piece", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("30013", results[0].ExternalId);
        Assert.Equal("One Piece", results[0].Title);
        Assert.Equal("https://shikimori.one/mangas/30013-one-piece", results[0].Url);
    }

    [Fact]
    public async Task SearchAsync_NonSuccessStatus_ThrowsProviderUnavailable()
    {
        var adapter = new ShikimoriTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        await Assert.ThrowsAsync<Paperbunkr.Data.Metadata.MetadataProviderUnavailableException>(() => adapter.SearchAsync("one piece", CancellationToken.None));
    }

    [Fact]
    public async Task PushEntryAsync_NoStoredAccessToken_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new ShikimoriTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "30013" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task PushEntryAsync_NoExistingRate_CreatesViaPost()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken, "token-abc");

        var adapter = new ShikimoriTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/users/whoami"))
            {
                return JsonResponse(HttpStatusCode.OK, """{ "id": 7 }""");
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/v2/user_rates") && req.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, "[]");
            }

            Assert.Equal(HttpMethod.Post, req.Method);
            return JsonResponse(HttpStatusCode.OK, """{ "id": 99 }""");
        })));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "30013" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task PushEntryAsync_ExistingRate_UpdatesViaPut()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken, "token-abc");

        var adapter = new ShikimoriTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/users/whoami"))
            {
                return JsonResponse(HttpStatusCode.OK, """{ "id": 7 }""");
            }
            if (req.RequestUri.AbsolutePath.EndsWith("/v2/user_rates") && req.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, """[ { "id": 555 } ]""");
            }

            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.EndsWith("/v2/user_rates/555", req.RequestUri.AbsolutePath);
            return JsonResponse(HttpStatusCode.OK, """{ "id": 555 }""");
        })));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "30013" }, new TrackerPushPayload(ReadingStatus.Completed, 100), CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task GetEntryAsync_NoStoredAccessToken_ReturnsNull_WithoutSendingRequest()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new ShikimoriTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "30013" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEntryAsync_ExistingRate_ParsesStatusAndChapters()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken, "token-abc");

        var adapter = new ShikimoriTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/users/whoami"))
            {
                return JsonResponse(HttpStatusCode.OK, """{ "id": 7 }""");
            }

            return JsonResponse(HttpStatusCode.OK, """[ { "id": 555, "status": "watching", "chapters": 42 } ]""");
        })));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "30013" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReadingStatus.Reading, result!.Status);
        Assert.Equal(42, result.ChapterProgress);
    }

    [Fact]
    public async Task GetEntryAsync_NoExistingRate_ReturnsNull()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken, "token-abc");

        var adapter = new ShikimoriTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/users/whoami"))
            {
                return JsonResponse(HttpStatusCode.OK, """{ "id": 7 }""");
            }

            return JsonResponse(HttpStatusCode.OK, "[]");
        })));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "30013" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("planned", ReadingStatus.Planned)]
    [InlineData("watching", ReadingStatus.Reading)]
    [InlineData("completed", ReadingStatus.Completed)]
    [InlineData("on_hold", ReadingStatus.Paused)]
    [InlineData("dropped", ReadingStatus.Dropped)]
    [InlineData("rewatching", ReadingStatus.ReReading)]
    [InlineData("something_else", ReadingStatus.Unknown)]
    public void ShikimoriStatusMapper_FromUserRateStatus_MapsEveryValue(string raw, ReadingStatus expected)
    {
        Assert.Equal(expected, ShikimoriStatusMapper.FromUserRateStatus(raw));
    }

    [Fact]
    public void BuildAuthorizationUrl_UsesOobRedirectAndUserRatesScope()
    {
        string url = ShikimoriTrackerAdapter.BuildAuthorizationUrl("client-1");

        Assert.Contains("urn%3Aietf%3Awg%3Aoauth%3A2.0%3Aoob", url);
        Assert.Contains("scope=user_rates", url);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request, cancellationToken));
    }
}
