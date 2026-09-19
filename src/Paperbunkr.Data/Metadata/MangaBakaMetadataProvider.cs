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
public sealed class MangaBakaMetadataProvider : IMetadataProvider, ITrackerSearchProvider, IMultiCoverProvider, IRelationsProvider
{
    /// <summary>Migrated from the beta <c>v2</c> family to the stable, richer <c>v1</c> family
    /// (docs/superpowers/specs/2026-09-18-external-metadata-full-extraction-design.md §6) - <c>v2</c>
    /// has no covers/relations/richer-tag-taxonomy endpoints at all. <c>series/search</c>/
    /// <c>series/{id}</c> response shape is assumed unchanged between the two families per
    /// docs/mangabaka-metadata-ui-research.md finding 14 ("v1 is also the richer family" - not a
    /// narrower/differently-shaped one); reconfirm against a live response before shipping.</summary>
    private const string BaseUrl = "https://api.mangabaka.org/v1/";

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

    /// <summary>
    /// First page only (`GET /v1/series/{id}/images`) - MangaBaka's own archives run into the
    /// hundreds of entries for a popular series (confirmed live: 931 for One Piece), and this is a
    /// browse-and-pick UI, not a bulk export; a further "load more" page is future work if it's
    /// ever asked for.
    /// </summary>
    public async Task<IReadOnlyList<CoverCandidate>> GetCoverCandidatesAsync(string externalId, CancellationToken cancellationToken)
    {
        var response = await FetchAsync<MangaBakaImagesResponse>($"series/{Uri.EscapeDataString(externalId)}/images?page=1", cancellationToken).ConfigureAwait(false);
        var items = response?.Data;
        if (items is null)
        {
            return Array.Empty<CoverCandidate>();
        }

        return items
            .Where(i => i.Image?.Raw?.Url is not null)
            .Select(i => new CoverCandidate(
                ThumbnailUrl: i.Image!.X150?.X1 ?? i.Image.Raw!.Url!,
                FullUrl: i.Image.Raw!.Url!,
                Type: i.Type ?? "other",
                Source: i.Note))
            .ToList();
    }

    /// <summary>
    /// <c>relationships_v2</c> is already embedded in the plain get-by-id response (confirmed live,
    /// no separate `/v1/series/{id}/relationships` call needed) - but it only carries a bare
    /// `to_series_id`, not a title/URL, so each relation needs a follow-up <see cref="GetAsync"/>
    /// call to resolve a display title. Same "only on explicit Related-tab open" lazy trigger as
    /// <see cref="AniListMetadataProvider.GetRelationsAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<ProviderRelation>> GetRelationsAsync(string externalId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(externalId, out int id))
        {
            return Array.Empty<ProviderRelation>();
        }

        var response = await FetchAsync<MangaBakaGetResponse>($"series/{id}", cancellationToken).ConfigureAwait(false);
        var relationships = response?.Data?.RelationshipsV2;
        if (relationships is null)
        {
            return Array.Empty<ProviderRelation>();
        }

        var results = new List<ProviderRelation>();
        foreach (var relationship in relationships)
        {
            var target = await GetAsync(relationship.ToSeriesId.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                continue;
            }

            results.Add(new ProviderRelation(
                TargetExternalId: target.ExternalId,
                TargetTitle: target.Title,
                TargetUrl: target.Url,
                Type: ProviderRelationTypeMapper.MapMangaBaka(relationship.RelationType)));
        }

        return results;
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
/// re-confirmed live against `v1` (2026-09-18, following the `v2`→`v1` migration) to return the
/// *same* rich shape for both, unlike `v2`'s search/get split. `title`/`native_title`/
/// `romanized_title` are flat strings (reliably populated, unlike `v2`'s all-`titles`-array-only
/// shape the old fixture assumed), `total_chapters`/`final_volume` are strings (not ints - e.g.
/// `"1193"`), and `canonical_url` is real (the old `v2` "no canonical URL field" finding no longer
/// holds under `v1`).
/// </summary>
internal sealed class MangaBakaSeriesDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("native_title")]
    public string? NativeTitle { get; set; }

    [JsonPropertyName("romanized_title")]
    public string? RomanizedTitle { get; set; }

    [JsonPropertyName("titles")]
    public List<MangaBakaTitleDto>? Titles { get; set; }

    [JsonPropertyName("canonical_url")]
    public string? CanonicalUrl { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("total_chapters")]
    public string? TotalChapters { get; set; }

    [JsonPropertyName("final_volume")]
    public string? FinalVolume { get; set; }

    [JsonPropertyName("authors")]
    public List<string>? Authors { get; set; }

    [JsonPropertyName("artists")]
    public List<string>? Artists { get; set; }

    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }

    /// <summary>The richer, categorized+weighted tag list (real per-series `weight` values
    /// confirmed live, e.g. `"incidental"`) - <see cref="IssueTagWeight"/> import still stays
    /// `Unset` regardless (the standing "never inferred on migration or import" rule,
    /// `IssueTag.cs`'s own doc comment - independent of whether a provider has a live value).</summary>
    [JsonPropertyName("tags_v2")]
    public List<MangaBakaTagV2Dto>? TagsV2 { get; set; }

    [JsonPropertyName("relationships_v2")]
    public List<MangaBakaRelationshipV2Dto>? RelationshipsV2 { get; set; }

    [JsonPropertyName("source")]
    public MangaBakaSourceDto? Source { get; set; }

    [JsonPropertyName("cover")]
    public MangaBakaCoverDto? Cover { get; set; }
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

