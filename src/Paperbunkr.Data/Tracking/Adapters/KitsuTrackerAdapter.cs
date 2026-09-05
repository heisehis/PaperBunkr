using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tracking.Adapters;

/// <summary>
/// Kitsu (kitsu.app) adapter (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md's
/// per-service pattern, extended to the 6th and last of the originally-scoped services - see
/// docs/tracker-manga-ui-research.md §1.3) - GraphQL (<c>https://kitsu.app/api/graphql</c>), OAuth2
/// password grant for login (<c>POST /api/oauth/token</c>, confirmed against Mihon's real
/// <c>KitsuApi.kt</c>/<c>Kitsu.kt</c> source since Kitsu itself publishes no current API reference).
///
/// <para><b>Client credentials, deliberately not a per-user registration:</b> unlike AniList/
/// MyAnimeList/Shikimori (each user registers their own OAuth app), Kitsu has no self-serve
/// third-party app registration at all. Every open-source manga reader that supports Kitsu (Mihon,
/// Komikku, and their forks) ships the same hardcoded <see cref="ClientId"/>/<see cref="ClientSecret"/>
/// pair, confirmed live in Mihon's own public source - there is no alternative a Paperbunkr user could
/// supply instead. Reusing them is a deliberate, user-approved call (2026-09-05), made with the
/// understanding that these aren't Paperbunkr's own credentials and Kitsu could revoke or rate-limit
/// them without notice, unlike every other tracker adapter in this file.</para>
/// </summary>
public sealed class KitsuTrackerAdapter : ITrackerSearchProvider, ITrackerAdapter
{
    private const string GraphQlEndpoint = "https://kitsu.app/api/graphql";
    private const string LoginEndpoint = "https://kitsu.app/api/oauth/token";

    private const string ClientId = "dd031b32d2f56c990b1425efe6c42ad847e7fe3ab46bf1299f05ecd856bdb7dd";
    private const string ClientSecret = "54d7307928f63414defd96399fc31ba847961ceaecef3a5fd93144e960c0e151";

    private readonly HttpClient _httpClient;
    private readonly string? _accessToken;

    /// <param name="accessToken">The stored bearer token, read from <see cref="CredentialStore"/> by
    /// the caller and passed in - same "no <see cref="PaperbunkrDbContext"/> in this shared signature"
    /// rationale as <see cref="MyAnimeListTrackerAdapter"/>'s own <c>clientId</c> parameter. Unlike
    /// MyAnimeList's search (a public endpoint needing only a Client ID), Kitsu's GraphQL API requires
    /// a full bearer token for every call including search - confirmed from Mihon's own
    /// <c>KitsuApi.search()</c>, which uses its authenticated client, not the plain one.</param>
    public KitsuTrackerAdapter(HttpClient httpClient, string? accessToken)
    {
        _httpClient = httpClient;
        _accessToken = accessToken;
    }

    public TrackingService Service => TrackingService.Kitsu;

