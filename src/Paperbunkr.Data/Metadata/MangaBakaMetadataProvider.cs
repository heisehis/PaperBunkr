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
/// Second real <see cref="IMetadataProvider"/> implementation, alongside <see cref="AniListMetadataProvider"/>.
/// Also implements <see cref="ITrackerSearchProvider"/>, same as <see cref="AniListMetadataProvider"/>
/// does - one search call serves both metadata search and tracker-linking search
/// (docs/superpowers/specs/2026-08-23-mangabaka-tracker-adapter-design.md). The actual reading-
/// progress push lives in the separate <c>Paperbunkr.Data.Tracking.Adapters.MangaBakaTrackerAdapter</c>
/// (PAT-authenticated, hits `PUT /v1/my/library/{series_id}`) - this class only ever calls the
/// public, unauthenticated `GET https://api.mangabaka.org/v2/series/search?q=`/`.../series/{id}`
/// endpoints, which is why it stays a plain <c>HttpClient</c> caller with no credential lookup of
/// its own.
/// </summary>
public sealed class MangaBakaMetadataProvider : IMetadataProvider, ITrackerSearchProvider
{
    private const string BaseUrl = "https://api.mangabaka.org/v2/";

    /// <summary>
    /// MangaBaka's own documented limit for `GET /series/search` (`api.mangabaka.org/data/api`):
    /// 30 requests/minute per IP, leaky-bucket, rate-limited only on cache misses (repeat identical
    /// requests are served from their CDN cache and don't count). `GET /series/{id}` falls under
    /// their more generous "Default" kind (180/min) but this class pays the stricter search rate
    /// uniformly for both - simpler than tracking two counters for an adapter this lightly used,
    /// and never a practical bottleneck for a manual per-search UI action.
    /// </summary>
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMinutes(1.0 / 30);

    private readonly HttpClient _httpClient;
    private readonly object _rateLimitLock = new();
    private DateTime _nextAllowedRequestUtc = DateTime.MinValue;

    public MangaBakaMetadataProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// One shared instance for every real call site (`DetailTabsViewModel`'s
    /// `GetTrackerSearchProvider`/`GetMetadataProviderFor`) to construct, instead of `new`-ing a
    /// fresh one per call. Real bug found live: this class's rate limiter is instance-scoped, so
    /// constructing a fresh instance per call (this app's usual "no DI container" pattern for
    /// stateless providers) reset it to <see cref="DateTime.MinValue"/> every time, defeating the
    /// whole point - two real user actions landing within MangaBaka's actual 30/min window (e.g.
    /// linking via Trackers, then searching again via External Metadata seconds later) could each
    /// fire immediately, and the second would eat a genuine 429 that this class's own error
    /// handling silently collapses into an empty result list indistinguishable from "no matches."
    /// Routing every call site through one shared instance instead (mirroring how
    /// <c>DetailTabsViewModel</c>'s own <c>_metadataProvider</c> field already keeps one persistent
    /// <see cref="AniListMetadataProvider"/> for the exact same reason) fixes this without making
    /// the rate limiter itself static/process-wide, which would have also slowed down this class's
    /// own unit tests (each constructing a fresh instance deliberately, for isolation).
    /// </summary>
    public static readonly MangaBakaMetadataProvider Shared = new(MangaBakaHttpClient.Shared);

    public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.MangaBaka;

    /// <summary>Satisfies <see cref="ITrackerSearchProvider"/> - see this class's own doc comment.</summary>
    TrackingService ITrackerSearchProvider.Service => TrackingService.MangaBaka;

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var response = await FetchAsync<MangaBakaSearchResponse>(
            $"series/search?q={Uri.EscapeDataString(query)}", cancellationToken).ConfigureAwait(false);

        var results = response?.Data;
        if (results is null)
        {
            return Array.Empty<MetadataSearchResult>();
        }

