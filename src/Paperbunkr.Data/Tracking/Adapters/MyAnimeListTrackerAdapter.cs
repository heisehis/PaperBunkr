using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tracking.Adapters;

/// <summary>
/// MyAnimeList adapter (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md) -
/// search is a "public" v2 endpoint needing only the user's pasted Client ID (no OAuth token), so it
/// works before "Connect" is completed; push needs the OAuth token from the PKCE exchange below.
/// PKCE is mandatory for MAL and only supports the <c>plain</c> challenge method (no SHA256), so the
/// challenge sent to the authorize URL is the verifier itself, unlike a typical S256 PKCE flow.
/// </summary>
public sealed class MyAnimeListTrackerAdapter : ITrackerSearchProvider, ITrackerAdapter
{
    private const string ApiBase = "https://api.myanimelist.net/v2";
    private const string AuthorizeEndpoint = "https://myanimelist.net/v1/oauth2/authorize";
    private const string TokenEndpoint = "https://myanimelist.net/v1/oauth2/token";

    // No local server actually listens here - MAL's authorize redirect fails to load, but the
    // address bar still carries the `code` query parameter for the user to copy out, matching this
    // app's "no callback server" design for every OAuth flow in this feature.
    private const string RedirectUri = "http://localhost:48197/callback";

    private readonly HttpClient _httpClient;
    private readonly string? _clientId;

    /// <param name="clientId">The user's own pasted Client ID (docs/superpowers/specs/2026-08-23-
    /// tracker-write-back-sync-design.md) - needed for search (via the <c>X-MAL-CLIENT-ID</c> header)
    /// even before an OAuth token exists. Callers read this from <see cref="CredentialStore"/>
    /// themselves and pass it in, since <see cref="ITrackerSearchProvider.SearchAsync"/>'s shared
    /// signature (matching <see cref="IMetadataProvider"/>'s) takes no <see cref="PaperbunkrDbContext"/>.</param>
    public MyAnimeListTrackerAdapter(HttpClient httpClient, string? clientId)
    {
        _httpClient = httpClient;
        _clientId = clientId;
    }

    public TrackingService Service => TrackingService.MyAnimeList;

    /// <summary>43-128 char unreserved-charset string per RFC 7636 - MAL's <c>plain</c> challenge
    /// method sends this same value back as the challenge, so caller must hold onto it until the
    /// token exchange (<see cref="CompleteConnectAsync"/>) completes.</summary>
    public static string GenerateCodeVerifier()
    {
        const string unreserved = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";
        var bytes = RandomNumberGenerator.GetBytes(96);
        return new string(bytes.Select(b => unreserved[b % unreserved.Length]).ToArray());
    }

