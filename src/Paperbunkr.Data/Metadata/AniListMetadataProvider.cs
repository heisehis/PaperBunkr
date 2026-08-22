using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// The first real <see cref="IMetadataProvider"/> implementation (docs/superpowers/specs/2026-08-18-
/// metadata-model-phase5b-anilist-adapter-design.md) - live GraphQL calls to AniList's public API.
/// Backend-only: nothing in <c>Paperbunkr.App</c> calls this yet (search/match UI and every other
/// provider are deferred to the tracker-service-sync backlog item, per that spec's own scope note).
/// AniList's own terms (github.com/AniList/docs, verified live since docs.anilist.co 403s automated
/// fetches) explicitly prohibit "using the API as a backup/data-storage service" and "hoarding or
/// mass collection of data" - this adapter only ever fetches one media at a time, on an explicit
/// caller-driven call, never a bulk crawl.
/// </summary>
public sealed class AniListMetadataProvider : IMetadataProvider
{
    private const string Endpoint = "https://graphql.anilist.co";

    // AniList's API is currently degraded to 30 requests/minute (nominally 90/min) - erring
    // conservative against the lower, currently-active limit rather than the nominal one.
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(60.0 / 30);

    private const string SearchQuery = """
        query ($search: String, $perPage: Int) {
          Page(page: 1, perPage: $perPage) {
            media(search: $search, type: MANGA) {
              id
              title { romaji english }
              siteUrl
            }
          }
        }
        """;

    private const string GetByIdQuery = """
        query ($id: Int) {
          Media(id: $id) {
            id
            title { romaji english }
            siteUrl
            description(asHtml: false)
            status
            chapters
            volumes
          }
        }
        """;

    private readonly HttpClient _httpClient;
    private readonly object _rateLimitLock = new();
    private DateTime _nextAllowedRequestUtc = DateTime.MinValue;

    public AniListMetadataProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.AniList;

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var response = await SendAsync(SearchQuery, new { search = query, perPage = 10 }, cancellationToken)
            .ConfigureAwait(false);

        var mediaList = response?.Data?.Page?.Media;
        if (mediaList is null)
        {
            return Array.Empty<MetadataSearchResult>();
        }

        return mediaList.Select(AniListNormalizer.ToSearchResult).ToList();
    }

    public async Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(externalId, out int id))
        {
            return null;
        }

        var response = await SendAsync(GetByIdQuery, new { id }, cancellationToken).ConfigureAwait(false);
        var media = response?.Data?.Media;
        return media is null ? null : AniListNormalizer.ToMediaMetadata(media);
    }

    private async Task<AniListGraphQlResponse?> SendAsync(string query, object variables, CancellationToken cancellationToken)
    {
        await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .PostAsJsonAsync(Endpoint, new { query, variables }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timeout, not caller cancellation - treat like any other failed fetch.
            return null;
        }

        using (response)
        {
            ObserveRateLimitHeaders(response);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                ObserveRetryAfter(response);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Covers AniList's documented "temporarily disabled due to severe stability issues"
                // 403 shape, and anything else - not found/unavailable, not exceptional.
                return null;
            }

            AniListGraphQlResponse? parsed;
            try
            {
                parsed = await response.Content
                    .ReadFromJsonAsync<AniListGraphQlResponse>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return null;
            }

            if (parsed?.Errors is { Count: > 0 })
            {
                // AniList reports GraphQL-level errors with HTTP 200 - same "not found," not exceptional.
                return null;
            }

            return parsed;
        }
    }

    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        TimeSpan delay;
        lock (_rateLimitLock)
        {
            delay = _nextAllowedRequestUtc - DateTime.UtcNow;
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        lock (_rateLimitLock)
        {
            _nextAllowedRequestUtc = DateTime.UtcNow + MinRequestInterval;
        }
    }

    private void ObserveRateLimitHeaders(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remainingValues)
            && int.TryParse(remainingValues.FirstOrDefault(), out int remaining)
            && remaining <= 0)
        {
            // AniList reports we're out of budget for this window - hold off until the minimum
            // interval has definitely rolled the window over, even without a 429 telling us so.
            lock (_rateLimitLock)
            {
                DateTime candidate = DateTime.UtcNow + MinRequestInterval;
                if (candidate > _nextAllowedRequestUtc)
                {
                    _nextAllowedRequestUtc = candidate;
                }
            }
        }
    }

    private void ObserveRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues)
            && int.TryParse(retryAfterValues.FirstOrDefault(), out int retryAfterSeconds))
        {
            lock (_rateLimitLock)
            {
                DateTime candidate = DateTime.UtcNow + TimeSpan.FromSeconds(retryAfterSeconds);
                if (candidate > _nextAllowedRequestUtc)
                {
                    _nextAllowedRequestUtc = candidate;
                }
            }
        }
    }
}

internal sealed class AniListGraphQlResponse
{
    [JsonPropertyName("data")]
    public AniListData? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<AniListGraphQlError>? Errors { get; set; }
}

internal sealed class AniListGraphQlError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class AniListData
{
    [JsonPropertyName("Page")]
    public AniListPage? Page { get; set; }

    [JsonPropertyName("Media")]
    public AniListMediaDto? Media { get; set; }
}

internal sealed class AniListPage
{
    [JsonPropertyName("media")]
    public List<AniListMediaDto>? Media { get; set; }
}

internal sealed class AniListMediaDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public AniListTitleDto? Title { get; set; }

    [JsonPropertyName("siteUrl")]
    public string? SiteUrl { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("chapters")]
    public int? Chapters { get; set; }

    [JsonPropertyName("volumes")]
    public int? Volumes { get; set; }
}

internal sealed class AniListTitleDto
{
    [JsonPropertyName("romaji")]
    public string? Romaji { get; set; }

    [JsonPropertyName("english")]
    public string? English { get; set; }
}