        return results.Select(MangaBakaNormalizer.ToSearchResult).ToList();
    }

    public async Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(externalId, out int id))
        {
            return null;
        }

        var response = await FetchAsync<MangaBakaGetResponse>($"series/{id}", cancellationToken).ConfigureAwait(false);
        return response?.Data is null ? null : MangaBakaNormalizer.ToMediaMetadata(response.Data);
    }

    private async Task<T?> FetchAsync<T>(string relativeUrl, CancellationToken cancellationToken) where T : class
    {
        await WaitForRateLimitAsync(cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(BaseUrl + relativeUrl, cancellationToken).ConfigureAwait(false);
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
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return null;
            }
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
}

internal sealed class MangaBakaSearchResponse
{
    [JsonPropertyName("data")]
    public List<MangaBakaSeriesDto>? Data { get; set; }
}

internal sealed class MangaBakaGetResponse
{
    [JsonPropertyName("data")]
    public MangaBakaSeriesDto? Data { get; set; }
}

/// <summary>
/// Covers both the search endpoint and the get-by-id endpoint's response shape - both were
/// re-confirmed live to return only the <see cref="Titles"/> array, no flat top-level title field
/// at all (an earlier capture that appeared to show one on the search endpoint didn't reproduce on
/// a later live check - possibly a fetch-tooling artifact, possibly real API drift; either way,
/// <see cref="Title"/> is kept as a tolerant first-choice fallback in case it's ever populated, and
/// <see cref="MangaBakaNormalizer.ResolveDisplayTitle"/> falls through to <see cref="Titles"/>
/// correctly either way, verified by a dedicated test).
/// </summary>
internal sealed class MangaBakaSeriesDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("titles")]
    public List<MangaBakaTitleDto>? Titles { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("total_chapters")]
    public int? TotalChapters { get; set; }

    [JsonPropertyName("final_volume")]
    public int? FinalVolume { get; set; }
}

internal sealed class MangaBakaTitleDto
{
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("traits")]
    public List<string>? Traits { get; set; }

    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }
}

internal static class MangaBakaNormalizer
{
    public static MetadataSearchResult ToSearchResult(MangaBakaSeriesDto dto) =>
        new(dto.Id.ToString(CultureInfo.InvariantCulture), ResolveDisplayTitle(dto), Url: null);

    public static ExternalMediaMetadata ToMediaMetadata(MangaBakaSeriesDto dto) => new(
        dto.Id.ToString(CultureInfo.InvariantCulture),
        ResolveDisplayTitle(dto),
        Url: null, // No canonical series-page URL field anywhere in the API response, confirmed live.
        dto.Description,
        dto.Status,
        dto.TotalChapters,
        dto.FinalVolume,
        TitleEnglish: FindTitle(dto, t => t.Language == "en"),
        TitleRomaji: FindTitle(dto, t => t.Language == "ja-Latn"),
        TitleNative: FindTitle(dto, t => t.Language == "ja" && t.Traits?.Contains("native") == true));

    /// <summary>English beats the search endpoint's own flat title (already usually English) beats
    /// the first primary-flagged localization beats whatever's first - never throws even when every
    /// title field is somehow empty.</summary>
    private static string ResolveDisplayTitle(MangaBakaSeriesDto dto) =>
        dto.Title
        ?? FindTitle(dto, t => t.Language == "en")
        ?? FindTitle(dto, t => t.IsPrimary)
        ?? dto.Titles?.FirstOrDefault()?.Title
        ?? "Untitled";

    private static string? FindTitle(MangaBakaSeriesDto dto, Func<MangaBakaTitleDto, bool> predicate) =>
        dto.Titles?.FirstOrDefault(predicate)?.Title;
}

/// <summary>Shared <see cref="HttpClient"/>, same rationale as <see cref="AniListHttpClient"/>.
/// 30s, not 15s - found live (2026-08-23, on-screen verification of the Apply-from-Provider
/// feature) that a raw request to api.mangabaka.org can legitimately take ~22s to complete from
/// this network even though it always eventually succeeds (curl against the same endpoint returns
/// near-instantly, so this is specifically a .NET HttpClient/socket-negotiation characteristic on
/// this environment, not a slow server) - the original 15s timeout was silently discarding a
/// request that was still in flight, indistinguishable from "no matches found" in the UI.</summary>
public static class MangaBakaHttpClient
{
    public static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(30) };
}
