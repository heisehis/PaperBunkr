using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Tracking;

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
public sealed class AniListMetadataProvider : IMetadataProvider, ITrackerSearchProvider, IRelationsProvider
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
            title { romaji english native }
            siteUrl
            description(asHtml: false)
            status
            chapters
            volumes
            genres
            coverImage { large }
            staff { edges { role node { name { full } } } }
            startDate { year }
            format
            tags { name rank isMediaSpoiler }
            relations { edges { relationType node { id title { romaji english } siteUrl } } }
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

    /// <summary>Satisfies <see cref="ITrackerSearchProvider"/> - the same <see cref="SearchAsync"/> call below serves both metadata search and tracker-linking search, no separate implementation needed.</summary>
    TrackingService ITrackerSearchProvider.Service => TrackingService.AniList;

    /// <summary>
    /// Throws <see cref="MetadataProviderUnavailableException"/> when the call itself failed (rate
    /// limit, outage, network error, malformed response) - <see cref="SendAsync"/> returns null for
    /// all of those, and conflating that with "AniList genuinely found zero matches" made a real
    /// outage look identical to "no such series" in the UI. A successful call whose result list is
    /// actually empty still returns an empty list, not an exception.
    /// </summary>
    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var response = await SendAsync(SearchQuery, new { search = query, perPage = 10 }, cancellationToken)
            .ConfigureAwait(false);

        if (response is null)
        {
            throw new MetadataProviderUnavailableException();
        }

        var mediaList = response.Data?.Page?.Media ?? new List<AniListMediaDto>();
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

    /// <summary>Reuses <see cref="GetByIdQuery"/> (relations are already part of it, per §5's own
    /// fetch-together decision) rather than a dedicated query - one more request than strictly
    /// needed when called right after <see cref="GetAsync"/>, but this is only ever triggered by an
    /// explicit, infrequent "open the Related tab" action, not the default Apply path.</summary>
    public async Task<IReadOnlyList<ProviderRelation>> GetRelationsAsync(string externalId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(externalId, out int id))
        {
            return Array.Empty<ProviderRelation>();
        }

        var response = await SendAsync(GetByIdQuery, new { id }, cancellationToken).ConfigureAwait(false);
        var edges = response?.Data?.Media?.Relations?.Edges;
        if (edges is null)
        {
            return Array.Empty<ProviderRelation>();
        }

        return edges
            .Where(e => e.Node is not null)
            .Select(e => new ProviderRelation(
                TargetExternalId: e.Node!.Id.ToString(CultureInfo.InvariantCulture),
                TargetTitle: e.Node.Title?.English ?? e.Node.Title?.Romaji ?? "Untitled",
                TargetUrl: e.Node.SiteUrl,
                Type: ProviderRelationTypeMapper.MapAniList(e.RelationType)))
            .ToList();
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

    /// <summary>Only requested by <see cref="AniListMetadataProvider.GetByIdQuery"/>, not the search query - same rationale as <see cref="AniListTitleDto.Native"/> above.</summary>
    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }

    [JsonPropertyName("coverImage")]
    public AniListCoverImageDto? CoverImage { get; set; }

    [JsonPropertyName("staff")]
    public AniListStaffConnectionDto? Staff { get; set; }

    [JsonPropertyName("startDate")]
    public AniListFuzzyDateDto? StartDate { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("tags")]
    public List<AniListTagDto>? Tags { get; set; }

    [JsonPropertyName("relations")]
    public AniListRelationConnectionDto? Relations { get; set; }
}

internal sealed class AniListCoverImageDto
{
    [JsonPropertyName("large")]
    public string? Large { get; set; }
}

internal sealed class AniListStaffConnectionDto
{
    [JsonPropertyName("edges")]
    public List<AniListStaffEdgeDto>? Edges { get; set; }
}

/// <summary><see cref="Role"/> is freeform community-edited text ("Story &amp; Art", "Story",
/// "Art", "Original Creator", ...), not an enum - every staff member's name is extracted
/// regardless of the exact role string, per the design spec's explicit "no reliable story/art
/// split" decision.</summary>
internal sealed class AniListStaffEdgeDto
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("node")]
    public AniListStaffNodeDto? Node { get; set; }
}

internal sealed class AniListStaffNodeDto
{
    [JsonPropertyName("name")]
    public AniListStaffNameDto? Name { get; set; }
}

internal sealed class AniListStaffNameDto
{
    [JsonPropertyName("full")]
    public string? Full { get; set; }
}

internal sealed class AniListFuzzyDateDto
{
    [JsonPropertyName("year")]
    public int? Year { get; set; }
}

internal sealed class AniListTagDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("rank")]
    public int? Rank { get; set; }

    [JsonPropertyName("isMediaSpoiler")]
    public bool IsMediaSpoiler { get; set; }
}

internal sealed class AniListRelationConnectionDto
{
    [JsonPropertyName("edges")]
    public List<AniListRelationEdgeDto>? Edges { get; set; }
}

internal sealed class AniListRelationEdgeDto
{
    [JsonPropertyName("relationType")]
    public string? RelationType { get; set; }

    [JsonPropertyName("node")]
    public AniListRelationNodeDto? Node { get; set; }
}

internal sealed class AniListRelationNodeDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public AniListTitleDto? Title { get; set; }

    [JsonPropertyName("siteUrl")]
    public string? SiteUrl { get; set; }
}

internal sealed class AniListTitleDto
{
    [JsonPropertyName("romaji")]
    public string? Romaji { get; set; }

    [JsonPropertyName("english")]
    public string? English { get; set; }

    /// <summary>Only requested by <see cref="AniListMetadataProvider.GetByIdQuery"/>, not the search query - search results only need a display title, and requesting an extra field on every search result row is wasted payload for a value nothing there reads.</summary>
    [JsonPropertyName("native")]
    public string? Native { get; set; }
}

/// <summary>Shared <see cref="HttpClient"/> for <see cref="AniListMetadataProvider"/> callers - .NET
/// guidance is against constructing a new <see cref="HttpClient"/> per call (socket exhaustion under
/// load), and this app has no DI container/<c>IHttpClientFactory</c> registration to hand one out
/// from, so a single static instance is the simplest correct option at this app's scale (one user,
/// occasional manual searches - not the high-throughput scenario <c>IHttpClientFactory</c> exists for).</summary>
public static class AniListHttpClient
{
    public static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(15) };
}
