using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
/// Shikimori adapter (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md) - search
/// is Shikimori's public content API (no auth); push uses the <c>user_rates</c> endpoint via
/// Doorkeeper OAuth2, a confidential-client model needing both a Client ID and Secret. Field names
/// for <c>user_rates</c> were not directly re-confirmed against the live doc during research (network
/// access to shikimori.one/.io failed repeatedly) - flagged in the design spec as worth re-verifying;
/// implemented here from the best-available secondary-source consensus.
/// </summary>
public sealed class ShikimoriTrackerAdapter : ITrackerSearchProvider, ITrackerAdapter, ITrackerDetailedPush
{
    private const string ApiBase = "https://shikimori.one/api";
    private const string AuthorizeEndpoint = "https://shikimori.one/oauth/authorize";
    private const string TokenEndpoint = "https://shikimori.one/oauth/token";
    private const string OobRedirectUri = "urn:ietf:wg:oauth:2.0:oob";

    private readonly HttpClient _httpClient;

    public ShikimoriTrackerAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TrackingService Service => TrackingService.Shikimori;

    public static string BuildAuthorizationUrl(string clientId) =>
        $"{AuthorizeEndpoint}?client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(OobRedirectUri)}" +
        "&response_type=code&scope=user_rates";

    public async Task<bool> CompleteConnectAsync(PaperbunkrDbContext context, string clientId, string clientSecret, string pastedCode, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = pastedCode,
            ["redirect_uri"] = OobRedirectUri,
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken).ConfigureAwait(false);
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

