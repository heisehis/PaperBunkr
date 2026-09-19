using System;
using System.Collections.Generic;
using System.Net;
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
/// MangaDex (mangadex.org) tracker adapter - corrects a stale assumption in
/// <see cref="MangaDexMetadataProvider"/>'s own doc comment ("no authenticated per-user library
/// API"), confirmed wrong live 2026-09-18: <c>GET /manga/status</c>/<c>/user/follows/manga</c> both
/// return <c>401</c> (not <c>404</c>), and <c>auth.mangadex.org</c> is a real Keycloak OIDC server
/// supporting the <c>password</c> grant. Unlike Kitsu's shared hardcoded OAuth client, MangaDex has
/// no public client - confirmed live (a made-up client_id/secret pair returns <c>invalid_client</c>,
/// not a scope/grant error), so each user registers their own "Personal Client" (mangadex.org
/// account settings -&gt; API Clients) and this adapter needs Client ID + Client Secret *and*
/// username/password, unlike every other <see cref="ITrackerAdapter"/> in this file (none combine
/// both). <see cref="ConnectionDialogKind.CredentialWithClient"/> is the new dialog shape this
/// needs.
///
/// <para><b>Deliberately status-only, no chapter-progress push/pull.</b> MangaDex has no numeric
/// "chapter progress" field on any tracker-facing endpoint - progress is tracked by marking
/// individual chapter UUIDs read/unread (<c>POST /manga/{id}/read</c>), which needs a separate
/// chapter-list resolution (<c>/manga/{id}/aggregate</c>) to reconcile "read up to chapter N"
/// against real UUIDs. Not attempted this pass rather than guessed at without a live account to
/// verify against - <see cref="GetEntryAsync"/> always returns a null <c>ChapterProgress</c>, and
/// <see cref="PushEntryAsync"/> never attempts to push one.</para>
/// </summary>
public sealed class MangaDexTrackerAdapter : ITrackerSearchProvider, ITrackerAdapter, ITrackerDetailedPush
{
    private const string ApiBase = "https://api.mangadex.org";
    private const string TokenEndpoint = "https://auth.mangadex.org/realms/mangadex/protocol/openid-connect/token";

    /// <summary>Both <c>auth.mangadex.org</c> and <c>api.mangadex.org</c> reject requests with no
    /// User-Agent header (confirmed live 2026-09-18: a real Personal Client + correct credentials
    /// still got <c>400 "You must set an appropriate User-Agent header"</c> from the token endpoint
    /// with .NET's default, header-less <see cref="HttpClient"/>) - same requirement, same fix
    /// shape as <see cref="BangumiTrackerAdapter"/>'s own <c>UserAgent</c> constant.</summary>
    private const string UserAgent = "Paperbunkr/1.0 (+https://github.com/paperbunkr/paperbunkr)";

    private readonly HttpClient _httpClient;

    public MangaDexTrackerAdapter(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public TrackingService Service => TrackingService.MangaDex;

    /// <summary>Satisfies <see cref="ITrackerSearchProvider"/> by delegating to the existing
    /// unauthenticated <see cref="MangaDexMetadataProvider"/> - search itself needs no bearer
    /// token, confirmed by that class's own real, already-tested implementation.</summary>
    public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
        MangaDexMetadataProvider.Shared.SearchAsync(query, cancellationToken);

    /// <summary>OAuth2 password grant against the user's own registered Personal Client.
    /// <paramref name="clientId"/>/<paramref name="clientSecret"/> are persisted by the caller
    /// regardless of outcome (same "worth keeping for a retry" precedent as Shikimori's own
    /// Client ID/Secret) - only the resulting tokens are this method's own concern.</summary>
    public async Task<bool> CompleteConnectAsync(PaperbunkrDbContext context, string clientId, string clientSecret, string username, string password, CancellationToken cancellationToken) =>
        (await CompleteConnectDetailedAsync(context, clientId, clientSecret, username, password, cancellationToken).ConfigureAwait(false)).Success;

    /// <summary>
    /// Same as <see cref="CompleteConnectAsync"/> but returns the real server-reported error
    /// (Keycloak's own <c>error</c>/<c>error_description</c> body, e.g. <c>invalid_grant</c> for a
    /// bad password vs <c>unauthorized_client</c> for a Personal Client that doesn't have "Direct
    /// Access Grants" enabled) instead of collapsing every failure into a bare <see langword="false"/> -
    /// added after a live "all fields correct, still won't connect" report where the generic
    /// failure gave no way to tell a wrong password apart from a client-configuration problem.
    /// </summary>
    public async Task<(bool Success, string? ErrorDetail)> CompleteConnectDetailedAsync(PaperbunkrDbContext context, string clientId, string clientSecret, string username, string password, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        request.Headers.UserAgent.ParseAdd(UserAgent);

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
                return (false, DescribeError(response.StatusCode, rawBody));
            }

            MangaDexOAuthResponse? token;
            try
            {
                token = JsonSerializer.Deserialize<MangaDexOAuthResponse>(rawBody);
            }
            catch (JsonException)
            {
                return (false, $"Unexpected response body: {Truncate(rawBody)}");
            }

            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                return (false, $"Response had no access_token: {Truncate(rawBody)}");
            }

            CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, token.AccessToken);
            if (!string.IsNullOrEmpty(token.RefreshToken))
            {
                CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthRefreshToken, token.RefreshToken);
            }

            return (true, null);
        }
    }

    private static string DescribeError(HttpStatusCode statusCode, string rawBody)
    {
        try
        {
            var error = JsonSerializer.Deserialize<MangaDexOAuthErrorResponse>(rawBody);
            if (error?.Error is not null)
            {
                return $"{(int)statusCode} {error.Error}: {error.ErrorDescription ?? "(no description)"}";
            }
        }
        catch (JsonException)
        {
        }

        return $"{(int)statusCode}: {Truncate(rawBody)}";
    }

    private static string Truncate(string value) => value.Length > 300 ? value[..300] + "..." : value;

    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken) =>
        (await PushEntryDetailedAsync(context, link, payload, cancellationToken).ConfigureAwait(false)).Success;

    /// <summary>Same as <see cref="PushEntryAsync"/> but returns the real failure reason - added
    /// alongside the token-refresh fix below (a live "reports success, mangadex.org itself never
    /// updates" report traced to the access token being silently expired by sync time, a 401 that
    /// <see cref="PushEntryAsync"/>'s old bare-bool contract couldn't distinguish from any other
    /// failure).</summary>
    public async Task<(bool Success, string? ErrorDetail)> PushEntryDetailedAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? token = CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(token))
        {
            return (false, "Not connected - no access token saved.");
        }

        (bool ok, string? error, string? refreshedToken) = await TryPushOnceAsync(context, link, payload, token, cancellationToken).ConfigureAwait(false);
        if (!ok && refreshedToken is not null)
        {
            // 401 on the first attempt - the stored access token is likely expired (Keycloak
            // default lifetimes are short, e.g. 15 minutes, and a sync rarely runs immediately
            // after connect). Refresh once and retry, rather than surfacing a confusing
            // "unauthorized" for what's really just a stale token.
            CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, refreshedToken);
            token = refreshedToken;
            (ok, error, _) = await TryPushOnceAsync(context, link, payload, refreshedToken, cancellationToken).ConfigureAwait(false);
        }

        if (!ok)
        {
            return (false, error);
        }

        if (!payload.UpdateScore)
        {
            // Rating lives on a separate resource (POST/DELETE /rating/{id}) - only touch it when
            // this push explicitly means to; otherwise an ordinary status/progress-only sync
            // (UpdateScore defaults false) would issue a real DELETE and wipe a rating the user set
            // independently on mangadex.org, since payload.Score is always null for that call shape.
            return (true, null);
        }

        var (ratingOk, ratingError) = await PushRatingAsync(context, link, payload.Score, token, cancellationToken).ConfigureAwait(false);
        return ratingOk ? (true, null) : (false, ratingError);
    }

    private async Task<(bool Success, string? ErrorDetail)> PushRatingAsync(PaperbunkrDbContext context, TrackingLink link, decimal? localScore, string accessToken, CancellationToken cancellationToken)
    {
        bool hasScore = localScore is decimal s and > 0;
        var request = hasScore
            ? new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/rating/{link.ExternalId}")
              {
                  Content = JsonContent.Create(new { rating = Math.Clamp((int)Math.Round((double)localScore!.Value * 2), 1, 10) }),
              }
            : new HttpRequestMessage(HttpMethod.Delete, $"{ApiBase}/rating/{link.ExternalId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(UserAgent);

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
            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                string? refreshed = await TryRefreshAccessTokenAsync(context, cancellationToken).ConfigureAwait(false);
                if (refreshed is null)
                {
                    return (false, "Session expired and refresh failed - reconnect MangaDex in Preferences.");
                }

                CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, refreshed);
                return await PushRatingAsync(context, link, localScore, refreshed, cancellationToken).ConfigureAwait(false);
            }

            string rawBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (false, DescribeError(response.StatusCode, rawBody));
        }
    }

    private async Task<(bool Success, string? ErrorDetail, string? RefreshedTokenToRetryWith)> TryPushOnceAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/manga/{link.ExternalId}/status")
        {
            Content = JsonContent.Create(new { status = MangaDexStatusMapper.ToStatus(payload.Status) }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Network error: {ex.Message}", null);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return (true, null, null);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                string? refreshed = await TryRefreshAccessTokenAsync(context, cancellationToken).ConfigureAwait(false);
                if (refreshed is not null)
                {
                    return (false, null, refreshed);
                }

                return (false, "Session expired and refresh failed - reconnect MangaDex in Preferences.", null);
            }

            string rawBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return (false, DescribeError(response.StatusCode, rawBody), null);
        }
    }

    /// <summary><see cref="TrackerRemoteEntry.ChapterProgress"/> is always null - see this class's
    /// own doc comment on why chapter-level progress isn't wired up. Same expired-token refresh-
    /// and-retry as <see cref="PushEntryDetailedAsync"/>.</summary>
    public async Task<TrackerRemoteEntry?> GetEntryAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken)
    {
        string? token = CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var (entry, unauthorized) = await TryGetOnceAsync(link, token, cancellationToken).ConfigureAwait(false);
        if (unauthorized)
        {
            string? refreshed = await TryRefreshAccessTokenAsync(context, cancellationToken).ConfigureAwait(false);
            if (refreshed is null)
            {
                return null;
            }

            CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, refreshed);
            token = refreshed;
            (entry, _) = await TryGetOnceAsync(link, refreshed, cancellationToken).ConfigureAwait(false);
        }

        if (entry is null)
        {
            return null;
        }

        decimal? score = await FetchRatingAsync(link, token, cancellationToken).ConfigureAwait(false);
        return entry with { Score = score };
    }

    private async Task<decimal?> FetchRatingAsync(TrackingLink link, string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/rating?manga[]={link.ExternalId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var parsed = await response.Content.ReadFromJsonAsync<MangaDexRatingResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            var rating = parsed?.Ratings?.TryGetValue(link.ExternalId, out var entry) == true ? entry : null;
            return rating?.Rating is int r and > 0 ? r / 2m : null;
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

    private async Task<(TrackerRemoteEntry? Entry, bool Unauthorized)> TryGetOnceAsync(TrackingLink link, string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/manga/{link.ExternalId}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.ParseAdd(UserAgent);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return (null, true);
            }

            if (!response.IsSuccessStatusCode)
            {
                return (null, false);
            }

            var parsed = await response.Content.ReadFromJsonAsync<MangaDexStatusResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            var result = parsed?.Status is null ? null : new TrackerRemoteEntry(MangaDexStatusMapper.FromStatus(parsed.Status), ChapterProgress: null);
            return (result, false);
        }
        catch (HttpRequestException)
        {
            return (null, false);
        }
        catch (JsonException)
        {
            return (null, false);
        }
    }

    /// <summary>Uses the stored refresh_token + the same Client ID/Secret persisted at connect
    /// time (<see cref="CompleteConnectDetailedAsync"/>) to mint a fresh access token, without
    /// needing the user's password again. Persists the new access/refresh tokens itself on
    /// success (the caller only receives the access token back, to retry its own in-flight
    /// request with).</summary>
    private async Task<string?> TryRefreshAccessTokenAsync(PaperbunkrDbContext context, CancellationToken cancellationToken)
    {
        string? refreshToken = CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthRefreshToken);
        string? clientId = CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthClientId);
        string? clientSecret = CredentialStore.Get(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthClientSecret);
        if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            return null;
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        };

        var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        request.Headers.UserAgent.ParseAdd(UserAgent);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var token = await response.Content.ReadFromJsonAsync<MangaDexOAuthResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                return null;
            }

            CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthAccessToken, token.AccessToken);
            if (!string.IsNullOrEmpty(token.RefreshToken))
            {
                CredentialStore.Set(context, nameof(TrackingService.MangaDex), CredentialKind.OAuthRefreshToken, token.RefreshToken);
            }

            return token.AccessToken;
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

