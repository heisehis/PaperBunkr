using System;
using System.Net.Http;
using System.Net.Http.Json;
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
public sealed class MangaBakaTrackerAdapter : ITrackerAdapter
{
    private const string ApiBase = "https://api.mangabaka.org/v1";

    private readonly HttpClient _httpClient;

    public MangaBakaTrackerAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TrackingService Service => TrackingService.MangaBaka;

    public static void CompleteConnect(PaperbunkrDbContext context, string personalAccessToken) =>
        CredentialStore.Set(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey, personalAccessToken);

    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? token = CredentialStore.Get(context, nameof(TrackingService.MangaBaka), CredentialKind.ApiKey);
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var body = new
        {
            state = MangaBakaLibraryStateMapper.ToState(payload.Status),
            progress_chapter = payload.ChapterProgress,
        };

        var request = new HttpRequestMessage(HttpMethod.Put, $"{ApiBase}/my/library/{link.ExternalId}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("x-api-key", token);

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
            return response.IsSuccessStatusCode;
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
}
