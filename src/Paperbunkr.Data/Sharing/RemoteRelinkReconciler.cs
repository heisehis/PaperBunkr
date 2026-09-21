using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Sharing.Protocol;

namespace Paperbunkr.Data.Sharing;

/// <param name="SeriesMatched">Existing mirror series re-keyed onto a catalog series.</param>
/// <param name="IssuesMatched">Existing mirror issues re-keyed onto a catalog issue - these keep every bit of client-local reading data.</param>
/// <param name="IssuesOrphaned">Existing mirror issues with no unambiguous counterpart. Kept (offline, unreadable) rather than deleted, so nothing vanishes silently.</param>
/// <param name="SeriesOrphaned">Existing mirror series with no unambiguous counterpart.</param>
public sealed record RelinkResult(int SeriesMatched, int IssuesMatched, int IssuesOrphaned, int SeriesOrphaned);

/// <summary>
/// Relink (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md §7.1): when a host's
/// library was rebuilt its ids changed, so the existing mirror rows - which carry the user's reading
/// progress, bookmarks and ratings - would otherwise be orphaned. This re-keys them onto the new
/// catalog by stable metadata (normalized series name via <see cref="TitleNormalizer"/>, then issue
/// number, then volume+year to break ties) and only ever on an <em>unambiguous</em> match: when two
/// candidates could fit, neither is touched and the row is orphaned instead of guessed at. It changes
/// only the remote ids; <see cref="RemoteMirrorSync.Apply"/> runs afterwards to refresh fields, add
/// what is new, and remove nothing that was orphaned (those rows carry null ids and are invisible to it).
/// </summary>
public static class RemoteRelinkReconciler
{
    public static RelinkResult Reconcile(
        PaperbunkrDbContext context,
        int sourceId,
        IReadOnlyList<CatalogSeriesDto> catalogSeries,
        IReadOnlyList<CatalogIssueDto> catalogIssues)
    {
        if (!context.IncludeRemote)
        {
            throw new InvalidOperationException("RemoteRelinkReconciler needs a context created with IncludeRemote = true.");
        }

        using var transaction = context.Database.BeginTransaction();

        List<Series> existingSeries = context.Series.Where(s => s.RemoteSourceId == sourceId).ToList();
        List<Issue> existingIssues = context.Issues.Where(i => i.RemoteSourceId == sourceId).ToList();

        // Phase 1: release every old key. The unique (source, remoteId) index would otherwise trip
        // while ids are being reassigned (an old row can hold an id a different new row now needs).
        foreach (Series s in existingSeries) s.RemoteSeriesId = null;
        foreach (Issue i in existingIssues) i.RemoteIssueId = null;
        context.SaveChanges();

        // Phase 2: assign new keys where the match is unambiguous.
        var newSeriesByKey = catalogSeries.GroupBy(s => SeriesKey(s.Name)).ToDictionary(g => g.Key, g => g.ToList());
        var oldSeriesByKey = existingSeries.GroupBy(s => SeriesKey(s.Name)).ToDictionary(g => g.Key, g => g.ToList());
        var issuesByLocalSeries = existingIssues.GroupBy(i => i.SeriesId).ToDictionary(g => g.Key, g => g.ToList());
        var catalogIssuesBySeries = catalogIssues.GroupBy(i => i.SeriesId).ToDictionary(g => g.Key, g => g.ToList());

        int seriesMatched = 0, issuesMatched = 0;
        foreach ((string key, List<CatalogSeriesDto> fresh) in newSeriesByKey)
        {
            if (fresh.Count != 1 || !oldSeriesByKey.TryGetValue(key, out List<Series>? old) || old.Count != 1)
            {
                continue; // absent on one side, or ambiguous on either: leave both alone
            }

            Series series = old[0];
            CatalogSeriesDto dto = fresh[0];
            series.RemoteSeriesId = dto.Id;
            seriesMatched++;

            issuesByLocalSeries.TryGetValue(series.Id, out List<Issue>? oldIssues);
            catalogIssuesBySeries.TryGetValue(dto.Id, out List<CatalogIssueDto>? newIssues);
            issuesMatched += MatchIssues(oldIssues ?? new List<Issue>(), newIssues ?? new List<CatalogIssueDto>());
        }

        context.SaveChanges();

        int issuesOrphaned = existingIssues.Count(i => i.RemoteIssueId is null);
        int seriesOrphaned = existingSeries.Count(s => s.RemoteSeriesId is null);
        transaction.Commit();
        return new RelinkResult(seriesMatched, issuesMatched, issuesOrphaned, seriesOrphaned);
    }

    private static int MatchIssues(List<Issue> oldIssues, List<CatalogIssueDto> newIssues)
    {
        int matched = 0;
        var oldByNumber = oldIssues.GroupBy(i => NumberKey(i.Number, i.Title)).ToDictionary(g => g.Key, g => g.ToList());
        var newByNumber = newIssues.GroupBy(i => NumberKey(i.Number, i.Title)).ToDictionary(g => g.Key, g => g.ToList());

        foreach ((string key, List<CatalogIssueDto> fresh) in newByNumber)
        {
            if (!oldByNumber.TryGetValue(key, out List<Issue>? old))
            {
                continue;
            }

            if (fresh.Count == 1 && old.Count == 1)
            {
                old[0].RemoteIssueId = fresh[0].Id;
                matched++;
                continue;
            }

            // Several with the same number (variants, reprints): only accept a pairing that volume+year
            // makes unique on BOTH sides; otherwise guessing could hand a user's progress to the wrong book.
            var oldByTie = old.GroupBy(i => TieKey(i.Volume, i.Year)).ToDictionary(g => g.Key, g => g.ToList());
            var newByTie = fresh.GroupBy(i => TieKey(i.Volume, i.Year)).ToDictionary(g => g.Key, g => g.ToList());
            foreach ((string tie, List<CatalogIssueDto> freshTied) in newByTie)
            {
                if (freshTied.Count == 1 && oldByTie.TryGetValue(tie, out List<Issue>? oldTied) && oldTied.Count == 1)
                {
                    oldTied[0].RemoteIssueId = freshTied[0].Id;
                    matched++;
                }
            }
        }

        return matched;
    }

    private static string SeriesKey(string name) => TitleNormalizer.StripDown(name ?? string.Empty).ToLowerInvariant();

    /// <summary>Issue number if there is one, otherwise the title (one-shots, TPBs) - both case-insensitive.</summary>
    private static string NumberKey(string? number, string? title) =>
        (!string.IsNullOrWhiteSpace(number) ? number : title ?? string.Empty).Trim().ToLowerInvariant();

    private static string TieKey(string? volume, int? year) => $"{volume?.Trim().ToLowerInvariant()}|{year}";
}