            ShikimoriTokenResponse? token;
            try
            {
                token = await response.Content.ReadFromJsonAsync<ShikimoriTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return false;
            }

            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                return false;
            }

            CredentialStore.Set(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken, token.AccessToken);
            if (!string.IsNullOrEmpty(token.RefreshToken))
            {
                CredentialStore.Set(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthRefreshToken, token.RefreshToken);
            }

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
            response = await _httpClient.GetAsync($"{ApiBase}/mangas?search={Uri.EscapeDataString(query)}&limit=10", cancellationToken).ConfigureAwait(false);
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

            List<ShikimoriMangaDto>? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<List<ShikimoriMangaDto>>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                throw new MetadataProviderUnavailableException();
            }

            if (parsed is null)
            {
                throw new MetadataProviderUnavailableException();
            }

            return parsed
                .Select(m => new MetadataSearchResult(
                    m.Id.ToString(),
                    m.Name ?? m.Russian ?? string.Empty,
                    string.IsNullOrEmpty(m.Url) ? null : $"https://shikimori.one{m.Url}"))
                .ToList();
        }
    }

    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken) =>
        (await PushEntryDetailedAsync(context, link, payload, cancellationToken).ConfigureAwait(false)).Success;

    /// <summary>Same as <see cref="PushEntryAsync"/> but returns Shikimori's real error body
    /// instead of collapsing every failure into a bare <see langword="false"/> - extends the
    /// detailed-error pattern already shipped this session for MangaBaka/MangaDex to this adapter.
    /// <c>score</c> rides the same <c>user_rates</c> call as status/progress - confirmed live via
    /// the real fetched Shikimori API doc (docs/superpowers/specs/2026-09-18-per-tracker-score-and-
    /// finish-date-design.md); no finish-date field exists on this service at all, confirmed
    /// absent from that same doc.</summary>
    public async Task<(bool Success, string? ErrorDetail)> PushEntryDetailedAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? accessToken = CredentialStore.Get(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return (false, "Not connected - no access token saved.");
        }

        var existingRate = await FindExistingRateAsync(accessToken, link.ExternalId, cancellationToken).ConfigureAwait(false);
        int? existingRateId = existingRate?.Id;

        // Dictionary, not an anonymous object, so a null/unset local rating omits `score` entirely
        // rather than forcing it to 0 - a user's independently-set Shikimori score shouldn't get
        // silently cleared just because Paperbunkr hasn't rated this series yet. Same restraint as
        // MyAnimeList's own "never send an unscored 0" choice (Step 5, this same design doc).
        var userRate = new Dictionary<string, object?>
        {
            ["target_id"] = int.TryParse(link.ExternalId, out int targetId) ? targetId : 0,
            ["target_type"] = "Manga",
            ["status"] = ShikimoriStatusMapper.ToUserRateStatus(payload.Status),
            ["chapters"] = payload.ChapterProgress,
        };
        if (payload.UpdateScore)
        {
            userRate["score"] = payload.Score is decimal localScore and > 0 ? (int)Math.Round((double)localScore * 2) : 0;
        }

        var body = new { user_rate = userRate };

        var request = existingRateId is int id
            ? new HttpRequestMessage(HttpMethod.Put, $"{ApiBase}/v2/user_rates/{id}") { Content = JsonContent.Create(body) }
            : new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/v2/user_rates") { Content = JsonContent.Create(body) };
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
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "Request timed out.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            string rawBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (false, $"{(int)response.StatusCode}: {rawBody}");
        }
    }

    public async Task<TrackerRemoteEntry?> GetEntryAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken)
    {
        string? accessToken = CredentialStore.Get(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        var rate = await FindExistingRateAsync(accessToken, link.ExternalId, cancellationToken).ConfigureAwait(false);
        if (rate is null)
        {
            return null;
        }

        decimal? score = rate.Score is int s and > 0 ? s / 2m : null;
        return new TrackerRemoteEntry(ShikimoriStatusMapper.FromUserRateStatus(rate.Status), rate.Chapters, score);
    }

    /// <summary>Looks up the existing <c>user_rate</c> for this manga, if any - <see cref="PushEntryAsync"/>
    /// uses just its id (to decide PUT vs POST), <see cref="GetEntryAsync"/> uses its status/chapters
    /// too. One shared lookup rather than two near-identical HTTP round trips. Returns null (treat as
    /// "create"/"nothing to compare") on any failure - matches every other adapter's "unavailable, not
    /// exceptional" idiom.</summary>
    private async Task<ShikimoriUserRateDto?> FindExistingRateAsync(string accessToken, string targetExternalId, CancellationToken cancellationToken)
    {
        var whoamiRequest = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/users/whoami");
        whoamiRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        string? userId;
        try
        {
            using var whoamiResponse = await _httpClient.SendAsync(whoamiRequest, cancellationToken).ConfigureAwait(false);
            if (!whoamiResponse.IsSuccessStatusCode)
            {
                return null;
            }

            var whoami = await whoamiResponse.Content.ReadFromJsonAsync<ShikimoriWhoamiDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
            userId = whoami?.Id.ToString();
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        try
        {
            using var rateResponse = await _httpClient.GetAsync(
                $"{ApiBase}/v2/user_rates?user_id={userId}&target_id={targetExternalId}&target_type=Manga",
                cancellationToken).ConfigureAwait(false);
            if (!rateResponse.IsSuccessStatusCode)
            {
                return null;
            }

            var rates = await rateResponse.Content.ReadFromJsonAsync<List<ShikimoriUserRateDto>>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return rates?.FirstOrDefault();
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
}

/// <summary><see cref="ReadingStatus"/> -&gt; Shikimori's <c>user_rates</c> status string - clean
/// 1:1, no lossy case, one of two services (with AniList) that has a genuine 6-state match
/// (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md).</summary>
public static class ShikimoriStatusMapper
{
    public static string ToUserRateStatus(ReadingStatus status) => status switch
    {
        ReadingStatus.Planned => "planned",
        ReadingStatus.Reading => "watching",
        ReadingStatus.Completed => "completed",
        ReadingStatus.Paused => "on_hold",
        ReadingStatus.Dropped => "dropped",
        ReadingStatus.ReReading => "rewatching",
        _ => "planned",
    };

    /// <summary>Reverse of <see cref="ToUserRateStatus"/> - clean 1:1 both ways, same as the push
    /// direction's own doc comment already notes for this service.</summary>
    public static ReadingStatus FromUserRateStatus(string? status) => status switch
    {
        "planned" => ReadingStatus.Planned,
        "watching" => ReadingStatus.Reading,
        "completed" => ReadingStatus.Completed,
        "on_hold" => ReadingStatus.Paused,
        "dropped" => ReadingStatus.Dropped,
        "rewatching" => ReadingStatus.ReReading,
        _ => ReadingStatus.Unknown,
    };
}

internal sealed class ShikimoriTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}

internal sealed class ShikimoriMangaDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("russian")]
    public string? Russian { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

internal sealed class ShikimoriWhoamiDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

internal sealed class ShikimoriUserRateDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("chapters")]
    public int? Chapters { get; set; }

    [JsonPropertyName("score")]
    public int? Score { get; set; }
}
