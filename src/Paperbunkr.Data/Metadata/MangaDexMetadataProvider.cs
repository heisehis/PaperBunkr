using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Third real <see cref="IMetadataProvider"/> implementation, alongside <see cref="AniListMetadataProvider"/>
/// and <see cref="MangaBakaMetadataProvider"/> (docs/superpowers/specs/2026-08-19-metadata-model-
/// second-provider-mangadex-design.md - sketched 2026-08-19, built 2026-09-05). REST, not GraphQL:
/// `GET /manga?title=` (search) and `GET /manga/{id}` (get by id) against `api.mangadex.org`, no auth
/// needed for either. Metadata-only, like <see cref="MangaBakaMetadataProvider"/>'s search half -
/// MangaDex has no authenticated per-user library API, so this deliberately does NOT implement
/// <c>ITrackerSearchProvider</c>/<c>ITrackerAdapter</c>, unlike AniList/MangaBaka which are both.
/// </summary>
public sealed class MangaDexMetadataProvider : IMetadataProvider
{
    private const string BaseUrl = "https://api.mangadex.org/";

    /// <summary>MangaDex documents a global ~5 requests/second/IP limit (api.mangadex.org/docs, the
    /// "Rate limits" page, re-verified live 2026-09-05) - erring conservative under that, same
    /// "target ~2-3 req/s for bulk scraping" posture already recorded in docs/open_items_resolved.md
    /// §2 for this exact provider, before it had an adapter.</summary>
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(400);

    private readonly HttpClient _httpClient;
    private readonly object _rateLimitLock = new();
    private DateTime _nextAllowedRequestUtc = DateTime.MinValue;

    public MangaDexMetadataProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>One shared instance for every real call site, same rationale as
    /// <see cref="MangaBakaMetadataProvider.Shared"/>'s own doc comment - an instance-scoped rate
    /// limiter reset per-call defeats the whole point of having one.</summary>
    public static readonly MangaDexMetadataProvider Shared = new(MangaDexHttpClient.Shared);

    public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.MangaDex;

    /// <summary>Throws <see cref="MetadataProviderUnavailableException"/> on a failed call (network
    /// error, rate limit, malformed response) - matches <see cref="AniListMetadataProvider"/>'s
    /// documented contract from <see cref="MetadataProviderUnavailableException"/>'s own doc comment,
    /// rather than <see cref="MangaBakaMetadataProvider"/>'s looser "return empty" shape.</summary>
    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<MetadataSearchResult>();
        }

        var response = await FetchAsync<MangaDexSearchResponse>(
            $"manga?title={Uri.EscapeDataString(query)}&limit=10", cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            throw new MetadataProviderUnavailableException();
        }

        var results = response.Data ?? new List<MangaDexMangaDto>();
        return results.Select(MangaDexNormalizer.ToSearchResult).ToList();
    }

    public async Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        var response = await FetchAsync<MangaDexGetResponse>($"manga/{Uri.EscapeDataString(externalId)}", cancellationToken).ConfigureAwait(false);
        return response?.Data is null ? null : MangaDexNormalizer.ToMediaMetadata(response.Data);
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
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                ObserveRetryAfter(response);
                return null;
            }

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

internal sealed class MangaDexSearchResponse
{
    [JsonPropertyName("data")]
    public List<MangaDexMangaDto>? Data { get; set; }
}

internal sealed class MangaDexGetResponse
{
    [JsonPropertyName("data")]
    public MangaDexMangaDto? Data { get; set; }
}

internal sealed class MangaDexMangaDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("attributes")]
    public MangaDexAttributesDto? Attributes { get; set; }
}

