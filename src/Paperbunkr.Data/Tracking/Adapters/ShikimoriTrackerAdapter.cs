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
public sealed class ShikimoriTrackerAdapter : ITrackerSearchProvider, ITrackerAdapter
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

    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? accessToken = CredentialStore.Get(context, nameof(TrackingService.Shikimori), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return false;
        }

        int? existingRateId = await FindExistingRateIdAsync(accessToken, link.ExternalId, cancellationToken).ConfigureAwait(false);

        var body = new
        {
            user_rate = new
            {
                target_id = int.TryParse(link.ExternalId, out int targetId) ? targetId : 0,
                target_type = "Manga",
                status = ShikimoriStatusMapper.ToUserRateStatus(payload.Status),
                chapters = payload.ChapterProgress,
            },
        };

        var request = existingRateId is int id
            ? new HttpRequestMessage(HttpMethod.Put, $"{ApiBase}/v2/user_rates/{id}") { Content = JsonContent.Create(body) }
            : new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/v2/user_rates") { Content = JsonContent.Create(body) };
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
            return response.IsSuccessStatusCode;
        }
    }

    /// <summary>Looks up whether a <c>user_rate</c> already exists for this manga, so
    /// <see cref="PushEntryAsync"/> knows to update it (PUT) rather than create a duplicate (POST).
    /// Returns null (treat as "create") on any failure - matches every other adapter's "unavailable,
    /// not exceptional" idiom.</summary>
    private async Task<int?> FindExistingRateIdAsync(string accessToken, string targetExternalId, CancellationToken cancellationToken)
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
            return rates?.FirstOrDefault()?.Id;
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
}
