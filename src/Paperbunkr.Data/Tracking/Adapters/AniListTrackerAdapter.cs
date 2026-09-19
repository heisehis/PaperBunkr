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
public sealed class AniListTrackerAdapter : ITrackerAdapter, ITrackerDetailedPush
{
    private const string Endpoint = "https://graphql.anilist.co";
    private const string AuthorizeEndpoint = "https://anilist.co/api/v2/oauth/authorize";

    private const string SaveMediaListEntryMutation = """
        mutation ($mediaId: Int, $status: MediaListStatus, $progress: Int, $score: Float, $completedAt: FuzzyDateInput) {
          SaveMediaListEntry(mediaId: $mediaId, status: $status, progress: $progress, score: $score, completedAt: $completedAt) {
            id
          }
        }
        """;

    /// <summary>Fetched once per push/pull (docs/superpowers/specs/2026-09-18-per-tracker-score-
    /// and-finish-date-design.md) - a user could change their AniList display format on AniList's
    /// own site between syncs, so this is never persisted locally.</summary>
    private const string GetScoreFormatQuery = "query { Viewer { mediaListOptions { scoreFormat } } }";

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
              score
              completedAt { year month day }
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

    /// <summary>Stores the pasted-back token - sanitized first (<see cref="TrackerCredentialSanitizer.SanitizeFragmentToken"/>)
    /// since there's no exchange/validation call for the implicit grant to catch a bad paste early;
    /// a raw, uncleaned paste otherwise fails silently at push time with AniList's generic "Invalid
    /// token" 400, indistinguishable from a genuinely revoked token.</summary>
    public static void CompleteConnect(PaperbunkrDbContext context, string accessToken) =>
        CredentialStore.Set(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken, TrackerCredentialSanitizer.SanitizeFragmentToken(accessToken));

    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken) =>
        (await PushEntryDetailedAsync(context, link, payload, cancellationToken).ConfigureAwait(false)).Success;

    /// <summary>Same as <see cref="PushEntryAsync"/> but returns AniList's real error body instead
    /// of collapsing every failure into a bare <see langword="false"/> - extends the detailed-error
    /// pattern already shipped this session for MangaBaka/MangaDex to this adapter
    /// (docs/superpowers/specs/2026-09-18-per-tracker-score-and-finish-date-design.md).</summary>
    public async Task<(bool Success, string? ErrorDetail)> PushEntryDetailedAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? accessToken = CredentialStore.Get(context, nameof(TrackingService.AniList), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return (false, "Not connected - no access token saved.");
        }

        if (!int.TryParse(link.ExternalId, out int mediaId))
        {
            return (false, $"Invalid AniList media id: {link.ExternalId}");
        }

        string status = AniListStatusMapper.ToMediaListStatus(payload.Status);

        // Dictionary, not an anonymous object - omitting a key (rather than sending it as JSON
        // null) leaves that field untouched on AniList's side, so an ordinary status/progress-only
        // sync (payload.UpdateScore/UpdateFinishDate both false by default) never risks clearing a
        // score/date the user set independently, matching Shikimori's own same fix in this file's
        // sibling adapter. `progress` itself also needs this same omit-don't-null treatment -
        // confirmed live 2026-09-18 that sending explicit JSON `"progress":null` (which happens
        // whenever payload.ChapterProgress is null - e.g. the per-tracker panel's push when nothing
        // was pulled/entered yet) gets rejected with a real 400 "The progress must be an integer",
        // not silently accepted as "leave unchanged" the way completedAt/score's own null handling
        // works elsewhere in this file.
        var variables = new Dictionary<string, object?> { ["mediaId"] = mediaId, ["status"] = status };
        if (payload.ChapterProgress is int progress)
        {
            variables["progress"] = progress;
        }
        if (payload.UpdateScore)
        {
            double score = 0;
            if (payload.Score is decimal localScore and > 0)
            {
                string format = await GetScoreFormatAsync(accessToken, cancellationToken).ConfigureAwait(false);
                score = AniListScoreFormatMapper.ToAniListScore(localScore, format);
            }

            variables["score"] = score;
        }

        if (payload.UpdateFinishDate)
        {
            variables["completedAt"] = payload.FinishDate is DateOnly date
                ? new { year = date.Year, month = date.Month, day = date.Day }
                : new { year = (int?)null, month = (int?)null, day = (int?)null };
        }

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { query = SaveMediaListEntryMutation, variables }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Network error: {ex.Message}");
        }

        using (response)
        {
            string rawBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"{(int)response.StatusCode}: {rawBody}");
            }

            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<AniListMutationResponse>(rawBody);
                return parsed?.Errors is { Count: > 0 }
                    ? (false, rawBody)
                    : (true, null);
            }
            catch (System.Text.Json.JsonException)
            {
                return (false, $"Unexpected response body: {rawBody}");
            }
        }
    }

    /// <summary>Fetches the viewer's own AniList score-display format - never persisted, a fresh
    /// read every call since it can change on AniList's own site between syncs. Defaults to
    /// <c>POINT_10</c> (the format-conversion mapper's own safe fallback) on any failure, rather
    /// than blocking the whole push over a secondary query.</summary>
    private async Task<string> GetScoreFormatAsync(string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { query = GetScoreFormatQuery, variables = new { } }),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return "POINT_10";
            }

            var parsed = await response.Content.ReadFromJsonAsync<AniListScoreFormatResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return parsed?.Data?.Viewer?.MediaListOptions?.ScoreFormat ?? "POINT_10";
        }
        catch (HttpRequestException)
        {
            return "POINT_10";
        }
        catch (System.Text.Json.JsonException)
        {
            return "POINT_10";
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
            if (entry is null)
            {
                return null;
            }

            decimal? score = null;
            if (entry.Score is double raw and > 0)
            {
                string format = await GetScoreFormatAsync(accessToken, cancellationToken).ConfigureAwait(false);
                score = AniListScoreFormatMapper.FromAniListScore(raw, format);
            }

            DateOnly? finishDate = entry.CompletedAt is { Year: int y, Month: int m, Day: int d } ? new DateOnly(y, m, d) : null;

            return new TrackerRemoteEntry(AniListStatusMapper.FromMediaListStatus(entry.Status), entry.Progress, score, finishDate);
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

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("completedAt")]
    public AniListFuzzyDateDto? CompletedAt { get; set; }
}

