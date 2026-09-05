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

public class MangaUpdatesTrackerAdapterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public MangaUpdatesTrackerAdapterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mangaupdates_tracker_test_{Guid.NewGuid():N}.db");
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
    public async Task CompleteConnectAsync_SuccessfulLogin_StoresSessionToken()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        const string json = """
            { "context": { "session_token": "abc123", "uid": 42 } }
            """;
        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.EndsWith("/account/login", req.RequestUri!.ToString());
            return JsonResponse(HttpStatusCode.OK, json);
        })));

        bool connected = await adapter.CompleteConnectAsync(context, "user", "pass", CancellationToken.None);

        Assert.True(connected);
        Assert.Equal("abc123", CredentialStore.Get(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken));
    }

    [Fact]
    public async Task CompleteConnectAsync_BadCredentials_ReturnsFalse_DoesNotStoreToken()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        bool connected = await adapter.CompleteConnectAsync(context, "user", "wrong", CancellationToken.None);

        Assert.False(connected);
        Assert.Null(CredentialStore.Get(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken));
    }

    [Fact]
    public async Task SearchAsync_ParsesRecords()
    {
        const string json = """
            { "results": [ { "record": { "series_id": 15090, "title": "One Piece", "url": "https://www.mangaupdates.com/series/abc/one-piece" } } ] }
            """;
        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.EndsWith("/series/search", req.RequestUri!.ToString());
            return JsonResponse(HttpStatusCode.OK, json);
        })));

        var results = await adapter.SearchAsync("one piece", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("15090", results[0].ExternalId);
        Assert.Equal("One Piece", results[0].Title);
        Assert.Equal("https://www.mangaupdates.com/series/abc/one-piece", results[0].Url);
    }

    [Fact]
    public async Task SearchAsync_NonSuccessStatus_ThrowsProviderUnavailable()
    {
        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        await Assert.ThrowsAsync<Paperbunkr.Data.Metadata.MetadataProviderUnavailableException>(() => adapter.SearchAsync("one piece", CancellationToken.None));
    }

    [Fact]
    public async Task PushEntryAsync_NoStoredToken_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "15090" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task PushEntryAsync_NotYetOnAnyList_AddsThenUpdates()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken, "session-token");

        var calls = new List<(HttpMethod Method, string Url)>();
        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            calls.Add((req.Method, req.RequestUri!.ToString()));
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return JsonResponse(HttpStatusCode.OK, "{}");
        })));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "15090" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(3, calls.Count);
        Assert.Equal(HttpMethod.Get, calls[0].Method);
        Assert.EndsWith("/lists/series/15090", calls[0].Url);
        Assert.Equal(HttpMethod.Post, calls[1].Method);
        Assert.EndsWith("/lists/series", calls[1].Url);
        Assert.Equal(HttpMethod.Post, calls[2].Method);
        Assert.EndsWith("/lists/series/update", calls[2].Url);
    }

    [Fact]
    public async Task PushEntryAsync_AlreadyOnList_SkipsAdd_OnlyUpdates()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken, "session-token");

        var calls = new List<(HttpMethod Method, string Url)>();
        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            calls.Add((req.Method, req.RequestUri!.ToString()));
            if (req.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, "{}");
            }

            return JsonResponse(HttpStatusCode.OK, "{}");
        })));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "15090" }, new TrackerPushPayload(ReadingStatus.Completed, 100), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(2, calls.Count);
        Assert.Equal(HttpMethod.Get, calls[0].Method);
        Assert.Equal(HttpMethod.Post, calls[1].Method);
        Assert.EndsWith("/lists/series/update", calls[1].Url);
    }

    [Fact]
    public async Task PushEntryAsync_UpdateCallFails_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken, "session-token");

        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return JsonResponse(HttpStatusCode.OK, "{}");
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        })));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "15090" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task GetEntryAsync_NoStoredToken_ReturnsNull_WithoutSendingRequest()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "15090" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEntryAsync_ParsesListIdAndChapter()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken, "session-token");

        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            return JsonResponse(HttpStatusCode.OK, """{ "list_id": 0, "status": { "chapter": 33 } }""");
        })));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "15090" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReadingStatus.Reading, result!.Status);
        Assert.Equal(33, result.ChapterProgress);
    }

    [Fact]
    public async Task GetEntryAsync_NotOnAnyList_ReturnsNull()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken, "session-token");

        var adapter = new MangaUpdatesTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "15090" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0L, ReadingStatus.Reading)]
    [InlineData(1L, ReadingStatus.Planned)]
    [InlineData(2L, ReadingStatus.Completed)]
    [InlineData(3L, ReadingStatus.Dropped)]
    [InlineData(4L, ReadingStatus.Paused)]
    [InlineData(99L, ReadingStatus.Unknown)]
    public void MangaUpdatesListMapper_FromListId_MapsEveryValue(long listId, ReadingStatus expected)
    {
        Assert.Equal(expected, MangaUpdatesListMapper.FromListId(listId));
    }

    [Theory]
    [InlineData(ReadingStatus.Reading, 0L)]
    [InlineData(ReadingStatus.Planned, 1L)]
    [InlineData(ReadingStatus.Completed, 2L)]
    [InlineData(ReadingStatus.Dropped, 3L)]
    [InlineData(ReadingStatus.Paused, 4L)]
    [InlineData(ReadingStatus.ReReading, 0L)]
    public void MangaUpdatesListMapper_MapsEveryReadingStatus(ReadingStatus status, long expectedListId)
    {
        Assert.Equal(expectedListId, MangaUpdatesListMapper.ToListId(status));
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