    public async Task<bool> CompleteConnectAsync(PaperbunkrDbContext context, string username, string password, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(LoginEndpoint, new FormUrlEncodedContent(form), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return false;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            KitsuOAuthResponse? token;
            try
            {
                token = await response.Content.ReadFromJsonAsync<KitsuOAuthResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return false;
            }

            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                return false;
            }

            CredentialStore.Set(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken, token.AccessToken);
            if (!string.IsNullOrEmpty(token.RefreshToken))
            {
                CredentialStore.Set(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthRefreshToken, token.RefreshToken);
            }

            return true;
        }
    }

    /// <summary>Returns empty (not connected yet) rather than throwing when there's no stored token -
    /// same "precondition, not a provider failure" idiom as <see cref="MyAnimeListTrackerAdapter"/>'s
    /// own null-Client-ID case.</summary>
    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<MetadataSearchResult>();
        }

        const string searchQuery = """
            query Query($query: String!) {
              searchMangaByTitle(title: $query, first: 20) {
                nodes { id titles { preferred } slug }
              }
            }
            """;

        var envelope = await SendGraphQlAsync<KitsuSearchData>(searchQuery, new { query }, _accessToken, cancellationToken).ConfigureAwait(false);
        if (envelope is null || HasTransportErrors(envelope))
        {
            throw new MetadataProviderUnavailableException();
        }

        var nodes = envelope.Data?.SearchMangaByTitle?.Nodes ?? new List<KitsuMangaDto>();
        return nodes.Select(KitsuNormalizer.ToSearchResult).ToList();
    }

    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? token = CredentialStore.Get(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        string status = KitsuStatusMapper.ToStatus(payload.Status);
        int progress = payload.ChapterProgress ?? 0;

        var existingEntry = await FindExistingLibraryEntryAsync(token, link.ExternalId, cancellationToken).ConfigureAwait(false);

        return existingEntry?.Id is not string libraryEntryId
            ? await CreateLibraryEntryAsync(token, link.ExternalId, status, progress, cancellationToken).ConfigureAwait(false)
            : await UpdateLibraryEntryAsync(token, libraryEntryId, status, progress, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrackerRemoteEntry?> GetEntryAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken)
    {
        string? token = CredentialStore.Get(context, nameof(TrackingService.Kitsu), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var entry = await FindExistingLibraryEntryAsync(token, link.ExternalId, cancellationToken).ConfigureAwait(false);
        return entry is null ? null : new TrackerRemoteEntry(KitsuStatusMapper.FromStatus(entry.Status), (int?)entry.Progress);
    }

    /// <summary>Looks up this series' existing Kitsu library entry, if any - <see cref="PushEntryAsync"/>
    /// uses just its id (to decide create vs. update), <see cref="GetEntryAsync"/> uses its status/
    /// progress too. One shared query serves both, same "one lookup, not two round trips" precedent
    /// as <see cref="ShikimoriTrackerAdapter.FindExistingRateAsync"/>. Returns null (treat as "create"/
    /// "nothing to compare") on any failure, not just a genuine "no entry yet".</summary>
    private async Task<KitsuLibraryEntryRefDto?> FindExistingLibraryEntryAsync(string token, string mangaId, CancellationToken cancellationToken)
    {
        const string query = """
            query Query($id: ID!) {
              findMangaById(id: $id) {
                myLibraryEntry { id status progress }
              }
            }
            """;

        var envelope = await SendGraphQlAsync<KitsuFindByIdData>(query, new { id = mangaId }, token, cancellationToken).ConfigureAwait(false);
        if (envelope is null || HasTransportErrors(envelope))
        {
            return null;
        }

        return envelope.Data?.FindMangaById?.MyLibraryEntry;
    }

    private async Task<bool> CreateLibraryEntryAsync(string token, string mangaId, string status, int progress, CancellationToken cancellationToken)
    {
        const string mutation = """
            mutation AddManga($media_id: ID!, $status: LibraryEntryStatusEnum!, $progress: Int!) {
              libraryEntry {
                create(input: { mediaId: $media_id, mediaType: MANGA, status: $status, progress: $progress, private: false }) {
                  errors { message }
                  libraryEntry { id }
                }
              }
            }
            """;

        var envelope = await SendGraphQlAsync<KitsuMutationData>(
            mutation, new { media_id = mangaId, status, progress }, token, cancellationToken).ConfigureAwait(false);

        return MutationSucceeded(envelope, envelope?.Data?.LibraryEntry?.Create);
    }

    private async Task<bool> UpdateLibraryEntryAsync(string token, string libraryEntryId, string status, int progress, CancellationToken cancellationToken)
    {
        const string mutation = """
            mutation UpdateManga($library_id: ID!, $status: LibraryEntryStatusEnum!, $progress: Int!) {
              libraryEntry {
                update(input: { id: $library_id, status: $status, progress: $progress, private: false }) {
                  errors { message }
                  libraryEntry { id }
                }
              }
            }
            """;

        var envelope = await SendGraphQlAsync<KitsuMutationData>(
            mutation, new { library_id = libraryEntryId, status, progress }, token, cancellationToken).ConfigureAwait(false);

        return MutationSucceeded(envelope, envelope?.Data?.LibraryEntry?.Update);
    }

    private static bool MutationSucceeded(KitsuGraphQlEnvelope<KitsuMutationData>? envelope, KitsuLibraryEntryMutationResult? result)
    {
        if (envelope is null || HasTransportErrors(envelope))
        {
            return false;
        }

        // Kitsu can return HTTP 200 with either transport-level `errors`/`error` (checked above) or
        // mutation-payload-level `errors` nested under the create/update result - confirmed from
        // Mihon's own doc comment ("yes there are two different error attributes... it seems both are
        // valid in different cases"). Both must be clear, and the mutation must have actually returned
        // a created/updated entry id, for this to count as success.
        return result is { Errors: null or { Count: 0 }, LibraryEntry.Id: not null };
    }

    private static bool HasTransportErrors<T>(KitsuGraphQlEnvelope<T> envelope) =>
        envelope.Error is not null || envelope.Errors is { Count: > 0 };

    private async Task<KitsuGraphQlEnvelope<T>?> SendGraphQlAsync<T>(string query, object variables, string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GraphQlEndpoint)
        {
            Content = JsonContent.Create(new { query, variables }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
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
                return await response.Content.ReadFromJsonAsync<KitsuGraphQlEnvelope<T>>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

/// <summary><see cref="ReadingStatus"/> -&gt; Kitsu's <c>LibraryEntryStatusEnum</c> string. No
/// dedicated re-reading status exists on this service (Mihon's own <c>getRereadingStatus()</c>
/// returns -1, i.e. unsupported) - collapses into <c>CURRENT</c> (reading), same lossy-collapse
/// precedent as <see cref="BangumiCollectionTypeMapper"/>/<see cref="MangaUpdatesListMapper"/>.
/// </summary>
public static class KitsuStatusMapper
{
    public static string ToStatus(ReadingStatus status) => status switch
    {
        ReadingStatus.Reading => "CURRENT",
        ReadingStatus.Planned => "PLANNED",
        ReadingStatus.Completed => "COMPLETED",
        ReadingStatus.Paused => "ON_HOLD",
        ReadingStatus.Dropped => "DROPPED",
        ReadingStatus.ReReading => "CURRENT",
        _ => "PLANNED",
    };

    /// <summary>Reverse of <see cref="ToStatus"/> - a pulled <c>CURRENT</c> always resolves to plain
    /// <see cref="ReadingStatus.Reading"/>, never ReReading, same "collapsed on the push side already"
    /// precedent as every other lossy-mapped service.</summary>
    public static ReadingStatus FromStatus(string? status) => status switch
    {
        "CURRENT" => ReadingStatus.Reading,
        "PLANNED" => ReadingStatus.Planned,
        "COMPLETED" => ReadingStatus.Completed,
        "ON_HOLD" => ReadingStatus.Paused,
        "DROPPED" => ReadingStatus.Dropped,
        _ => ReadingStatus.Unknown,
    };
}

internal static class KitsuNormalizer
{
    public static MetadataSearchResult ToSearchResult(KitsuMangaDto dto) => new(
        dto.Id,
        dto.Titles?.Preferred ?? string.Empty,
        string.IsNullOrEmpty(dto.Slug) ? null : $"https://kitsu.app/manga/{dto.Slug}");
}

internal sealed class KitsuOAuthResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}

/// <summary>Shared shape for every GraphQL response this adapter parses - Kitsu can carry a
/// transport-level <see cref="Errors"/> array or a single <see cref="Error"/> object depending on
/// failure kind (confirmed from Mihon's own DTOs), alongside <see cref="Data"/>.</summary>
internal sealed class KitsuGraphQlEnvelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<KitsuGraphQlError>? Errors { get; set; }

    [JsonPropertyName("error")]
    public KitsuGraphQlError? Error { get; set; }
}

internal sealed class KitsuGraphQlError
{
    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

internal sealed class KitsuSearchData
{
    [JsonPropertyName("searchMangaByTitle")]
    public KitsuSearchNodes? SearchMangaByTitle { get; set; }
}

internal sealed class KitsuSearchNodes
{
    [JsonPropertyName("nodes")]
    public List<KitsuMangaDto>? Nodes { get; set; }
}

internal sealed class KitsuMangaDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("titles")]
    public KitsuTitlesDto? Titles { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

internal sealed class KitsuTitlesDto
{
    [JsonPropertyName("preferred")]
    public string? Preferred { get; set; }
}

internal sealed class KitsuFindByIdData
{
    [JsonPropertyName("findMangaById")]
    public KitsuMangaWithEntryDto? FindMangaById { get; set; }
}

internal sealed class KitsuMangaWithEntryDto
{
    [JsonPropertyName("myLibraryEntry")]
    public KitsuLibraryEntryRefDto? MyLibraryEntry { get; set; }
}

internal sealed class KitsuLibraryEntryRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("progress")]
    public long? Progress { get; set; }
}

internal sealed class KitsuMutationData
{
    [JsonPropertyName("libraryEntry")]
    public KitsuLibraryEntryMutationWrapper? LibraryEntry { get; set; }
}

internal sealed class KitsuLibraryEntryMutationWrapper
{
    [JsonPropertyName("create")]
    public KitsuLibraryEntryMutationResult? Create { get; set; }

    [JsonPropertyName("update")]
    public KitsuLibraryEntryMutationResult? Update { get; set; }
}

internal sealed class KitsuLibraryEntryMutationResult
{
    [JsonPropertyName("errors")]
    public List<KitsuGraphQlError>? Errors { get; set; }

    [JsonPropertyName("libraryEntry")]
    public KitsuLibraryEntryRefDto? LibraryEntry { get; set; }
}
