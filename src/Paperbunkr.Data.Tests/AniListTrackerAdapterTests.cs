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
/// Exercises <see cref="AniListTrackerAdapter"/>'s push path against a fake
/// <see cref="HttpMessageHandler"/> (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-
/// design.md) - same "no live network calls" precedent as <c>AniListMetadataProviderTests</c>.
/// </summary>
public class AniListTrackerAdapterTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public AniListTrackerAdapterTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_anilist_tracker_test_{Guid.NewGuid():N}.db");
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
    public async Task PushEntryAsync_NoStoredAccessToken_ReturnsFalse_WithoutSendingRequest()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new AniListTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "30013" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task PushEntryAsync_SuccessfulMutation_ReturnsTrue()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken, "token-123");

        var adapter = new AniListTrackerAdapter(new HttpClient(new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, """{ "data": { "SaveMediaListEntry": { "id": 1 } } }"""))));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "30013" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task PushEntryAsync_GraphQlErrorResponse_ReturnsFalse()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken, "token-123");

        var adapter = new AniListTrackerAdapter(new HttpClient(new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, """{ "data": null, "errors": [ { "message": "Invalid token" } ] }"""))));

        bool result = await adapter.PushEntryAsync(context, new TrackingLink { ExternalId = "30013" }, new TrackerPushPayload(ReadingStatus.Reading, 5), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task GetEntryAsync_NoStoredAccessToken_ReturnsNull_WithoutSendingRequest()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var adapter = new AniListTrackerAdapter(new HttpClient(new StubHandler((_, _) => throw new InvalidOperationException("should not send"))));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "30013" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEntryAsync_ParsesMediaListEntry()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken, "token-123");

        var adapter = new AniListTrackerAdapter(new HttpClient(new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, """{ "data": { "Media": { "mediaListEntry": { "status": "CURRENT", "progress": 12 } } } } """))));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "30013" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ReadingStatus.Reading, result!.Status);
        Assert.Equal(12, result.ChapterProgress);
    }

    [Fact]
    public async Task GetEntryAsync_NoMediaListEntry_ReturnsNull()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken, "token-123");

        var adapter = new AniListTrackerAdapter(new HttpClient(new StubHandler((_, _) =>
            JsonResponse(HttpStatusCode.OK, """{ "data": { "Media": { "mediaListEntry": null } } }"""))));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "30013" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEntryAsync_NonSuccessStatus_ReturnsNull()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken, "token-123");

        var adapter = new AniListTrackerAdapter(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        var result = await adapter.GetEntryAsync(context, new TrackingLink { ExternalId = "30013" }, CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("PLANNING", ReadingStatus.Planned)]
    [InlineData("CURRENT", ReadingStatus.Reading)]
    [InlineData("COMPLETED", ReadingStatus.Completed)]
    [InlineData("PAUSED", ReadingStatus.Paused)]
    [InlineData("DROPPED", ReadingStatus.Dropped)]
    [InlineData("REPEATING", ReadingStatus.ReReading)]
    [InlineData("SOMETHING_ELSE", ReadingStatus.Unknown)]
    public void AniListStatusMapper_FromMediaListStatus_MapsEveryValue(string raw, ReadingStatus expected)
    {
        Assert.Equal(expected, AniListStatusMapper.FromMediaListStatus(raw));
    }

    [Fact]
    public void BuildAuthorizationUrl_IncludesClientIdAndImplicitGrantResponseType()
    {
        string url = AniListTrackerAdapter.BuildAuthorizationUrl("42");

        Assert.Contains("client_id=42", url);
        Assert.Contains("response_type=token", url);
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
