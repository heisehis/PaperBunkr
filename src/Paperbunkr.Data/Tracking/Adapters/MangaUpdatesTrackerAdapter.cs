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
/// MangaUpdates (mangaupdates.com) adapter (docs/superpowers/specs/2026-08-23-tracker-write-back-
/// sync-design.md's per-service pattern, extended to the 5th of the 6 originally-scoped services -
/// see docs/tracker-manga-ui-research.md §1.3). Unlike the OAuth2 four and unlike MangaBaka/Bangumi's
/// pasted Personal Access Token, MangaUpdates uses its own username/password -&gt; session-token login
/// (<c>PUT /v1/account/login</c>, confirmed against Mihon's real <c>MangaUpdatesApi.kt</c> source since
/// MangaUpdates publishes no public API reference docs) - the password itself is never stored, only
/// the returned session token, via <see cref="CredentialKind.OAuthAccessToken"/> (semantically a
/// bearer session token, same storage kind AniList/Shikimori use for their own bearer tokens).
/// </summary>
public sealed class MangaUpdatesTrackerAdapter : ITrackerSearchProvider, ITrackerAdapter
{
    private const string ApiBase = "https://api.mangaupdates.com/v1";

    private readonly HttpClient _httpClient;

    public MangaUpdatesTrackerAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TrackingService Service => TrackingService.MangaUpdates;

    public async Task<bool> CompleteConnectAsync(PaperbunkrDbContext context, string username, string password, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PutAsync(
                $"{ApiBase}/account/login",
                JsonContent.Create(new { username, password }),
                cancellationToken).ConfigureAwait(false);
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

            MangaUpdatesLoginResponse? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<MangaUpdatesLoginResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return false;
            }

            string? sessionToken = parsed?.Context?.SessionToken;
            if (string.IsNullOrEmpty(sessionToken))
            {
                return false;
            }

