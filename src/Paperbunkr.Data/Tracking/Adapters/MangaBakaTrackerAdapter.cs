using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tracking.Adapters;

/// <summary>
/// MangaBaka (mangabaka.org) push-only adapter (docs/superpowers/specs/2026-08-23-mangabaka-
/// tracker-adapter-design.md) - corrects this session's own earlier conclusion
/// (`2026-08-23-mangabaka-metadata-provider-design.md`) that MangaBaka "cannot be a tracker": a
/// real authenticated personal-library API exists (`PUT/PATCH /v1/my/library/{series_id}`), just
/// not reachable from the unauthenticated search/get endpoints <see cref="Metadata.MangaBakaMetadataProvider"/>
/// calls. PAT-authenticated, no OAuth flow, same reasoning <see cref="BangumiTrackerAdapter"/>'s own
/// doc comment gives for skipping Bangumi's OAuth - simpler and no flakiness to work around here,
/// just a smaller implementation surface. Stored as a plain <see cref="CredentialKind.ApiKey"/>.
/// Search is not duplicated here - <see cref="Metadata.MangaBakaMetadataProvider"/> already
/// implements <see cref="ITrackerSearchProvider"/> for that half.
/// </summary>
public sealed class MangaBakaTrackerAdapter : ITrackerAdapter, ITrackerDetailedPush
{
    private const string ApiBase = "https://api.mangabaka.org/v1";

    private readonly HttpClient _httpClient;

    public MangaBakaTrackerAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TrackingService Service => TrackingService.MangaBaka;

    /// <summary>Sanitized first (<see cref="TrackerCredentialSanitizer.SanitizeToken"/>) - same
    /// "no exchange call to catch a bad paste early" reasoning as <see cref="BangumiTrackerAdapter.CompleteConnect"/>.</summary>
    public static void CompleteConnect(PaperbunkrDbContext context, string personalAccessToken) =>
        CredentialStore.Set(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey, TrackerCredentialSanitizer.SanitizeToken(personalAccessToken));

    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken) =>
        (await PushEntryDetailedAsync(context, link, payload, cancellationToken).ConfigureAwait(false)).Success;

    /// <summary>Same as <see cref="PushEntryAsync"/> but returns MangaBaka's real error body
    /// instead of collapsing every failure into a bare <see langword="false"/> - added after a live
    /// "sync failed" report gave no way to tell an invalid/unscoped PAT apart from a malformed
    /// request or a server-side rejection.</summary>
    public async Task<(bool Success, string? ErrorDetail)> PushEntryDetailedAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? token = CredentialStore.Get(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey);
        if (string.IsNullOrEmpty(token))
        {
            return (false, "Not connected - no PAT saved.");
        }

        // Dictionary, not an anonymous object - an omitted key leaves that field untouched on
        // MangaBaka's side, so an ordinary status/progress-only sync (UpdateScore/UpdateFinishDate
        // both default false) never sends rating/finish_date as null and clobbers a value the user
        // set independently there.
        var body = new Dictionary<string, object?>
        {
            ["state"] = MangaBakaLibraryStateMapper.ToState(payload.Status),
            ["progress_chapter"] = payload.ChapterProgress,
        };
        if (payload.UpdateScore)
        {
            body["rating"] = payload.Score is decimal localScore and > 0 ? (int?)Math.Round((double)localScore * 20) : null;
        }

        if (payload.UpdateFinishDate)
        {
            body["finish_date"] = payload.FinishDate?.ToString("yyyy-MM-dd");
        }

        var (ok, statusCode, rawBody) = await SendLibraryRequestAsync(HttpMethod.Put, link.ExternalId, token, body, cancellationToken).ConfigureAwait(false);
        if (ok)
        {
            return (true, null);
        }

        // PUT only updates an existing library entry - confirmed live 2026-09-18 (a real PAT got a
        // real 404 NOT_FOUND for a series never added to that account's MangaBaka library before,
        // not the 401 a bad/unscoped PAT would give). POST creates it; retry with that once, same
        // "create-then-update" two-step MangaUpdatesTrackerAdapter already uses for its own list-
        // entry endpoint.
        if (statusCode == HttpStatusCode.NotFound)
        {
            (ok, statusCode, rawBody) = await SendLibraryRequestAsync(HttpMethod.Post, link.ExternalId, token, body, cancellationToken).ConfigureAwait(false);
            if (ok)
            {
                return (true, null);
            }
        }

        return (false, statusCode is { } code ? $"{(int)code}: {rawBody}" : rawBody);
    }

    private async Task<(bool Success, HttpStatusCode? StatusCode, string Body)> SendLibraryRequestAsync(HttpMethod method, string externalId, string token, object body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, $"{ApiBase}/my/library/{externalId}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-api-key", token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return (false, null, $"Network error: {ex.Message}");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return (true, response.StatusCode, string.Empty);
            }

            string rawBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (false, response.StatusCode, rawBody.Length > 300 ? rawBody[..300] + "..." : rawBody);
        }
    }

    /// <summary>`GET /v1/my/library/{series_id}` - documented in `docs/mangabaka-metadata-ui-
    /// research.md` finding 19 (a real endpoint, confirmed via the user-supplied OpenAPI spec, never
    /// previously called since `ITrackerAdapter` was push-only until this pass). Assumed to wrap its
    /// body in the same `{ data: {...} }` envelope every other MangaBaka endpoint this codebase calls
    /// uses (`/v1/series/search`, `/v1/series/{id}`) - not independently re-confirmed against a live
    /// PAT this session, same "first thing to verify once a real PAT exists" caveat this file's own
    /// `PUT`-upsert assumption already carries.</summary>
    public async Task<TrackerRemoteEntry?> GetEntryAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken)
    {
        string? token = CredentialStore.Get(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey);
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/my/library/{link.ExternalId}");
        request.Headers.Add("x-api-key", token);

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

            MangaBakaLibraryEntryResponse? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<MangaBakaLibraryEntryResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return null;
            }

            var entry = parsed?.Data;
            if (entry?.State is null)
            {
                return null;
            }

            decimal? score = entry.Rating is int rating and > 0 ? rating / 20m : null;
            DateOnly? finishDate = DateOnly.TryParse(entry.FinishDate, out var parsedDate) ? parsedDate : null;
            return new TrackerRemoteEntry(MangaBakaLibraryStateMapper.FromState(entry.State), entry.ProgressChapter, score, finishDate);
        }
    }
}