internal sealed class MangaBakaTagV2Dto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("name_path")]
    public string? NamePath { get; set; }

    [JsonPropertyName("is_genre")]
    public bool IsGenre { get; set; }

    [JsonPropertyName("is_spoiler")]
    public bool IsSpoiler { get; set; }
}

internal sealed class MangaBakaRelationshipV2Dto
{
    [JsonPropertyName("to_series_id")]
    public int ToSeriesId { get; set; }

    [JsonPropertyName("relation_type")]
    public string? RelationType { get; set; }
}

/// <summary>Cross-referenced external ids for the six tracked sources this codebase has an
/// <see cref="ExternalMetadataProvider"/> value for (`anime_news_network` also exists live but has
/// no matching enum value - skipped). Each source's `id` is a string for some providers
/// (`manga_updates`, `anime_planet`) and a number for others - <see cref="JsonElement"/> reads
/// either without a custom converter.</summary>
internal sealed class MangaBakaSourceDto
{
    [JsonPropertyName("anilist")]
    public MangaBakaSourceEntryDto? AniList { get; set; }

    [JsonPropertyName("kitsu")]
    public MangaBakaSourceEntryDto? Kitsu { get; set; }

    [JsonPropertyName("manga_updates")]
    public MangaBakaSourceEntryDto? MangaUpdates { get; set; }

    [JsonPropertyName("my_anime_list")]
    public MangaBakaSourceEntryDto? MyAnimeList { get; set; }

    [JsonPropertyName("anime_planet")]
    public MangaBakaSourceEntryDto? AnimePlanet { get; set; }

    [JsonPropertyName("shikimori")]
    public MangaBakaSourceEntryDto? Shikimori { get; set; }
}

internal sealed class MangaBakaSourceEntryDto
{
    [JsonPropertyName("id")]
    public JsonElement Id { get; set; }
}

internal sealed class MangaBakaCoverDto
{
    [JsonPropertyName("raw")]
    public MangaBakaCoverVariantDto? Raw { get; set; }
}

internal sealed class MangaBakaCoverVariantDto
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

internal sealed class MangaBakaImagesResponse
{
    [JsonPropertyName("data")]
    public List<MangaBakaImageDto>? Data { get; set; }
}

internal sealed class MangaBakaImageDto
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Source attribution, e.g. "Banner from Weekly Shounen Jump website" - shown as the
    /// candidate's tooltip in the picker grid.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("image")]
    public MangaBakaImageVariantsDto? Image { get; set; }
}

internal sealed class MangaBakaImageVariantsDto
{
    [JsonPropertyName("raw")]
    public MangaBakaCoverVariantDto? Raw { get; set; }

    [JsonPropertyName("x150")]
    public MangaBakaImageSizeDto? X150 { get; set; }
}

internal sealed class MangaBakaImageSizeDto
{
    [JsonPropertyName("x1")]
    public string? X1 { get; set; }
}

internal static class MangaBakaNormalizer
{
    public static MetadataSearchResult ToSearchResult(MangaBakaSeriesDto dto) =>
        new(dto.Id.ToString(CultureInfo.InvariantCulture), ResolveDisplayTitle(dto), dto.CanonicalUrl);

