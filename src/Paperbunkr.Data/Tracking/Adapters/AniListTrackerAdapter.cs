using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tracking.Adapters;

/// <summary>
/// AniList push half of tracker sync (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-
/// design.md) - search is already covered by <see cref="AniListMetadataProvider"/> implementing
/// <see cref="ITrackerSearchProvider"/> directly. Uses a fresh, per-request <c>Authorization</c>
/// header rather than mutating <see cref="AniListHttpClient"/>'s shared instance's default headers -
/// the read-only metadata provider must stay usable with zero AniList account connected, so an
/// authenticated write call must never leak onto the shared client's anonymous search/get calls.
/// </summary>
public sealed class AniListTrackerAdapter : ITrackerAdapter
{
    private const string Endpoint = "https://graphql.anilist.co";
    private const string AuthorizeEndpoint = "https://anilist.co/api/v2/oauth/authorize";

    private const string SaveMediaListEntryMutation = """
        mutation ($mediaId: Int, $status: MediaListStatus, $progress: Int) {
          SaveMediaListEntry(mediaId: $mediaId, status: $status, progress: $progress) {
            id
          }
        }
        """;

    /// <summary><c>mediaListEntry</c> on <c>Media</c> resolves to the authenticated viewer's own list
    /// entry for that media (confirmed live against AniList's own API docs, docs.anilist.co/guide/
    /// graphql/queries/media-list) - no separate numeric AniList user id needed, unlike Mihon's own
    /// <c>findLibManga</c> (which queries <c>Page.mediaList(userId:, mediaId:)</c> and therefore has
    /// to look up the viewer's id first). Null when unauthenticated or the media has no entry yet.</summary>
    private const string GetMediaListEntryQuery = """
        query ($mediaId: Int) {
          Media(id: $mediaId) {
            mediaListEntry {
              status
              progress
            }
          }
        }
        """;

    private readonly HttpClient _httpClient;

    public AniListTrackerAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TrackingService Service => TrackingService.AniList;

    /// <summary>Implicit-grant authorize URL - the user's own registered Client ID, pasted into
    /// Preferences. AniList returns the access token directly in the redirect URL's fragment, no
    /// exchange call needed (unlike MyAnimeList/Shikimori's authorization-code flows).</summary>
    public static string BuildAuthorizationUrl(string clientId) =>
        $"{AuthorizeEndpoint}?client_id={Uri.EscapeDataString(clientId)}&response_type=token";

    /// <summary>Stores the pasted-back token directly - nothing to exchange for the implicit grant.</summary>
    public static void CompleteConnect(PaperbunkrDbContext context, string accessToken) =>
        CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken, accessToken);

    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? accessToken = CredentialStore.Get(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(accessToken) || !int.TryParse(link.ExternalId, out int mediaId))
        {
            return false;
        }

        string status = AniListStatusMapper.ToMediaListStatus(payload.Status);

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                query = SaveMediaListEntryMutation,
                variables = new { mediaId, status, progress = payload.ChapterProgress },
            }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            try
            {
                var parsed = await response.Content
                    .ReadFromJsonAsync<AniListMutationResponse>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return parsed?.Errors is not { Count: > 0 };
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
        }
    }

    public async Task<TrackerRemoteEntry?> GetEntryAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken)
    {
        string? accessToken = CredentialStore.Get(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(accessToken) || !int.TryParse(link.ExternalId, out int mediaId))
        {
            return null;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { query = GetMediaListEntryQuery, variables = new { mediaId } }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

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

            AniListMediaListEntryResponse? parsed;
            try
            {
                parsed = await response.Content
                    .ReadFromJsonAsync<AniListMediaListEntryResponse>(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }

            if (parsed?.Errors is { Count: > 0 })
            {
                return null;
            }

            var entry = parsed?.Data?.Media?.MediaListEntry;
            return entry is null ? null : new TrackerRemoteEntry(AniListStatusMapper.FromMediaListStatus(entry.Status), entry.Progress);
        }
    }
}

/// <summary><see cref="ReadingStatus"/> -&gt; AniList's <c>MediaListStatus</c> enum - clean 1:1, no
/// lossy case (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md).</summary>
public static class AniListStatusMapper
{
    public static string ToMediaListStatus(ReadingStatus status) => status switch
    {
        ReadingStatus.Planned => "PLANNING",
        ReadingStatus.Reading => "CURRENT",
        ReadingStatus.Completed => "COMPLETED",
        ReadingStatus.Paused => "PAUSED",
        ReadingStatus.Dropped => "DROPPED",
        ReadingStatus.ReReading => "REPEATING",
        _ => "PLANNING",
    };

    /// <summary>Reverse of <see cref="ToMediaListStatus"/> - clean 1:1 both ways, unlike services
    /// whose push side is already lossy. Unrecognized/null resolves to <see cref="ReadingStatus.Unknown"/>
    /// rather than guessing.</summary>
    public static ReadingStatus FromMediaListStatus(string? status) => status switch
    {
        "PLANNING" => ReadingStatus.Planned,
        "CURRENT" => ReadingStatus.Reading,
        "COMPLETED" => ReadingStatus.Completed,
        "PAUSED" => ReadingStatus.Paused,
        "DROPPED" => ReadingStatus.Dropped,
        "REPEATING" => ReadingStatus.ReReading,
        _ => ReadingStatus.Unknown,
    };
}

internal sealed class AniListMutationResponse
{
    [JsonPropertyName("errors")]
    public System.Collections.Generic.List<object>? Errors { get; set; }
}

internal sealed class AniListMediaListEntryResponse
{
    [JsonPropertyName("data")]
    public AniListMediaListEntryData? Data { get; set; }

    [JsonPropertyName("errors")]
    public System.Collections.Generic.List<object>? Errors { get; set; }
}

internal sealed class AniListMediaListEntryData
{
    [JsonPropertyName("Media")]
    public AniListMediaListEntryMedia? Media { get; set; }
}

internal sealed class AniListMediaListEntryMedia
{
    [JsonPropertyName("mediaListEntry")]
    public AniListMediaListEntryDto? MediaListEntry { get; set; }
}

internal sealed class AniListMediaListEntryDto
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("progress")]
    public int? Progress { get; set; }
}
