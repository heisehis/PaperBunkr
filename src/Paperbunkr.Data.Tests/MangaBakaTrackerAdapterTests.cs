using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Tracking;
using Paperbunkr.Data.Tracking.Adapters;
using Xunit;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="MangaBakaTrackerAdapter"/> (docs/superpowers/specs/2026-08-23-mangabaka-
/// tracker-adapter-design.md) against a fake <see cref="HttpMessageHandler"/> - no real network
/// calls, same precedent as <see cref="BangumiTrackerAdapterTests"/>.
/// </summary>
public class MangaBakaTrackerAdapterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public MangaBakaTrackerAdapterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mangabaka_tracker_test_{Guid.NewGuid():N}.db");
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
    public async Task PushEntryAsync_NoStoredToken_ReturnsFalse_WithoutSendingRequest()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new MangaBakaTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "708" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task PushEntryAsync_SendsPutWithApiKeyHeaderAndStateAndProgress()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        MangaBakaTrackerAdapter.CompleteConnect(context, "mb-test-token");

        string? capturedBody = null;
        var adapter = new MangaBakaTrackerAdapter(new HttpClient(new StubHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Put, req.Method);
            Assert.Equal("https://api.mangabaka.org/v1/my/library/708", req.RequestUri!.ToString());
            Assert.Equal("mb-test-token", req.Headers.GetValues("x-api-key").Single());
            capturedBody = req.Content!.ReadAsStringAsync().Result;
            return JsonResponse(HttpStatusCode.OK, "{}");
        })));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "708" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.True(result);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.Equal("reading", doc.RootElement.GetProperty("state").GetString());
        Assert.Equal(5, doc.RootElement.GetProperty("progress_chapter").GetInt32());
    }

    [Fact]
    public async Task PushEntryAsync_NonSuccessStatus_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        MangaBakaTrackerAdapter.CompleteConnect(context, "mb-test-token");
        var adapter = new MangaBakaTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "708" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task PushEntryAsync_NetworkFailure_ReturnsFalse_WithoutThrowing()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        MangaBakaTrackerAdapter.CompleteConnect(context, "mb-test-token");
        var adapter = new MangaBakaTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new HttpRequestException("connection refused"))));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "708" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public void Service_IsMangaBaka()
    {
        var adapter = new MangaBakaTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK))));

        Assert.Equal(TrackingService.MangaBaka, adapter.Service);
    }

    // --- MangaBakaLibraryStateMapper: lossless 1:1 mapping, every ReadingStatus value covered ---

    [Theory]
    [InlineData(ReadingStatus.Unknown, "considering")]
    [InlineData(ReadingStatus.Planned, "plan_to_read")]
    [InlineData(ReadingStatus.Reading, "reading")]
    [InlineData(ReadingStatus.Completed, "completed")]
    [InlineData(ReadingStatus.Paused, "paused")]
    [InlineData(ReadingStatus.Dropped, "dropped")]
    [InlineData(ReadingStatus.ReReading, "rereading")]
    public void ToState_MapsEveryReadingStatusLosslessly(ReadingStatus status, string expected)
    {
        Assert.Equal(expected, MangaBakaLibraryStateMapper.ToState(status));
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
