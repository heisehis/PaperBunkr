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

public class MyAnimeListTrackerAdapterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public MyAnimeListTrackerAdapterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mal_tracker_test_{Guid.NewGuid():N}.db");
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
    public async Task SearchAsync_NoClientId_ReturnsEmpty_WithoutSendingRequest()
    {
        var adapter = new MyAnimeListTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))), clientId: null);

        var results = await adapter.SearchAsync("one piece", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ParsesNodeWrappedResults()
    {
        const string json = """
            { "data": [
                { "node": { "id": 13, "title": "One Piece" } },
                { "node": { "id": 44, "title": "Naruto" } }
            ] }
            """;
        var adapter = new MyAnimeListTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.True(req.Headers.Contains("X-MAL-CLIENT-ID"));
            return JsonResponse(HttpStatusCode.OK, json);
        })), clientId: "client-abc");

        var results = await adapter.SearchAsync("one piece", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("13", results[0].ExternalId);
        Assert.Equal("One Piece", results[0].Title);
        Assert.Equal("https://myanimelist.net/manga/13", results[0].Url);
    }

    [Fact]
    public async Task SearchAsync_NonSuccessStatus_ThrowsProviderUnavailable()
    {
        var adapter = new MyAnimeListTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))), clientId: "client-abc");

        await Assert.ThrowsAsync<Paperbunkr.Data.Metadata.MetadataProviderUnavailableException>(() => adapter.SearchAsync("one piece", CancellationToken.None));
    }

    [Fact]
    public async Task PushEntryAsync_NoStoredAccessToken_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new MyAnimeListTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))), clientId: null);

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "13" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task PushEntryAsync_SuccessfulPut_ReturnsTrue()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken, "token-xyz");

        var adapter = new MyAnimeListTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            return JsonResponse(HttpStatusCode.OK, "{}");
        })), clientId: null);

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "13" }, new TrackerPushPayload(ReadingStatus.ReReading, 5), CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task GetEntryAsync_NoStoredAccessToken_ReturnsNull_WithoutSendingRequest()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new MyAnimeListTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))), clientId: null);

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "13" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEntryAsync_ParsesMyListStatus_RereadingFlagWinsOverRawStatus()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken, "token-xyz");

        const string json = """{ "my_list_status": { "status": "reading", "num_chapters_read": 7, "is_rereading": true } }""";
        var adapter = new MyAnimeListTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            return JsonResponse(HttpStatusCode.OK, json);
        })), clientId: null);

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "13" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReadingStatus.ReReading, result!.Status);
        Assert.Equal(7, result.ChapterProgress);
    }

    [Fact]
    public async Task GetEntryAsync_NoMyListStatus_ReturnsNull()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken, "token-xyz");

        var adapter = new MyAnimeListTrackerAdapter(new HttpClient(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, "{}"))), clientId: null);

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "13" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("plan_to_read", ReadingStatus.Planned)]
    [InlineData("reading", ReadingStatus.Reading)]
    [InlineData("completed", ReadingStatus.Completed)]
    [InlineData("on_hold", ReadingStatus.Paused)]
    [InlineData("dropped", ReadingStatus.Dropped)]
    [InlineData("something_else", ReadingStatus.Unknown)]
    public void MyAnimeListStatusMapper_FromListStatus_MapsEveryValue(string raw, ReadingStatus expected)
    {
        Assert.Equal(expected, MyAnimeListStatusMapper.FromListStatus(raw));
    }

    [Fact]
    public async Task CompleteConnectAsync_SuccessfulExchange_StoresTokens()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new MyAnimeListTrackerAdapter(new HttpClient(new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, """{ "access_token": "at-1", "refresh_token": "rt-1", "token_type": "Bearer", "expires_in": 3600 }"""))), clientId: "client-abc");

        bool result = await adapter.CompleteConnectAsync(context, "client-abc", "verifier-value", "pasted-code", CancellationToken.None);

        Assert.True(result);
        Assert.Equal("at-1", CredentialStore.Get(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken));
        Assert.Equal("rt-1", CredentialStore.Get(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthRefreshToken));
    }

    [Fact]
    public void GenerateCodeVerifier_ProducesRfc7636CompliantLength()
    {
        string verifier = MyAnimeListTrackerAdapter.GenerateCodeVerifier();

        Assert.InRange(verifier.Length, 43, 128);
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