/// <summary><see cref="ReadingStatus"/> -&gt; MangaDex's own status string. Unlike Kitsu/
/// MangaUpdates, MangaDex has a real dedicated <c>re_reading</c> value - no lossy ReReading
/// collapse needed here.</summary>
public static class MangaDexStatusMapper
{
    public static string ToStatus(ReadingStatus status) => status switch
    {
        ReadingStatus.Reading => "reading",
        ReadingStatus.Planned => "plan_to_read",
        ReadingStatus.Completed => "completed",
        ReadingStatus.Paused => "on_hold",
        ReadingStatus.Dropped => "dropped",
        ReadingStatus.ReReading => "re_reading",
        _ => "plan_to_read",
    };

    public static ReadingStatus FromStatus(string? status) => status switch
    {
        "reading" => ReadingStatus.Reading,
        "plan_to_read" => ReadingStatus.Planned,
        "completed" => ReadingStatus.Completed,
        "on_hold" => ReadingStatus.Paused,
        "dropped" => ReadingStatus.Dropped,
        "re_reading" => ReadingStatus.ReReading,
        _ => ReadingStatus.Unknown,
    };
}

internal sealed class MangaDexOAuthResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}

/// <summary>Standard Keycloak/OIDC error body shape, e.g. <c>{"error":"invalid_grant",
/// "error_description":"Invalid user credentials"}</c> or <c>{"error":"unauthorized_client",
/// "error_description":"..."}</c> for a client missing "Direct Access Grants".</summary>
internal sealed class MangaDexOAuthErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

internal sealed class MangaDexStatusResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

internal sealed class MangaDexRatingResponse
{
    [JsonPropertyName("ratings")]
    public Dictionary<string, MangaDexRatingEntryDto>? Ratings { get; set; }
}

internal sealed class MangaDexRatingEntryDto
{
    [JsonPropertyName("rating")]
    public int? Rating { get; set; }
}