/// <summary>
/// <see cref="ReadingStatus"/> -&gt; MangaBaka's `state` string enum. Lossless 1:1 mapping - both
/// are 7-value enums that line up directly, unlike <see cref="BangumiCollectionTypeMapper"/>'s own
/// forced collapse of <see cref="ReadingStatus.ReReading"/> into plain reading (Bangumi's service
/// only has 5 states; MangaBaka has a dedicated "rereading" state).
/// </summary>
public static class MangaBakaLibraryStateMapper
{
    public static string ToState(ReadingStatus status) => status switch
    {
        ReadingStatus.Planned => "plan_to_read",
        ReadingStatus.Reading => "reading",
        ReadingStatus.Completed => "completed",
        ReadingStatus.Paused => "paused",
        ReadingStatus.Dropped => "dropped",
        ReadingStatus.ReReading => "rereading",
        _ => "considering", // ReadingStatus.Unknown - MangaBaka's own "not yet decided" bucket, the closest semantic match.
    };

    /// <summary>Reverse of <see cref="ToState"/> - lossless both ways per this file's own summary
    /// (both are 7-value enums that line up 1:1).</summary>
    public static ReadingStatus FromState(string? state) => state switch
    {
        "plan_to_read" => ReadingStatus.Planned,
        "reading" => ReadingStatus.Reading,
        "completed" => ReadingStatus.Completed,
        "paused" => ReadingStatus.Paused,
        "dropped" => ReadingStatus.Dropped,
        "rereading" => ReadingStatus.ReReading,
        "considering" => ReadingStatus.Unknown,
        _ => ReadingStatus.Unknown,
    };
}

internal sealed class MangaBakaLibraryEntryResponse
{
    [JsonPropertyName("data")]
    public MangaBakaLibraryEntryDto? Data { get; set; }
}

internal sealed class MangaBakaLibraryEntryDto
{
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("progress_chapter")]
    public int? ProgressChapter { get; set; }

    [JsonPropertyName("rating")]
    public int? Rating { get; set; }

    [JsonPropertyName("finish_date")]
    public string? FinishDate { get; set; }
}