            CredentialStore.Set(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken, sessionToken);
            return true;
        }
    }

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<MetadataSearchResult>();
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(
                $"{ApiBase}/series/search",
                JsonContent.Create(new { search = query }),
                cancellationToken).ConfigureAwait(false);
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

            MangaUpdatesSearchResponse? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<MangaUpdatesSearchResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                throw new MetadataProviderUnavailableException();
            }

            var results = parsed?.Results ?? throw new MetadataProviderUnavailableException();

            return results
                .Select(r => r.Record)
                .Where(r => r is not null)
                .Select(r => new MetadataSearchResult(r!.SeriesId.ToString(), r.Title ?? string.Empty, r.Url))
                .ToList();
        }
    }

    /// <summary>Pushes status + chapter progress. MangaUpdates' own API splits this into two calls -
    /// <c>POST /lists/series</c> to add a series to a list for the first time (no status payload
    /// accepted on that call, confirmed from Mihon's <c>addSeriesToList</c>) and
    /// <c>POST /lists/series/update</c> to set list/chapter on an entry already on some list - so a
    /// not-yet-tracked series costs two requests here, matching the real API's own shape rather than
    /// guessing a combined endpoint exists.</summary>
    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? token = CredentialStore.Get(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        if (!long.TryParse(link.ExternalId, out long seriesId))
        {
            return false;
        }

        long listId = MangaUpdatesListMapper.ToListId(payload.Status);

        var existingEntry = await FindListEntryAsync(token, seriesId, cancellationToken).ConfigureAwait(false);
        if (existingEntry is null)
        {
            bool added = await SendListRequestAsync(
                $"{ApiBase}/lists/series",
                token,
                new[] { new { series = new { id = seriesId }, list_id = listId } },
                cancellationToken).ConfigureAwait(false);
            if (!added)
            {
                return false;
            }
        }

        return await SendListRequestAsync(
            $"{ApiBase}/lists/series/update",
            token,
            new[] { new { series = new { id = seriesId }, list_id = listId, status = new { chapter = payload.ChapterProgress } } },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrackerRemoteEntry?> GetEntryAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken)
    {
        string? token = CredentialStore.Get(context, nameof(TrackingService.MangaUpdates), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(token) || !long.TryParse(link.ExternalId, out long seriesId))
        {
            return null;
        }

        var entry = await FindListEntryAsync(token, seriesId, cancellationToken).ConfigureAwait(false);
        if (entry?.ListId is not long listId)
        {
            return null;
        }

        return new TrackerRemoteEntry(MangaUpdatesListMapper.FromListId(listId), entry.Status?.Chapter);
    }

    /// <summary>404 (or any non-success) reads as "not on any list yet" - matches every other
    /// adapter's "unavailable, not exceptional" idiom rather than throwing on a routine 404. Shared by
    /// <see cref="PushEntryAsync"/> (which only needs to know whether an entry exists, to decide
    /// POST-then-update vs. update-only) and <see cref="GetEntryAsync"/> (which needs the full
    /// list_id/status too) - one HTTP call serves both instead of two near-identical round trips.</summary>
    private async Task<MangaUpdatesListItemDto?> FindListEntryAsync(string token, long seriesId, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/lists/series/{seriesId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<MangaUpdatesListItemDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<bool> SendListRequestAsync(string url, string token, object body, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
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

/// <summary><see cref="ReadingStatus"/> -&gt; MangaUpdates' own list_id (its five named lists: Reading
/// List=0, Wish List=1, Complete List=2, Unfinished List=3, On Hold List=4 - confirmed against Mihon's
/// <c>MangaUpdates.kt</c> companion constants). No dedicated re-reading list exists on this service
/// (Mihon's own <c>getRereadingStatus()</c> returns -1, i.e. unsupported) - collapses into Reading
/// List, same lossy-collapse precedent as <see cref="BangumiCollectionTypeMapper"/>.</summary>
public static class MangaUpdatesListMapper
{
    public static long ToListId(ReadingStatus status) => status switch
    {
        ReadingStatus.Reading => 0L,
        ReadingStatus.Planned => 1L,
        ReadingStatus.Completed => 2L,
        ReadingStatus.Dropped => 3L,
        ReadingStatus.Paused => 4L,
        ReadingStatus.ReReading => 0L,
        _ => 1L,
    };

    /// <summary>Reverse of <see cref="ToListId"/> - a pulled 0 (Reading List) always resolves to plain
    /// <see cref="ReadingStatus.Reading"/>, never ReReading, same "collapsed on the push side already"
    /// precedent as every other lossy-mapped service in this file.</summary>
    public static ReadingStatus FromListId(long listId) => listId switch
    {
        0L => ReadingStatus.Reading,
        1L => ReadingStatus.Planned,
        2L => ReadingStatus.Completed,
        3L => ReadingStatus.Dropped,
        4L => ReadingStatus.Paused,
        _ => ReadingStatus.Unknown,
    };
}

internal sealed class MangaUpdatesListItemDto
{
    [JsonPropertyName("list_id")]
    public long? ListId { get; set; }

    [JsonPropertyName("status")]
    public MangaUpdatesListItemStatusDto? Status { get; set; }
}

internal sealed class MangaUpdatesListItemStatusDto
{
    [JsonPropertyName("chapter")]
    public int? Chapter { get; set; }
}

internal sealed class MangaUpdatesLoginResponse
{
    [JsonPropertyName("context")]
    public MangaUpdatesLoginContext? Context { get; set; }
}

internal sealed class MangaUpdatesLoginContext
{
    [JsonPropertyName("session_token")]
    public string? SessionToken { get; set; }
}

internal sealed class MangaUpdatesSearchResponse
{
    [JsonPropertyName("results")]
    public List<MangaUpdatesSearchResultItem>? Results { get; set; }
}

internal sealed class MangaUpdatesSearchResultItem
{
    [JsonPropertyName("record")]
    public MangaUpdatesRecordDto? Record { get; set; }
}

internal sealed class MangaUpdatesRecordDto
{
    [JsonPropertyName("series_id")]
    public long SeriesId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
