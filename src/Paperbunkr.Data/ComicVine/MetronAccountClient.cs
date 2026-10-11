using System.Globalization;
using System.Text.Json.Nodes;

namespace Paperbunkr.Data.ComicVine;

public sealed record MetronPullListSeries(int SeriesId, string Name, int? YearBegan);

/// <param name="ItemId">The collection item's own id - what a rating update addresses - not the issue's.</param>
/// <param name="DateRead">The most recent read, when it has been read.</param>
public sealed record MetronCollectionItem(int ItemId, int IssueId, bool IsRead, DateTime? DateRead, int? Rating);

public sealed record MetronWishListItem(int ItemId, int IssueId, string Status);

/// <summary>One page of the account's collection and the address of the next, if there is one.</summary>
public sealed record MetronCollectionPage(IReadOnlyList<MetronCollectionItem> Items, string? Next);

/// <summary>
/// The parts of Metron that belong to one account: its pull list, collection (with read tracking) and wish list
/// (docs/superpowers/specs/2026-10-05-metron-account-sync-design.md). Every call sends or reads personal data, so
/// nothing reaches for this unless the user has turned account sync on.
/// </summary>
public interface IMetronAccount
{
    Task<IReadOnlyList<MetronPullListSeries>> GetPullListAsync(CancellationToken cancellationToken);

    Task AddToPullListAsync(int seriesId, CancellationToken cancellationToken);

    /// <summary>A series that is already off the list is not an error.</summary>
    Task RemoveFromPullListAsync(int seriesId, CancellationToken cancellationToken);

    /// <summary>Records one read. Each call adds a read date (that is how a re-read is kept), and creates the collection item if there isn't one.</summary>
    Task<MetronCollectionItem> ScrobbleAsync(int issueId, DateTime readAtUtc, int? rating, CancellationToken cancellationToken);

    /// <summary>Adds the issue as a digital copy. An issue already in the collection comes back unchanged.</summary>
    Task<MetronCollectionItem> AddToCollectionAsync(int issueId, CancellationToken cancellationToken);

    Task SetCollectionRatingAsync(int itemId, int? rating, CancellationToken cancellationToken);

    /// <param name="pageUrl">Null for the first page, then each page's <see cref="MetronCollectionPage.Next"/>.</param>
    Task<MetronCollectionPage> GetCollectionPageAsync(string? pageUrl, CancellationToken cancellationToken);

    Task<MetronWishListItem> AddToWishListAsync(int issueId, CancellationToken cancellationToken);

    /// <summary>Marks the item acquired, which also puts the issue in the collection. Returns that collection item's id when Metron names it.</summary>
    Task<int?> AcquireWishListItemAsync(int itemId, CancellationToken cancellationToken);

    /// <summary>An item that is already gone is not an error.</summary>
    Task RemoveFromWishListAsync(int itemId, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IMetronAccount"/> over <see cref="MetronClient"/>'s own request path, so writes share its login, rate limit, quota bookkeeping and error codes.
/// <b>Every path ends in a slash.</b> Metron's <c>api/README.md</c> writes five of the write paths without one (<c>pull_list/series/add</c>, the
/// two <c>remove</c>s, <c>wish_list/items/add</c>, <c>acquire</c>), but its router only serves them with it, as its own client shows
/// (mokkari: <c>METRON_URL = "https://metron.cloud/api/{}/"</c>). Without the slash Metron answers with a redirect, a redirect drops the login, and the
/// request that arrives is a 401 - three of which get the address blocked for a day. Found on the first real run, 2026-10-05.
/// A write is retried after a dropped connection only where repeating it changes nothing; a scrobble isn't, because a second one is a second read date.
/// </summary>
public sealed class MetronAccountClient(MetronClient client) : IMetronAccount
{
    private const string BaseUrl = "https://metron.cloud/api";
    private const int MaxPages = 30;

    public async Task<IReadOnlyList<MetronPullListSeries>> GetPullListAsync(CancellationToken cancellationToken)
    {
        var series = new List<MetronPullListSeries>();
        string? url = $"{BaseUrl}/pull_list/series/";
        for (int page = 0; page < MaxPages && url is not null; page++)
        {
            var root = await client.SendAsync(HttpMethod.Get, url, null, retryTransportFailure: true, cancellationToken).ConfigureAwait(false);
            foreach (var entry in Results(root))
            {
                if (entry["series"] is JsonObject s && Int(s["id"]) is int id)
                {
                    // The list shows the name with its year appended, as the series search does.
                    int? year = Int(s["year_began"]);
                    series.Add(new MetronPullListSeries(id, MetronClient.CleanName(s["series"]?.GetValue<string>() ?? s["name"]?.GetValue<string>() ?? string.Empty, year), year));
                }
            }

            url = root["next"]?.GetValue<string?>();
        }

        return series;
    }

    public Task AddToPullListAsync(int seriesId, CancellationToken cancellationToken) =>
        client.SendAsync(HttpMethod.Post, $"{BaseUrl}/pull_list/series/add/", new JsonObject { ["series_id"] = seriesId }, retryTransportFailure: true, cancellationToken);

    public Task RemoveFromPullListAsync(int seriesId, CancellationToken cancellationToken) =>
        IgnoringNotFound(client.SendAsync(HttpMethod.Delete, $"{BaseUrl}/pull_list/series/{Id(seriesId)}/remove/", null, retryTransportFailure: true, cancellationToken));

    public async Task<MetronCollectionItem> ScrobbleAsync(int issueId, DateTime readAtUtc, int? rating, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["issue_id"] = issueId,
            ["date_read"] = readAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };
        if (rating is int stars)
        {
            body["rating"] = stars;
        }

        var root = await client.SendAsync(HttpMethod.Post, $"{BaseUrl}/collection/scrobble/", body, retryTransportFailure: false, cancellationToken).ConfigureAwait(false);
        return ParseCollectionItem(root) ?? throw new ComicVineException("Metron's reply to a read didn't name the collection item.");
    }

    public async Task<MetronCollectionItem> AddToCollectionAsync(int issueId, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["issue_id"] = issueId, ["book_format"] = "DIGITAL" };
        var root = await client.SendAsync(HttpMethod.Post, $"{BaseUrl}/collection/add/", body, retryTransportFailure: true, cancellationToken).ConfigureAwait(false);
        return ParseCollectionItem(root) ?? throw new ComicVineException("Metron's reply to a collection add didn't name the item.");
    }

