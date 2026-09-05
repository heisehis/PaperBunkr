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

public class KitsuTrackerAdapterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public KitsuTrackerAdapterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_kitsu_tracker_test_{Guid.NewGuid():N}.db");
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
    public async Task CompleteConnectAsync_SuccessfulLogin_StoresAccessAndRefreshTokens()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        const string json = """
            { "access_token": "abc123", "token_type": "Bearer", "created_at": 1000, "expires_in": 3600, "refresh_token": "refresh456" }
            """;
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.EndsWith("/api/oauth/token", req.RequestUri!.ToString());
            return JsonResponse(HttpStatusCode.OK, json);
        })), accessToken: null);

        bool connected = await adapter.CompleteConnectAsync(context, "user@example.com", "pass", CancellationToken.None);

        Assert.True(connected);
        Assert.Equal("abc123", CredentialStore.Get(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken));
        Assert.Equal("refresh456", CredentialStore.Get(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthRefreshToken));
    }

    [Fact]
    public async Task CompleteConnectAsync_BadCredentials_ReturnsFalse_DoesNotStoreToken()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized))), accessToken: null);

        bool connected = await adapter.CompleteConnectAsync(context, "user@example.com", "wrong", CancellationToken.None);

        Assert.False(connected);
        Assert.Null(CredentialStore.Get(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken));
    }

    [Fact]
    public async Task SearchAsync_NoAccessToken_ReturnsEmpty_WithoutCallingKitsu()
    {
        bool called = false;
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((_, _) =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        })), accessToken: null);

        var results = await adapter.SearchAsync("one piece", CancellationToken.None);

        Assert.Empty(results);
        Assert.False(called);
    }

    [Fact]
    public async Task SearchAsync_ParsesNodes_UsesBearerAuth()
    {
        const string json = """
            { "data": { "searchMangaByTitle": { "nodes": [
                { "id": "42", "titles": { "preferred": "One Piece" }, "slug": "one-piece" }
            ] } } }
            """;
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal("token-abc", req.Headers.Authorization?.Parameter);
            return JsonResponse(HttpStatusCode.OK, json);
        })), accessToken: "token-abc");

        var results = await adapter.SearchAsync("one piece", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("42", results[0].ExternalId);
        Assert.Equal("One Piece", results[0].Title);
        Assert.Equal("https://kitsu.app/manga/one-piece", results[0].Url);
    }

    [Fact]
    public async Task SearchAsync_TransportLevelError_ThrowsProviderUnavailable()
    {
        const string json = """{ "data": null, "errors": [ { "message": "boom" } ] }""";
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, json))), accessToken: "token-abc");

        await Assert.ThrowsAsync<Paperbunkr.Data.Metadata.MetadataProviderUnavailableException>(() => adapter.SearchAsync("anything", CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_NetworkFailure_ThrowsProviderUnavailable()
    {
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new HttpRequestException("connection refused"))), accessToken: "token-abc");

        await Assert.ThrowsAsync<Paperbunkr.Data.Metadata.MetadataProviderUnavailableException>(() => adapter.SearchAsync("anything", CancellationToken.None));
    }

    [Fact]
    public async Task PushEntryAsync_NoStoredToken_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))), accessToken: null);

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "42" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task PushEntryAsync_NoExistingLibraryEntry_CreatesOne()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken, "token-abc");

        var calls = new List<string>();
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            string body = req.Content!.ReadAsStringAsync().Result;
            calls.Add(body);

            if (body.Contains("findMangaById"))
            {
                return JsonResponse(HttpStatusCode.OK, """{ "data": { "findMangaById": { "myLibraryEntry": null } } }""");
            }

            Assert.Contains("AddManga", body);
            return JsonResponse(HttpStatusCode.OK, """{ "data": { "libraryEntry": { "create": { "errors": null, "libraryEntry": { "id": "999" } } } } }""");
        })), accessToken: null);

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "42" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public async Task PushEntryAsync_ExistingLibraryEntry_Updates()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken, "token-abc");

        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            string body = req.Content!.ReadAsStringAsync().Result;

            if (body.Contains("findMangaById"))
            {
                return JsonResponse(HttpStatusCode.OK, """{ "data": { "findMangaById": { "myLibraryEntry": { "id": "777" } } } }""");
            }

            Assert.Contains("UpdateManga", body);
            Assert.Contains("777", body);
            return JsonResponse(HttpStatusCode.OK, """{ "data": { "libraryEntry": { "update": { "errors": null, "libraryEntry": { "id": "777" } } } } }""");
        })), accessToken: null);

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "42" }, new TrackerPushPayload(ReadingStatus.Completed, 100), CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task PushEntryAsync_MutationLevelErrors_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken, "token-abc");

        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            string body = req.Content!.ReadAsStringAsync().Result;

            if (body.Contains("findMangaById"))
            {
                return JsonResponse(HttpStatusCode.OK, """{ "data": { "findMangaById": { "myLibraryEntry": null } } }""");
            }

            return JsonResponse(HttpStatusCode.OK, """{ "data": { "libraryEntry": { "create": { "errors": [ { "message": "Validation failed" } ], "libraryEntry": null } } } }""");
        })), accessToken: null);

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "42" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task GetEntryAsync_NoStoredToken_ReturnsNull_WithoutSendingRequest()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))), accessToken: null);

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "42" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEntryAsync_ParsesStatusAndProgress()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken, "token-abc");

        const string json = """{ "data": { "findMangaById": { "myLibraryEntry": { "id": "777", "status": "CURRENT", "progress": 21 } } } }""";
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, json))), accessToken: null);

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "42" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReadingStatus.Reading, result!.Status);
        Assert.Equal(21, result.ChapterProgress);
    }

    [Fact]
    public async Task GetEntryAsync_NoLibraryEntry_ReturnsNull()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken, "token-abc");

        const string json = """{ "data": { "findMangaById": { "myLibraryEntry": null } } }""";
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, json))), accessToken: null);

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "42" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("CURRENT", ReadingStatus.Reading)]
    [InlineData("PLANNED", ReadingStatus.Planned)]
    [InlineData("COMPLETED", ReadingStatus.Completed)]
    [InlineData("ON_HOLD", ReadingStatus.Paused)]
    [InlineData("DROPPED", ReadingStatus.Dropped)]
    [InlineData("SOMETHING_ELSE", ReadingStatus.Unknown)]
    public void KitsuStatusMapper_FromStatus_MapsEveryValue(string raw, ReadingStatus expected)
    {
        Assert.Equal(expected, KitsuStatusMapper.FromStatus(raw));
    }

    [Theory]
    [InlineData(ReadingStatus.Reading, "CURRENT")]
    [InlineData(ReadingStatus.Planned, "PLANNED")]
    [InlineData(ReadingStatus.Completed, "COMPLETED")]
    [InlineData(ReadingStatus.Paused, "ON_HOLD")]
    [InlineData(ReadingStatus.Dropped, "DROPPED")]
    [InlineData(ReadingStatus.ReReading, "CURRENT")]
    public void KitsuStatusMapper_MapsEveryReadingStatus(ReadingStatus status, string expected)
    {
        Assert.Equal(expected, KitsuStatusMapper.ToStatus(status));
    }

    [Fact]
    public void ProviderKey_IsKitsu()
    {
        var adapter = new KitsuTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK))), accessToken: null);

        Assert.Equal(TrackingService.Kitsu, adapter.Service);
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