    public static ExternalMediaMetadata ToMediaMetadata(MangaBakaSeriesDto dto) => new(
        dto.Id.ToString(CultureInfo.InvariantCulture),
        ResolveDisplayTitle(dto),
        dto.CanonicalUrl,
        dto.Description,
        dto.Status,
        ParseIntOrNull(dto.TotalChapters),
        ParseIntOrNull(dto.FinalVolume),
        TitleEnglish: FindTitle(dto, t => t.Language == "en"),
        TitleRomaji: dto.RomanizedTitle ?? FindTitle(dto, t => t.Language == "ja-Latn"),
        TitleNative: dto.NativeTitle ?? FindTitle(dto, t => t.Language == "ja" && t.Traits?.Contains("native") == true),
        Genre: dto.Genres is { Count: > 0 } genres ? string.Join(", ", genres) : null,
        CoverImageUrl: dto.Cover?.Raw?.Url,
        Creator: ResolveCreator(dto),
        PublicationYear: dto.Year,
        PublicationFormat: dto.Type,
        Demographic: null, // Confirmed absent from the v1 schema entirely, not just empty for a given series.
        CrossReferences: ResolveCrossReferences(dto.Source),
        GenreTags: dto.Genres is { Count: > 0 } genreTags ? genreTags : null,
        OtherTags: ResolveOtherTags(dto.TagsV2));

    /// <summary>English beats the flat <c>title</c> field beats the first primary-flagged
    /// localization beats whatever's first - never throws even when every title field is somehow
    /// empty.</summary>
    private static string ResolveDisplayTitle(MangaBakaSeriesDto dto) =>
        FindTitle(dto, t => t.Language == "en")
        ?? dto.Title
        ?? FindTitle(dto, t => t.IsPrimary)
        ?? dto.Titles?.FirstOrDefault()?.Title
        ?? "Untitled";

    private static string? FindTitle(MangaBakaSeriesDto dto, Func<MangaBakaTitleDto, bool> predicate) =>
        dto.Titles?.FirstOrDefault(predicate)?.Title;

    private static int? ParseIntOrNull(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;

    private static string? ResolveCreator(MangaBakaSeriesDto dto)
    {
        var names = (dto.Authors ?? new List<string>())
            .Concat(dto.Artists ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count > 0 ? string.Join(", ", names) : null;
    }

    /// <summary>Non-genre, non-spoiler tags. <see cref="MangaBakaTagV2Dto.NamePath"/> is a
    /// breadcrumb like <c>"Activities &gt; Competitions"</c> - the first segment is the category
    /// (Genres/Themes/Settings/Activities/Character Traits/...), matching the taxonomy structure
    /// docs/mangabaka-metadata-ui-research.md finding 15 described.</summary>
    private static IReadOnlyList<(string Value, string Category)>? ResolveOtherTags(List<MangaBakaTagV2Dto>? tagsV2)
    {
        var values = tagsV2?
            .Where(t => !t.IsGenre && !t.IsSpoiler && !string.IsNullOrWhiteSpace(t.Name))
            .Select(t => (t.Name!, CategoryFromNamePath(t.NamePath)))
            .ToList();

        return values is { Count: > 0 } ? values : null;
    }

    private static string CategoryFromNamePath(string? namePath)
    {
        if (string.IsNullOrWhiteSpace(namePath))
        {
            return "Uncategorized";
        }

        int separatorIndex = namePath.IndexOf('>');
        return separatorIndex > 0 ? namePath[..separatorIndex].Trim() : namePath.Trim();
    }

    private static IReadOnlyList<(ExternalMetadataProvider Provider, string ExternalId)>? ResolveCrossReferences(MangaBakaSourceDto? source)
    {
        if (source is null)
        {
            return null;
        }

        var result = new List<(ExternalMetadataProvider, string)>();
        AddIfPresent(result, ExternalMetadataProvider.AniList, source.AniList);
        AddIfPresent(result, ExternalMetadataProvider.Kitsu, source.Kitsu);
        AddIfPresent(result, ExternalMetadataProvider.MangaUpdates, source.MangaUpdates);
        AddIfPresent(result, ExternalMetadataProvider.MyAnimeList, source.MyAnimeList);
        AddIfPresent(result, ExternalMetadataProvider.AnimePlanet, source.AnimePlanet);
        AddIfPresent(result, ExternalMetadataProvider.Shikimori, source.Shikimori);

        return result.Count > 0 ? result : null;
    }

    private static void AddIfPresent(List<(ExternalMetadataProvider, string)> result, ExternalMetadataProvider provider, MangaBakaSourceEntryDto? entry)
    {
        if (entry is null)
        {
            return;
        }

        string? id = entry.Id.ValueKind switch
        {
            JsonValueKind.String => entry.Id.GetString(),
            JsonValueKind.Number => entry.Id.GetRawText(),
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(id))
        {
            result.Add((provider, id));
        }
    }
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
