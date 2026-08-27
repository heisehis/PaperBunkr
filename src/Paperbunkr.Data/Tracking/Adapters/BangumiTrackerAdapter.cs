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
/// Bangumi (bgm.tv) adapter (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md) -
/// deliberately no OAuth flow at all, unlike the other three. Bangumi's OAuth token exchange is
/// documented (community reports) as returning HTTP 500 roughly 10-20% of the time, and its
/// app-registration/redirect requirements were never confirmed even after direct research - a
/// Personal Access Token the user generates on bgm.tv's own site and pastes in sidesteps the flaky
/// endpoint and the unconfirmed unknowns entirely. Stored as a plain <see cref="CredentialKind.ApiKey"/>,
/// not an OAuth token kind.
/// </summary>
public sealed class BangumiTrackerAdapter : ITrackerSearchProvider, ITrackerAdapter
{
    private const string ApiBase = "https://api.bgm.tv/v0";

    // Bangumi's API guidelines require a descriptive User-Agent identifying the calling application.
    private const string UserAgent = "Paperbunkr/1.0 (+https://github.com/paperbunkr/paperbunkr)";

    private readonly HttpClient _httpClient;

    public BangumiTrackerAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TrackingService Service => TrackingService.Bangumi;

    public static void CompleteConnect(PaperbunkrDbContext context, string personalAccessToken) =>
        CredentialStore.Set(context, nameof(TrackingService.Bangumi), CredentialKind.ApiKey, personalAccessToken);

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<MetadataSearchResult>();
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/search/subjects")
        {
            Content = JsonContent.Create(new { keyword = query, filter = new { type = new[] { 1 } } }),
        };
        request.Headers.UserAgent.ParseAdd(UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new MetadataProviderUnavailableException();
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new MetadataProviderUnavailableException();
            }

            BangumiSearchResponse? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<BangumiSearchResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                throw new MetadataProviderUnavailableException();
            }

            var data = parsed?.Data ?? throw new MetadataProviderUnavailableException();

            return data
                .Select(s => new MetadataSearchResult(
                    s.Id.ToString(),
                    s.NameCn is { Length: > 0 } ? s.NameCn : s.Name ?? string.Empty,
                    $"https://bgm.tv/subject/{s.Id}"))
                .ToList();
        }
    }

    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? token = CredentialStore.Get(context, nameof(TrackingService.Bangumi), CredentialKind.ApiKey);
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var body = new Dictionary<string, object>
        {
            ["type"] = BangumiCollectionTypeMapper.ToCollectionType(payload.Status),
        };
        if (payload.ChapterProgress is int chapters)
        {
            body["ep_status"] = chapters;
        }

        bool succeeded = await SendCollectionRequestAsync(HttpMethod.Post, token, link.ExternalId, body, cancellationToken).ConfigureAwait(false);
        if (!succeeded)
        {
            // Bangumi's own community-reported flakiness isn't limited to token exchange (which this
            // adapter avoids entirely via PAT) - one retry-with-backoff on the actual collection call
            // before surfacing failure, per the design spec's error-handling section.
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            succeeded = await SendCollectionRequestAsync(HttpMethod.Patch, token, link.ExternalId, body, cancellationToken).ConfigureAwait(false);
        }

        return succeeded;
    }

    private async Task<bool> SendCollectionRequestAsync(HttpMethod method, string token, string subjectId, Dictionary<string, object> body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, $"{ApiBase}/users/-/collections/{subjectId}")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

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

/// <summary><see cref="ReadingStatus"/> -&gt; Bangumi's <c>SubjectCollectionType</c> numeric enum.
/// Only 5 states exist on this service at all, shared across every subject type - no re-reading
/// concept anywhere, so <see cref="ReadingStatus.ReReading"/> collapses into <c>Doing</c> (3), the
/// same value as plain <see cref="ReadingStatus.Reading"/>. Permanent, accepted information loss for
/// this service specifically (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md).</summary>
public static class BangumiCollectionTypeMapper
{
    public static int ToCollectionType(ReadingStatus status) => status switch
    {
        ReadingStatus.Planned => 1,   // Wish
        ReadingStatus.Completed => 2, // Done
        ReadingStatus.Reading => 3,   // Doing
        ReadingStatus.Paused => 4,    // OnHold
        ReadingStatus.Dropped => 5,   // Dropped
        ReadingStatus.ReReading => 3, // Doing - collapsed, see summary above
        _ => 1,
    };
}

internal sealed class BangumiSearchResponse
{
    [JsonPropertyName("data")]
    public List<BangumiSubjectDto>? Data { get; set; }
}

internal sealed class BangumiSubjectDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("name_cn")]
    public string? NameCn { get; set; }
}
