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

public class BangumiTrackerAdapterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public BangumiTrackerAdapterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_bangumi_tracker_test_{Guid.NewGuid():N}.db");
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
    public async Task SearchAsync_ParsesSubjects_PreferringChineseNameOverOriginal()
    {
        const string json = """
            { "data": [ { "id": 12, "name": "One Piece", "name_cn": "海贼王" } ] }
            """;
        var adapter = new BangumiTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.NotNull(req.Headers.UserAgent.ToString());
            return JsonResponse(HttpStatusCode.OK, json);
        })));

        var results = await adapter.SearchAsync("one piece", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("12", results[0].ExternalId);
        Assert.Equal("海贼王", results[0].Title);
        Assert.Equal("https://bgm.tv/subject/12", results[0].Url);
    }

    [Fact]
    public async Task SearchAsync_NonSuccessStatus_ThrowsProviderUnavailable()
    {
        var adapter = new BangumiTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        await Assert.ThrowsAsync<Paperbunkr.Data.Metadata.MetadataProviderUnavailableException>(() => adapter.SearchAsync("one piece", CancellationToken.None));
    }

    [Fact]
    public async Task PushEntryAsync_NoStoredToken_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new BangumiTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "12" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task PushEntryAsync_SuccessfulFirstAttempt_ReturnsTrue_NoRetry()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        BangumiTrackerAdapter.CompleteConnect(context, "pat-123");

        int callCount = 0;
        var adapter = new BangumiTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            callCount++;
            Assert.Equal(HttpMethod.Post, req.Method);
            return JsonResponse(HttpStatusCode.OK, "{}");
        })));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "12" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task PushEntryAsync_FirstAttemptFails_RetriesOnceViaPatch_ThenSucceeds()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        BangumiTrackerAdapter.CompleteConnect(context, "pat-123");

        int callCount = 0;
        var adapter = new BangumiTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            callCount++;
            if (callCount == 1)
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                return JsonResponse(HttpStatusCode.InternalServerError, "{}");
            }

            Assert.Equal(HttpMethod.Patch, req.Method);
            return JsonResponse(HttpStatusCode.OK, "{}");
        })));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "12" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task PushEntryAsync_BothAttemptsFail_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        BangumiTrackerAdapter.CompleteConnect(context, "pat-123");

        var adapter = new BangumiTrackerAdapter(new HttpClient(new StubHandler((_, _) => JsonResponse(HttpStatusCode.InternalServerError, "{}"))));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "12" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task GetEntryAsync_NoStoredToken_ReturnsNull_WithoutSendingRequest()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new BangumiTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "12" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEntryAsync_ParsesTypeAndEpStatus()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        BangumiTrackerAdapter.CompleteConnect(context, "pat-123");

        var adapter = new BangumiTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Get, req.Method);
            return JsonResponse(HttpStatusCode.OK, """{ "type": 3, "ep_status": 8 }""");
        })));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "12" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReadingStatus.Reading, result!.Status);
        Assert.Equal(8, result.ChapterProgress);
    }

    [Fact]
    public async Task GetEntryAsync_NotFound_ReturnsNull()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        BangumiTrackerAdapter.CompleteConnect(context, "pat-123");

        var adapter = new BangumiTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound))));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "12" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(1, ReadingStatus.Planned)]
    [InlineData(2, ReadingStatus.Completed)]
    [InlineData(3, ReadingStatus.Reading)]
    [InlineData(4, ReadingStatus.Paused)]
    [InlineData(5, ReadingStatus.Dropped)]
    [InlineData(99, ReadingStatus.Unknown)]
    public void BangumiCollectionTypeMapper_FromCollectionType_MapsEveryValue(int type, ReadingStatus expected)
    {
        Assert.Equal(expected, BangumiCollectionTypeMapper.FromCollectionType(type));
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