    public static string BuildAuthorizationUrl(string clientId, string codeVerifier) =>
        $"{AuthorizeEndpoint}?response_type=code&client_id={Uri.EscapeDataString(clientId)}" +
        $"&code_challenge={Uri.EscapeDataString(codeVerifier)}&code_challenge_method=plain" +
        $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}";

    public async Task<bool> CompleteConnectAsync(PaperbunkrDbContext context, string clientId, string codeVerifier, string pastedCode, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = pastedCode,
            ["code_verifier"] = codeVerifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = RedirectUri,
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

            MalTokenResponse? token;
            try
            {
                token = await response.Content.ReadFromJsonAsync<MalTokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return false;
            }

            if (string.IsNullOrEmpty(token?.AccessToken))
            {
                return false;
            }

            CredentialStore.Set(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken, token.AccessToken);
            if (!string.IsNullOrEmpty(token.RefreshToken))
            {
                CredentialStore.Set(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthRefreshToken, token.RefreshToken);
            }

            return true;
        }
    }

    public async Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        // Search needs only the pasted Client ID, not a full OAuth token - confirmed via MAL's v2
        // docs that manga search is a "public" endpoint gated by X-MAL-CLIENT-ID alone.
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<MetadataSearchResult>();
        }

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{ApiBase}/manga?q={Uri.EscapeDataString(query)}&fields=id,title&limit=10");
        request.Headers.Add("X-MAL-CLIENT-ID", _clientId);

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

            MalSearchResponse? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<MalSearchResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                throw new MetadataProviderUnavailableException();
            }

            var data = parsed?.Data ?? throw new MetadataProviderUnavailableException();

            return data
                .Where(d => d.Node is not null)
                .Select(d => new MetadataSearchResult(
                    d.Node!.Id.ToString(),
                    d.Node.Title ?? string.Empty,
                    $"https://myanimelist.net/manga/{d.Node.Id}"))
                .ToList();
        }
    }

    public async Task<bool> PushEntryAsync(PaperbunkrDbContext context, TrackingLink link, TrackerPushPayload payload, CancellationToken cancellationToken)
    {
        string? accessToken = CredentialStore.Get(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return false;
        }

        var (status, isRereading) = MyAnimeListStatusMapper.ToListStatus(payload.Status);
        var form = new Dictionary<string, string> { ["status"] = status, ["is_rereading"] = isRereading ? "true" : "false" };
        if (payload.ChapterProgress is int chapters)
        {
            form["num_chapters_read"] = chapters.ToString();
        }

        var request = new HttpRequestMessage(HttpMethod.Put, $"{ApiBase}/manga/{link.ExternalId}/my_list_status")
        {
            Content = new FormUrlEncodedContent(form),
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
            return response.IsSuccessStatusCode;
        }
    }

    /// <summary>MAL's own <c>my_list_status</c> sub-object always carries its core fields
    /// (<c>status</c>/<c>num_chapters_read</c>/<c>is_rereading</c>) once requested, confirmed from
    /// Mihon's <c>MALListItemStatus</c> DTO declaring them non-nullable regardless of which optional
    /// sub-fields (e.g. <c>start_date</c>) a caller's <c>fields=</c> selector also asked for.</summary>
    public async Task<TrackerRemoteEntry?> GetEntryAsync(PaperbunkrDbContext context, TrackingLink link, CancellationToken cancellationToken)
    {
        string? accessToken = CredentialStore.Get(context, nameof(TrackingService.MyAnimeList), CredentialKind.OAuthAccessToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{ApiBase}/manga/{link.ExternalId}?fields=my_list_status{{status,num_chapters_read,is_rereading}}");
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

            MalListItemDto? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<MalListItemDto>(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return null;
            }

            var status = parsed?.MyListStatus;
            if (status is null)
            {
                return null;
            }

            var readingStatus = status.IsRereading ? ReadingStatus.ReReading : MyAnimeListStatusMapper.FromListStatus(status.Status);
            return new TrackerRemoteEntry(readingStatus, (int?)status.NumChaptersRead);
        }
    }
}

/// <summary><see cref="ReadingStatus"/> -&gt; MyAnimeList's manga status string + <c>is_rereading</c>
/// flag. MAL has no separate re-reading status - <see cref="ReadingStatus.ReReading"/> maps to
/// <c>reading</c> with the flag set, the one documented lossy case for this service
/// (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md).</summary>
public static class MyAnimeListStatusMapper
{
    public static (string Status, bool IsRereading) ToListStatus(ReadingStatus status) => status switch
    {
        ReadingStatus.Planned => ("plan_to_read", false),
        ReadingStatus.Reading => ("reading", false),
        ReadingStatus.Completed => ("completed", false),
        ReadingStatus.Paused => ("on_hold", false),
        ReadingStatus.Dropped => ("dropped", false),
        ReadingStatus.ReReading => ("reading", true),
        _ => ("plan_to_read", false),
    };

    /// <summary>Reverse of <see cref="ToListStatus"/>'s status half - the <c>is_rereading</c> flag is
    /// handled separately by the caller, matching how it's a sibling field on MAL's own response, not
    /// encoded into <c>status</c> itself.</summary>
    public static ReadingStatus FromListStatus(string? status) => status switch
    {
        "plan_to_read" => ReadingStatus.Planned,
        "reading" => ReadingStatus.Reading,
        "completed" => ReadingStatus.Completed,
        "on_hold" => ReadingStatus.Paused,
        "dropped" => ReadingStatus.Dropped,
        _ => ReadingStatus.Unknown,
    };
}

internal sealed class MalTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}

internal sealed class MalListItemDto
{
    [JsonPropertyName("my_list_status")]
    public MalListItemStatusDto? MyListStatus { get; set; }
}

internal sealed class MalListItemStatusDto
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("num_chapters_read")]
    public double NumChaptersRead { get; set; }

    [JsonPropertyName("is_rereading")]
    public bool IsRereading { get; set; }
}

internal sealed class MalSearchResponse
{
    [JsonPropertyName("data")]
    public List<MalSearchDataEntry>? Data { get; set; }
}

internal sealed class MalSearchDataEntry
{
    [JsonPropertyName("node")]
    public MalMangaNode? Node { get; set; }
}

internal sealed class MalMangaNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}