/// <summary>MangaDex's <c>title</c>/<c>description</c> are language-code-keyed maps (<c>en</c>,
/// <c>ja</c>, <c>ja-ro</c>, ...), and <c>altTitles</c> is an array of single-key maps of the same
/// shape - richer than AniList's three fixed fields, per the design spec's own note. <c>tags</c>
/// carries its own <c>attributes.group</c> ("genre"/"theme"/"format"/"content") - only "genre"-group
/// tags feed <see cref="ExternalMediaMetadata.Genre"/>, so "theme"/"format"/"content" tags (e.g.
/// "School Life", "Web Comic", "Gore") don't over-broaden what this codebase treats as Genre.</summary>
internal sealed class MangaDexAttributesDto
{
    [JsonPropertyName("title")]
    public Dictionary<string, string>? Title { get; set; }

    [JsonPropertyName("altTitles")]
    public List<Dictionary<string, string>>? AltTitles { get; set; }

    [JsonPropertyName("description")]
    public Dictionary<string, string>? Description { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("tags")]
    public List<MangaDexTagDto>? Tags { get; set; }
}

internal sealed class MangaDexTagDto
{
    [JsonPropertyName("attributes")]
    public MangaDexTagAttributesDto? Attributes { get; set; }
}

internal sealed class MangaDexTagAttributesDto
{
    [JsonPropertyName("name")]
    public Dictionary<string, string>? Name { get; set; }

    [JsonPropertyName("group")]
    public string? Group { get; set; }
}

internal static class MangaDexNormalizer
{
    public static MetadataSearchResult ToSearchResult(MangaDexMangaDto dto) =>
        new(dto.Id, ResolveDisplayTitle(dto), $"https://mangadex.org/title/{dto.Id}");

    public static ExternalMediaMetadata ToMediaMetadata(MangaDexMangaDto dto) => new(
        dto.Id,
        ResolveDisplayTitle(dto),
        $"https://mangadex.org/title/{dto.Id}",
        FindLocalized(dto.Attributes?.Description, "en"),
        dto.Attributes?.Status,
        ChapterCount: null, // Not on this endpoint - would need /manga/{id}/aggregate, out of this provider's scope.
        VolumeCount: null,
        TitleEnglish: FindTitle(dto, "en"),
        TitleRomaji: FindTitle(dto, "ja-ro"),
        TitleNative: FindTitle(dto, "ja"),
        Genre: ResolveGenre(dto));

    /// <summary>English beats the first alt-title in any language beats "Untitled" - never throws
    /// even when every title field is somehow empty.</summary>
    private static string ResolveDisplayTitle(MangaDexMangaDto dto) =>
        FindTitle(dto, "en")
        ?? dto.Attributes?.Title?.Values.FirstOrDefault()
        ?? dto.Attributes?.AltTitles?.SelectMany(t => t.Values).FirstOrDefault()
        ?? "Untitled";

    /// <summary>Checks the primary <c>title</c> map first, then falls back to the first matching
    /// entry in the plural <c>altTitles</c> array (MangaDex allows more than one title per language;
    /// per the design spec's own open question, this takes the first only, not all - "boring version
    /// first" precedent).</summary>
    private static string? FindTitle(MangaDexMangaDto dto, string languageCode) =>
        FindLocalized(dto.Attributes?.Title, languageCode)
        ?? dto.Attributes?.AltTitles?
            .Select(t => FindLocalized(t, languageCode))
            .FirstOrDefault(v => v is not null);

    private static string? FindLocalized(Dictionary<string, string>? map, string languageCode) =>
        map is not null && map.TryGetValue(languageCode, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string? ResolveGenre(MangaDexMangaDto dto)
    {
        var genreNames = dto.Attributes?.Tags?
            .Where(t => string.Equals(t.Attributes?.Group, "genre", StringComparison.OrdinalIgnoreCase))
            .Select(t => FindLocalized(t.Attributes?.Name, "en"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return genreNames is { Count: > 0 } ? string.Join(", ", genreNames) : null;
    }
}

/// <summary>Shared <see cref="HttpClient"/>, same rationale as <see cref="AniListHttpClient"/>/
/// <see cref="MangaBakaHttpClient"/>.</summary>
public static class MangaDexHttpClient
{
    public static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(15) };
}