internal sealed class AniListFuzzyDateDto
{
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("month")]
    public int? Month { get; set; }

    [JsonPropertyName("day")]
    public int? Day { get; set; }
}

internal sealed class AniListScoreFormatResponse
{
    [JsonPropertyName("data")]
    public AniListScoreFormatData? Data { get; set; }
}

internal sealed class AniListScoreFormatData
{
    [JsonPropertyName("Viewer")]
    public AniListViewerDto? Viewer { get; set; }
}

internal sealed class AniListViewerDto
{
    [JsonPropertyName("mediaListOptions")]
    public AniListMediaListOptionsDto? MediaListOptions { get; set; }
}

internal sealed class AniListMediaListOptionsDto
{
    [JsonPropertyName("scoreFormat")]
    public string? ScoreFormat { get; set; }
}

/// <summary>Converts Paperbunkr's canonical 0-5 <see cref="Series.Rating"/> scale to/from AniList's
/// own per-user configurable <c>scoreFormat</c> (docs/superpowers/specs/2026-09-18-per-tracker-
/// score-and-finish-date-design.md) - sending a raw number blind would silently misdisplay on
/// AniList's own site.</summary>
public static class AniListScoreFormatMapper
{
    public static double ToAniListScore(decimal localScore0To5, string format) => format switch
    {
        "POINT_100" => (double)localScore0To5 * 20,
        "POINT_10_DECIMAL" => (double)localScore0To5 * 2,
        "POINT_10" => Math.Round((double)localScore0To5 * 2),
        "POINT_5" => Math.Round((double)localScore0To5),
        "POINT_3" => Math.Round((double)localScore0To5 / 5 * 3),
        _ => Math.Round((double)localScore0To5 * 2), // unrecognized format - fall back to POINT_10's shape
    };

    public static decimal FromAniListScore(double aniListScore, string format) => format switch
    {
        "POINT_100" => (decimal)(aniListScore / 20),
        "POINT_10_DECIMAL" or "POINT_10" => (decimal)(aniListScore / 2),
        "POINT_5" => (decimal)aniListScore,
        "POINT_3" => (decimal)(aniListScore / 3 * 5),
        _ => (decimal)(aniListScore / 2),
    };
}
