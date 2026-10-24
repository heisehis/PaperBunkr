using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>What an on-demand Metron look-up for one series found.</summary>
public sealed record MetronUniverseLookupResult(bool Reachable, IReadOnlyList<string> Universes, int IssuesChecked)
{
    public string Describe() => !Reachable
        ? "Metron could not be reached."
        : Universes.Count > 0
            ? $"Added to {string.Join(", ", Universes.Select(u => $"\"{u}\""))}."
            : IssuesChecked == 0
                ? "None of this series' issues could be matched on Metron (no Metron id, ComicVine id or barcode)."
                : "Metron lists no universe for this series' issues.";
}

public sealed record MetronUniverseScanResult(IReadOnlyList<MetronUniverseInfo> Universes, int IssuesChecked);

/// <summary>Looks up issue universe associations using stored provider identities. Discovery is read-only; callers choose when to persist memberships.</summary>
public static class ContinuityMetronSeriesLookup
{
    private const int MaxIssuesToTry = 4;

    public static async Task<MetronUniverseScanResult> FindUniversesAsync(PaperbunkrDbContext context, int seriesId, IComicProvider provider, CancellationToken cancellationToken)
    {
        var issues = context.Issues.AsNoTracking().Where(i => i.SeriesId == seriesId).OrderBy(i => i.Id)
            .Select(i => new { i.Id, i.Upc }).ToList();
        var comicVineSeriesIds = context.ComicMetadataExternalIds.AsNoTracking()
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Series && e.EntityId == seriesId && e.Provider == ComicProvider.ComicVine)
            .Select(e => e.ExternalId).AsEnumerable()
            .Select(value => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 ? id : (int?)null)
            .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        int? comicVineSeriesId = comicVineSeriesIds.Count == 1 ? comicVineSeriesIds[0] : null;
        var found = new Dictionary<int, MetronUniverseInfo>();
        int attempted = 0;

        foreach (var issue in issues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? metronId = null;
            int? comicVineId = null;
            foreach (var link in context.ComicMetadataExternalIds.AsNoTracking()
                         .Where(e => e.EntityKind == ComicMetadataEntityKind.Issue && e.EntityId == issue.Id)
                         .Select(e => new { e.Provider, e.ExternalId }).ToList())
            {
                if (int.TryParse(link.ExternalId, NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0)
                {
                    if (link.Provider == ComicProvider.Metron) metronId = id;
                    else if (link.Provider == ComicProvider.ComicVine) comicVineId = id;
                }
            }

            string? upc = issue.Upc?.Trim();
            bool hasUsableUpc = upc is { Length: >= 12 } && upc.All(char.IsAsciiDigit);
            if (metronId is null && comicVineId is null && !hasUsableUpc) continue;
            if (attempted >= MaxIssuesToTry) break;
            attempted++;

            if (metronId is null && provider is IComicIssueLookup lookup)
            {
                if (comicVineId is int cv)
                {
                    var hits = await lookup.FindIssuesByComicVineIdAsync(cv, cancellationToken).ConfigureAwait(false);
                    if (hits.Count == 1) metronId = hits[0].IssueId;
                }

                if (metronId is null && hasUsableUpc)
                {
                    var hits = await lookup.FindIssuesByUpcAsync(upc!, cancellationToken).ConfigureAwait(false);
                    if (hits.Count == 1) metronId = hits[0].IssueId;
                }
            }

            if (metronId is not int idOnMetron) continue;
            var details = await provider.GetIssueDetailsAsync(idOnMetron, cancellationToken).ConfigureAwait(false);
            if (details is null) continue;
            foreach (var universe in details.Universes.Where(u => u.ExternalId is > 0 && !string.IsNullOrWhiteSpace(u.Name)))
            {
                int id = universe.ExternalId!.Value;
                found.TryAdd(id, new MetronUniverseInfo(id, universe.Name, null, null));
            }

        }

        // ComicVine scraping may have preserved only the series identity. Metron can bridge that
        // exact external ID to its series; inspect a small sample of that series' issues for the
        // universe tags. Never substitute a name search here.
        if (found.Count == 0 && comicVineSeriesId is int cvSeriesId && provider is IComicSeriesLookup seriesLookup)
        {
            var seriesHits = await seriesLookup.FindSeriesByComicVineIdAsync(cvSeriesId, cancellationToken).ConfigureAwait(false);
            if (seriesHits.Count == 1)
            {
                var metronIssues = await provider.GetVolumeIssuesAsync(seriesHits[0].SeriesId, cancellationToken).ConfigureAwait(false);
                foreach (var metronIssue in metronIssues.Take(MaxIssuesToTry))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    attempted++;
                    var details = await provider.GetIssueDetailsAsync(metronIssue.Id, cancellationToken).ConfigureAwait(false);
                    if (details is null) continue;
                    foreach (var universe in details.Universes.Where(u => u.ExternalId is > 0 && !string.IsNullOrWhiteSpace(u.Name)))
                    {
                        int id = universe.ExternalId!.Value;
                        found.TryAdd(id, new MetronUniverseInfo(id, universe.Name, null, null));
                    }
                    if (found.Count > 0) break;
                }
            }
        }

        return new MetronUniverseScanResult(found.Values.ToList(), attempted);
    }

    /// <summary>Existing detail-screen action: perform the read-only lookup, then apply its findings using the shared scraper path.</summary>
    public static async Task<MetronUniverseLookupResult> RunAsync(PaperbunkrDbContext context, int seriesId, IComicProvider provider, CancellationToken cancellationToken)
    {
        MetronUniverseScanResult scan;
        try
        {
            scan = await FindUniversesAsync(context, seriesId, provider, cancellationToken).ConfigureAwait(false);
        }
        catch (ComicVineException)
        {
            return new MetronUniverseLookupResult(false, Array.Empty<string>(), 0);
        }

        if (scan.Universes.Count > 0)
        {
            ContinuityMetronMatchResolver.SyncFromIssueDetails(context, seriesId, ComicProvider.Metron,
                scan.Universes.Select(u => new ComicVineIdName(u.Id, u.Name)).ToList());
        }

        return new MetronUniverseLookupResult(true, scan.Universes.Select(u => u.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList(), scan.IssuesChecked);
    }
}
