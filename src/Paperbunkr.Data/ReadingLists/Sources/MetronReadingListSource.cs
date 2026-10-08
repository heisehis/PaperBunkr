using System.Text.Json.Nodes;
using Paperbunkr.Data.ComicVine;

namespace Paperbunkr.Data.ReadingLists.Sources;

/// <summary>
/// Metron's reading lists - the user's own and every public one - as a source for a new reading list
/// (docs/superpowers/specs/2026-10-05-metron-account-sync-design.md, D6). Distinct from <see cref="MetronSource"/>,
/// which lists Metron's story <i>arcs</i>: a reading list is a curated order someone built (often credited to
/// Comic Book Reading Orders or the like), an arc is a database record ordered by cover date. Metron's API is
/// read-only here, so this only ever imports.
///
/// Goes through <see cref="MetronClient"/>'s own request path, so it shares the one rate limit and quota
/// bookkeeping with scraping and the weekly list instead of keeping a private throttle.
/// </summary>
public sealed class MetronReadingListSource(MetronClient client) : IReadingListSource
{
    private const string BaseUrl = "https://metron.cloud/api";

    // 50 items a page; a master reading order can run to thousands of issues.
    private const int MaxPages = 60;

    public string SourceKey => "MetronLists";

    public string DisplayName => "Metron reading lists";

    public bool RequiresCredentials => true;

    public bool HasBrowsableCatalog => false;

    public async Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var root = await GetAsync($"{BaseUrl}/reading_list/?name={Uri.EscapeDataString(query)}", cancellationToken).ConfigureAwait(false);
        var results = new List<ArcSearchResult>();
        foreach (var node in (root["results"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            if (node["id"] is null)
            {
                continue;
            }

            // The list row has no synopsis or issue count; what tells two same-named lists apart is whose it is and what kind.
            string? owner = (node["user"] as JsonObject)?["username"]?.GetValue<string>();
            string? type = node["list_type"]?.GetValue<string>();
            string? credit = node["attribution_source"]?.GetValue<string>();
            string deck = string.Join(" · ", new[] { type, string.IsNullOrWhiteSpace(owner) ? null : $"by {owner}", string.IsNullOrWhiteSpace(credit) ? null : $"from {credit}" }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

            results.Add(new ArcSearchResult(node["id"]!.ToString(), node["name"]?.GetValue<string>() ?? string.Empty, deck.Length == 0 ? null : deck, Publisher: null, IssueCount: 0));
        }

        return results;
    }

    public async Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken)
    {
        var issues = new List<(int Order, ArcIssue Issue)>();
        string? url = $"{BaseUrl}/reading_list/{Uri.EscapeDataString(arcId)}/items/";
        for (int page = 0; page < MaxPages && url is not null; page++)
        {
            var root = await GetAsync(url, cancellationToken).ConfigureAwait(false);
            foreach (var node in (root["results"] as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                if (node["issue"] is not JsonObject issue)
                {
                    continue;
                }

                // issue_type is the list's own note on the issue's part in the story (Prologue, Core Issue, Tie-In, Epilogue).
                string? role = node["issue_type"]?.GetValue<string>();
                int order = node["order"] is { } o && int.TryParse(o.ToString(), out int parsed) ? parsed : issues.Count + 1;
                issues.Add((order, MetronSource.ParseArcIssue(issue) with { Annotation = string.IsNullOrWhiteSpace(role) ? null : role }));
            }

            url = root["next"]?.GetValue<string?>();
        }

        // Metron already returns them by `order`; sorting again costs nothing and doesn't trust it to.
        return issues.OrderBy(i => i.Order).Select(i => i.Issue).ToList();
    }

    public async Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken)
    {
        var detail = await GetAsync($"{BaseUrl}/reading_list/{Uri.EscapeDataString(arcId)}/", cancellationToken).ConfigureAwait(false);
        return new ArcOverviewInfo(detail["desc"]?.GetValue<string>(), detail["image"]?.GetValue<string>());
    }

    private async Task<JsonNode> GetAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(HttpMethod.Get, url, body: null, retryTransportFailure: true, cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException ex)
        {
            throw new ReadingListSourceException(DisplayName, ex.Message);
        }
    }
}