    public Task SetCollectionRatingAsync(int itemId, int? rating, CancellationToken cancellationToken) =>
        client.SendAsync(HttpMethod.Patch, $"{BaseUrl}/collection/{Id(itemId)}/", new JsonObject { ["rating"] = rating }, retryTransportFailure: true, cancellationToken);

    public async Task<MetronCollectionPage> GetCollectionPageAsync(string? pageUrl, CancellationToken cancellationToken)
    {
        var root = await client.SendAsync(HttpMethod.Get, pageUrl ?? $"{BaseUrl}/collection/", null, retryTransportFailure: true, cancellationToken).ConfigureAwait(false);
        var items = Results(root).Select(ParseCollectionItem).Where(i => i is not null).Select(i => i!).ToList();
        return new MetronCollectionPage(items, root["next"]?.GetValue<string?>());
    }

    public async Task<MetronWishListItem> AddToWishListAsync(int issueId, CancellationToken cancellationToken)
    {
        var root = await client.SendAsync(HttpMethod.Post, $"{BaseUrl}/wish_list/items/add/", new JsonObject { ["issue_id"] = issueId }, retryTransportFailure: true, cancellationToken).ConfigureAwait(false);
        return Int(root["id"]) is int itemId
            ? new MetronWishListItem(itemId, Int((root["issue"] as JsonObject)?["id"]) ?? issueId, root["status"]?.GetValue<string>() ?? "Wanted")
            : throw new ComicVineException("Metron's reply to a wish-list add didn't name the item.");
    }

    public async Task<int?> AcquireWishListItemAsync(int itemId, CancellationToken cancellationToken)
    {
        var root = await client.SendAsync(HttpMethod.Post, $"{BaseUrl}/wish_list/items/{Id(itemId)}/acquire/", new JsonObject(), retryTransportFailure: true, cancellationToken).ConfigureAwait(false);
        return Int(root["collection_item_id"]);
    }

    public Task RemoveFromWishListAsync(int itemId, CancellationToken cancellationToken) =>
        IgnoringNotFound(client.SendAsync(HttpMethod.Delete, $"{BaseUrl}/wish_list/items/{Id(itemId)}/remove/", null, retryTransportFailure: true, cancellationToken));

    private static async Task IgnoringNotFound(Task<JsonNode> send)
    {
        try
        {
            await send.ConfigureAwait(false);
        }
        catch (ComicVineException ex) when (ex.ApiStatusCode == 101)
        {
            // Already gone, which is what was wanted.
        }
    }

    /// <summary>
    /// A collection item as the list, the add reply and the scrobble reply each give it: the item id, the issue id, and the read state. The list carries
    /// <c>read_dates</c> as objects and the scrobble reply as plain strings; both are read, newest first as Metron orders them.
    /// </summary>
    private static MetronCollectionItem? ParseCollectionItem(JsonNode? node)
    {
        if (node is not JsonObject o || Int(o["id"]) is not int itemId || Int((o["issue"] as JsonObject)?["id"]) is not int issueId)
        {
            return null;
        }

        string? read = o["date_read"]?.GetValue<string>();
        if (read is null && o["read_dates"] is JsonArray { Count: > 0 } dates)
        {
            read = dates[0] is JsonObject first ? first["read_date"]?.GetValue<string>() : dates[0]?.GetValue<string>();
        }

        DateTime? dateRead = DateTime.TryParse(read, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
        bool isRead = o["is_read"] is JsonValue flag && flag.TryGetValue(out bool value) ? value : dateRead is not null;
        return new MetronCollectionItem(itemId, issueId, isRead, dateRead, Int(o["rating"]));
    }

    private static IEnumerable<JsonObject> Results(JsonNode root) =>
        (root["results"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();

    private static int? Int(JsonNode? node) => node is not null && int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : null;

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);
}
