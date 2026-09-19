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

/// <summary>
/// Exercises <see cref="MangaDexTrackerAdapter"/> against a fake <see cref="HttpMessageHandler"/> -
/// no real network calls, same "no live network calls" precedent as
/// <see cref="KitsuTrackerAdapterTests"/>. The <c>CompleteConnectAsync</c>/status endpoint shapes
/// were confirmed live 2026-09-18 (real <c>401</c>s on <c>GET /manga/status</c> and
/// <c>/user/follows/manga</c>, real Keycloak OIDC config at <c>auth.mangadex.org</c>) - the response
/// bodies below follow the standard OAuth2/OIDC token shape those endpoints use, not a guess.
/// </summary>
public class MangaDexTrackerAdapterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public MangaDexTrackerAdapterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mangadex_tracker_test_{Guid.NewGuid():N}.db");
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
        const string json = """{ "access_token": "abc123", "refresh_token": "refresh456", "token_type": "Bearer", "expires_in": 900 }""";
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal("https://auth.mangadex.org/realms/mangadex/protocol/openid-connect/token", req.RequestUri!.ToString());
            return JsonResponse(HttpStatusCode.OK, json);
        })));

        bool connected = await adapter.CompleteConnectAsync(context, "client-id", "client-secret", "user@example.com", "pass", CancellationToken.None);

        Assert.True(connected);
        Assert.Equal("abc123", CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken));
        Assert.Equal("refresh456", CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthRefreshToken));
    }

    [Fact]
    public async Task CompleteConnectAsync_InvalidClient_ReturnsFalse_DoesNotStoreToken()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        const string json = """{ "error": "invalid_client", "error_description": "Invalid client or Invalid client credentials" }""";
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((_, _) => JsonResponse(HttpStatusCode.Unauthorized, json))));

        bool connected = await adapter.CompleteConnectAsync(context, "bad-id", "bad-secret", "user@example.com", "pass", CancellationToken.None);

        Assert.False(connected);
        Assert.Null(CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken));
    }

    [Fact]
    public async Task PushEntryAsync_NoAccessToken_ReturnsFalse_WithoutCallingMangaDex()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        bool called = false;
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((_, _) =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        bool pushed = await adapter.PushEntryAsync(context, link, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(pushed);
        Assert.False(called);
    }

    [Fact]
    public async Task PushEntryAsync_PostsStatusOnly_NoChapterProgressField()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, "token-abc");
        string? capturedBody = null;
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.EndsWith("/manga/manga-id/status", req.RequestUri!.ToString());
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("token-abc", req.Headers.Authorization.Parameter);
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        bool pushed = await adapter.PushEntryAsync(context, link, new TrackerPushPayload(ReadingStatus.ReReading, 5), CancellationToken.None);

        Assert.True(pushed);
        Assert.Contains("\"status\":\"re_reading\"", capturedBody);
        Assert.DoesNotContain("chapter", capturedBody);
    }

    [Fact]
    public async Task PushEntryAsync_UpdateScoreFalse_NeverCallsRatingEndpoint()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, "token-abc");
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.DoesNotContain("/rating/", req.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        // Score has a value but UpdateScore defaults false - the shared "Sync with Trackers" button's
        // own call shape - so no DELETE must be issued to wipe an independently-set remote rating.
        bool pushed = await adapter.PushEntryAsync(context, link, new TrackerPushPayload(ReadingStatus.Reading, 5, Score: 4.5m), CancellationToken.None);

        Assert.True(pushed);
    }

    [Fact]
    public async Task PushEntryAsync_UpdateScoreTrue_PostsConvertedRatingToSeparateEndpoint()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, "token-abc");
        string? ratingBody = null;
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            if (req.RequestUri!.ToString().Contains("/rating/"))
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                ratingBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        var payload = new TrackerPushPayload(ReadingStatus.Reading, 5, Score: 4.5m, UpdateScore: true);
        bool pushed = await adapter.PushEntryAsync(context, link, payload, CancellationToken.None);

        Assert.True(pushed);
        Assert.NotNull(ratingBody);
        Assert.Contains("\"rating\":9", ratingBody); // 4.5 * 2
    }

    [Fact]
    public async Task PushEntryAsync_UpdateScoreTrue_ZeroScore_DeletesRating()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, "token-abc");
        bool sawDelete = false;
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            if (req.RequestUri!.ToString().Contains("/rating/"))
            {
                Assert.Equal(HttpMethod.Delete, req.Method);
                sawDelete = true;
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        var payload = new TrackerPushPayload(ReadingStatus.Reading, 5, Score: null, UpdateScore: true);
        bool pushed = await adapter.PushEntryAsync(context, link, payload, CancellationToken.None);

        Assert.True(pushed);
        Assert.True(sawDelete);
    }

    // --- Expired-token refresh-and-retry (a live "reports success, mangadex.org never updates"
    // report traced to no refresh logic existing at all - Keycloak access tokens are short-lived
    // and a sync rarely runs within that window of connecting) ---

    [Fact]
    public async Task PushEntryDetailedAsync_ExpiredToken_RefreshesAndRetries_Succeeds()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, "expired-token");
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthRefreshToken, "refresh-token");
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthClientId, "client-id");
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthClientSecret, "client-secret");

        var requestsSeen = new List<(string Url, string? Bearer)>();
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            if (req.RequestUri!.ToString().Contains("auth.mangadex.org"))
            {
                return JsonResponse(HttpStatusCode.OK, """{ "access_token": "fresh-token", "refresh_token": "fresh-refresh" }""");
            }

            requestsSeen.Add((req.RequestUri!.ToString(), req.Headers.Authorization?.Parameter));
            return req.Headers.Authorization?.Parameter == "expired-token"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        var (success, error) = await adapter.PushEntryDetailedAsync(context, link, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(2, requestsSeen.Count); // one with the stale token (401), one retried with the refreshed one
        Assert.Equal("expired-token", requestsSeen[0].Bearer);
        Assert.Equal("fresh-token", requestsSeen[1].Bearer);
        Assert.Equal("fresh-token", CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken));
        Assert.Equal("fresh-refresh", CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthRefreshToken));
    }

    [Fact]
    public async Task PushEntryDetailedAsync_ExpiredToken_RefreshAlsoFails_ReturnsClearError()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, "expired-token");
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthRefreshToken, "revoked-refresh-token");
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthClientId, "client-id");
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthClientSecret, "client-secret");

        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
            req.RequestUri!.ToString().Contains("auth.mangadex.org")
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_grant"}""") }
                : new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        var (success, error) = await adapter.PushEntryDetailedAsync(context, link, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(success);
        Assert.Contains("reconnect", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PushEntryDetailedAsync_ExpiredToken_NoRefreshTokenStored_ReturnsClearError_WithoutCallingAuth()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, "expired-token");
        // No refresh token / client id / secret stored - simulates a connection made before this fix.
        bool authCalled = false;
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            if (req.RequestUri!.ToString().Contains("auth.mangadex.org"))
            {
                authCalled = true;
            }

            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        })));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        var (success, error) = await adapter.PushEntryDetailedAsync(context, link, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(success);
        Assert.Contains("reconnect", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(authCalled); // no refresh token to try - fails fast rather than sending a doomed request
    }

    [Fact]
    public async Task GetEntryAsync_ExpiredToken_RefreshesAndRetries_Succeeds()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, "expired-token");
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthRefreshToken, "refresh-token");
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthClientId, "client-id");
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthClientSecret, "client-secret");

        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            if (req.RequestUri!.ToString().Contains("auth.mangadex.org"))
            {
                return JsonResponse(HttpStatusCode.OK, """{ "access_token": "fresh-token", "refresh_token": "fresh-refresh" }""");
            }

            return req.Headers.Authorization?.Parameter == "expired-token"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : JsonResponse(HttpStatusCode.OK, """{ "status": "reading" }""");
        })));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        var entry = await adapter.GetEntryAsync(context, link, CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal(ReadingStatus.Reading, entry!.Status);
        Assert.Equal("fresh-token", CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken));
    }

    [Fact]
    public async Task GetEntryAsync_ParsesStatus_ChapterProgressAlwaysNull()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, "token-abc");
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, """{ "result": "ok", "status": "reading" }"""))));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        var entry = await adapter.GetEntryAsync(context, link, CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal(ReadingStatus.Reading, entry!.Status);
        Assert.Null(entry.ChapterProgress);
    }

    [Fact]
    public async Task GetEntryAsync_NoAccessToken_ReturnsNull_WithoutCallingMangaDex()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        bool called = false;
        var adapter = new MangaDexTrackerAdapter(new HttpClient(new StubHandler((_, _) =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        })));
        var link = new TrackingLink { Service = TrackingService.MangaDex, ExternalId = "manga-id" };

        var entry = await adapter.GetEntryAsync(context, link, CancellationToken.None);

        Assert.Null(entry);
        Assert.False(called);
    }

    [Theory]
    [InlineData(ReadingStatus.Reading, "reading")]
    [InlineData(ReadingStatus.Planned, "plan_to_read")]
    [InlineData(ReadingStatus.Completed, "completed")]
    [InlineData(ReadingStatus.Paused, "on_hold")]
    [InlineData(ReadingStatus.Dropped, "dropped")]
    [InlineData(ReadingStatus.ReReading, "re_reading")]
    public void StatusMapper_RoundTrips_EveryReadingStatus(ReadingStatus status, string expected)
    {
        Assert.Equal(expected, MangaDexStatusMapper.ToStatus(status));
        Assert.Equal(status, MangaDexStatusMapper.FromStatus(expected));
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
