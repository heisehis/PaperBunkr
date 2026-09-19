using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Paperbunkr.Data.Metadata;

/// <summary>One Wikidata search hit - <see cref="Qid"/> is the bare identifier (e.g. "Q79037"), no "http://www.wikidata.org/entity/" prefix.</summary>
public sealed record WikidataSearchResult(string Qid, string Label);

/// <summary>
/// The subset of a Wikidata item's claims <see cref="ContinuityWikidataMatchResolver"/> needs:
/// its own classes (<c>P31</c>, "instance of") and its universe-linking properties.
/// </summary>
public sealed record WikidataEntity(
    string Qid,
    string Label,
    string? Description,
    IReadOnlyList<string> InstanceOfQids,
    string? FromNarrativeUniverseQid,
    string? TakesPlaceInFictionalUniverseQid,
    IReadOnlyList<string> PublisherQids,
    IReadOnlyList<string> GenreQids);

/// <summary>
/// Thin wrapper over Wikidata's public `wbsearchentities`/EntityData APIs (docs/superpowers/specs/
/// 2026-09-17-storyevent-continuity-autopopulate-design.md, Phase 2) - first Wikidata integration in
/// this codebase (confirmed via repo-wide grep before this feature). No API key required. Mirrors
/// <see cref="AniListMetadataProvider"/>'s error-handling shape: a failed call returns null/empty
/// rather than throwing, since there is no proactive "is online" check anywhere in this app.
/// </summary>
internal sealed class WikidataClient
{
    private const string ApiUrl = "https://www.wikidata.org/w/api.php";
    private const string EntityDataUrlTemplate = "https://www.wikidata.org/wiki/Special:EntityData/{0}.json";

    // Wikidata's API etiquette (mediawiki.org/wiki/API:Etiquette) asks for serial requests, not a
    // hard per-second cap - a courtesy throttle, same shape as ComicVineSource's own.
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(1);

    private readonly HttpClient _httpClient;
    private readonly object _throttleLock = new();
    private DateTime _nextAllowedRequestUtc = DateTime.MinValue;

    public WikidataClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<WikidataSearchResult>> SearchEntitiesAsync(string name, CancellationToken cancellationToken)
    {
        string url = $"{ApiUrl}?action=wbsearchentities&search={Uri.EscapeDataString(name)}&language=en&type=item&limit=10&format=json";
        var root = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var results = root?["search"]?.AsArray() ?? new JsonArray();

        var list = new List<WikidataSearchResult>();
        foreach (var node in results)
        {
            string? id = node?["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            list.Add(new WikidataSearchResult(id, node?["label"]?.GetValue<string>() ?? id));
        }

        return list;
    }

    public async Task<WikidataEntity?> GetEntityAsync(string qid, CancellationToken cancellationToken)
    {
        string url = string.Format(EntityDataUrlTemplate, Uri.EscapeDataString(qid));
        var root = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);
        var entity = root?["entities"]?[qid];
        var claims = entity?["claims"];
        if (claims is null)
        {
            return null;
        }

        string label = entity?["labels"]?["en"]?["value"]?.GetValue<string>() ?? qid;
        string? description = entity?["descriptions"]?["en"]?["value"]?.GetValue<string>();
        var instanceOf = ReadItemIds(claims, "P31");
        string? fromNarrativeUniverse = ReadItemIds(claims, "P1080").FirstOrDefault();
        string? takesPlaceIn = ReadItemIds(claims, "P1434").FirstOrDefault();
        var publishers = ReadItemIds(claims, "P123");
        var genres = ReadItemIds(claims, "P136");
        return new WikidataEntity(qid, label, description, instanceOf, fromNarrativeUniverse, takesPlaceIn, publishers, genres);
    }

    private static IReadOnlyList<string> ReadItemIds(JsonNode claims, string property)
    {
        var array = claims[property]?.AsArray();
        if (array is null)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var claim in array)
        {
            string? id = claim?["mainsnak"]?["datavalue"]?["value"]?["id"]?.GetValue<string>();
            if (id is not null)
            {
                result.Add(id);
            }
        }

        return result;
    }

    private async Task<JsonNode?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        await ThrottleAsync(cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                TimeSpan retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);
                response.Dispose();

                try
                {
                    response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    return null;
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return JsonNode.Parse(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        TimeSpan delay;
        lock (_throttleLock)
        {
            delay = _nextAllowedRequestUtc - DateTime.UtcNow;
        }

        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        lock (_throttleLock)
        {
            _nextAllowedRequestUtc = DateTime.UtcNow + MinRequestInterval;
        }
    }

    /// <summary>Sets a contactable User-Agent per Wikidata's API etiquette - a non-compliant UA gets throttled to a restrictive tier.</summary>
    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Paperbunkr/0.1 (comic library manager; https://github.com/heisehis/PaperBunkr)");
        return client;
    }
}
