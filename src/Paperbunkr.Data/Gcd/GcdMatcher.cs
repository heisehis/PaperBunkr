using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Gcd;

/// <summary>What one <see cref="GcdMatcher.RunAsync"/> pass changed.</summary>
public sealed record GcdMatchResult(int SeriesByMetron, int SeriesByName, int IssuesMatched, int IssuesCleared)
{
    public int SeriesMatched => SeriesByMetron + SeriesByName;
}

/// <summary>
/// Links library series and issues to the installed Grand Comics Database extract (docs/superpowers/specs/2026-09-27-gcd-data-design.md
/// §3). Metron's own GCD ids win: first the ones the scraper already stored on issues, then Metron's series record for series scraped
/// before that. Everything else gets a GCD id only from a unique name + start year + publisher hit. Issues follow their series, by
/// normalized issue number. A Metron match is never overwritten by a name match.
/// </summary>
public static class GcdMatcher
{
    private static readonly Regex RxYearSuffix = new(@"\s*\((\d{4})\)\s*$", RegexOptions.Compiled);
    private static readonly Regex RxVolume = new(@"\bv(ol(ume)?)?\.?\s?\d+\b\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RxLeadingZeros = new(@"^0+(?=\d)", RegexOptions.Compiled);

    private static readonly string[] PublisherSuffixes =
        { "comics", "comic", "publishing", "publications", "entertainment", "worldwide", "group", "press", "books", "inc", "llc", "ltd", "co" };

    /// <summary>
    /// One matching pass. <paramref name="metronSeriesGcdId"/> asks Metron for a series' GCD id (null skips the network step, e.g. no
    /// Metron account); at most <paramref name="maxMetronLookups"/> series are looked up per pass, to stay inside Metron's rate limit.
    /// </summary>
    public static async Task<GcdMatchResult> RunAsync(
        Func<PaperbunkrDbContext> contextFactory,
        GcdDataStore store,
        Func<int, CancellationToken, Task<int?>>? metronSeriesGcdId = null,
        int maxMetronLookups = 60,
        CancellationToken cancellationToken = default)
    {
        List<SeriesRow> series;
        List<IssueRow> issues;
        Dictionary<int, int> metronSeriesIds;
        using (var context = contextFactory())
        {
            series = await context.Series.AsNoTracking()
                .Select(s => new SeriesRow(s.Id, s.Name, s.Publisher, s.GcdSeriesId, s.GcdMatchSource))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            issues = await context.Issues.AsNoTracking().Where(i => !i.IsPlaceholder)
                .Select(i => new IssueRow(i.Id, i.SeriesId, i.Number, i.Year, i.Publisher, i.GcdIssueId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var metronRows = await context.ComicMetadataExternalIds.AsNoTracking()
                .Where(x => x.EntityKind == ComicMetadataEntityKind.Series && x.Provider == ComicProvider.Metron)
                .Select(x => new { x.EntityId, x.ExternalId })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            metronSeriesIds = new Dictionary<int, int>();
            foreach (var row in metronRows)
            {
                if (int.TryParse(row.ExternalId, out int metronId) && metronId > 0)
                {
                    metronSeriesIds[row.EntityId] = metronId;
                }
            }
        }

        var issuesBySeries = issues.GroupBy(i => i.SeriesId).ToDictionary(g => g.Key, g => g.ToList());
        var changed = new Dictionary<int, (int GcdId, GcdMatchSourceKind Source)>();
        int byMetron = 0, byName = 0;

        // 1. Metron ids the scraper already stored on issues: the GCD series most of them belong to.
        var scrapedIssueIds = issues.Where(i => i.GcdIssueId is not null).Select(i => i.GcdIssueId!.Value).ToList();
        var seriesOfScraped = store.SeriesOfIssues(scrapedIssueIds);
        foreach (var s in series.Where(s => s.GcdMatchSource != GcdMatchSourceKind.Metron))
        {
            var known = issuesBySeries.GetValueOrDefault(s.Id)?
                .Where(i => i.GcdIssueId is int id && seriesOfScraped.ContainsKey(id))
                .Select(i => seriesOfScraped[i.GcdIssueId!.Value]).ToList();
            if (known is not { Count: > 0 })
            {
                continue;
            }

            var top = known.GroupBy(id => id).OrderByDescending(g => g.Count()).First();
            if (top.Count() * 2 <= known.Count || top.Key == s.GcdSeriesId)
            {
                // No clear majority, or it only confirms the name match already there.
                continue;
            }

            changed[s.Id] = (top.Key, GcdMatchSourceKind.Metron);
            byMetron++;
        }

        // 2. Metron's series record, for Metron-scraped series still without a Metron-sourced id.
        if (metronSeriesGcdId is not null && maxMetronLookups > 0)
        {
            var toAsk = series
                .Where(s => s.GcdMatchSource != GcdMatchSourceKind.Metron && !changed.ContainsKey(s.Id) && metronSeriesIds.ContainsKey(s.Id))
                .OrderBy(s => s.GcdSeriesId is null ? 0 : 1).ThenBy(s => s.Id)
                .Take(maxMetronLookups);
            foreach (var s in toAsk)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int? gcdId;
                try
                {
                    gcdId = await metronSeriesGcdId(metronSeriesIds[s.Id], cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    continue;
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                if (gcdId is int id && id > 0)
                {
                    changed[s.Id] = (id, GcdMatchSourceKind.Metron);
                    byMetron++;
                }
            }
        }

        // 3. Names, for series with no GCD id at all.
        foreach (var s in series.Where(s => s.GcdSeriesId is null && !changed.ContainsKey(s.Id)))
        {
            var own = issuesBySeries.GetValueOrDefault(s.Id) ?? new List<IssueRow>();
            if (FindByName(store, s.Name, StartYear(s.Name, own), s.Publisher ?? MostCommon(own.Select(i => i.Publisher))) is int id)
            {
                changed[s.Id] = (id, GcdMatchSourceKind.Name);
                byName++;
            }
        }

        // 4. Issues, within every matched series.
        var issueUpdates = new Dictionary<int, int?>();
        foreach (var s in series)
        {
            int? gcdSeriesId = changed.TryGetValue(s.Id, out var c) ? c.GcdId : s.GcdSeriesId;
            if (gcdSeriesId is not int seriesGcdId || !issuesBySeries.TryGetValue(s.Id, out var own))
            {
                continue;
            }

            var source = changed.TryGetValue(s.Id, out var c2) ? c2.Source : s.GcdMatchSource;
            var byNumber = store.IssuesOf(seriesGcdId)
                .GroupBy(i => NormalizeNumber(i.Number))
                .Where(g => g.Key.Length > 0 && g.Count() == 1)
                .ToDictionary(g => g.Key, g => g.Single().Id);
            var gcdIdsInSeries = new HashSet<int>(byNumber.Values);
            var ownedGcdIds = store.SeriesOfIssues(own.Where(i => i.GcdIssueId is not null).Select(i => i.GcdIssueId!.Value));
            foreach (var issue in own)
            {
                if (issue.GcdIssueId is int existing)
                {
                    bool belongs = ownedGcdIds.TryGetValue(existing, out int inSeries) && inSeries == seriesGcdId;

                    // Metron's own issue ids stand even when GCD splits the run differently; name-matched series (or ones whose GCD
                    // series just changed) drop ids that point outside the series.
                    if (belongs || (source == GcdMatchSourceKind.Metron && !changed.ContainsKey(s.Id)))
                    {
                        continue;
                    }

                    issueUpdates[issue.Id] = null;
                }

                if (byNumber.TryGetValue(NormalizeNumber(issue.Number), out int gcdIssueId) && gcdIdsInSeries.Contains(gcdIssueId))
                {
                    issueUpdates[issue.Id] = gcdIssueId;
                }
            }
        }

        int matched = issueUpdates.Count(u => u.Value is not null);
        int cleared = issueUpdates.Count(u => u.Value is null);
        if (changed.Count > 0 || issueUpdates.Count > 0)
        {
            using var context = contextFactory();
            using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            foreach (var (seriesId, (gcdId, source)) in changed)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Series SET GcdSeriesId = {gcdId}, GcdMatchSource = {source.ToString()} WHERE Id = {seriesId}",
                    cancellationToken).ConfigureAwait(false);
            }

            foreach (var (issueId, gcdIssueId) in issueUpdates)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Issues SET GcdIssueId = {gcdIssueId} WHERE Id = {issueId}", cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new GcdMatchResult(byMetron, byName, matched, cleared);
    }

    /// <summary>Forgets every GCD id (the data was removed).</summary>
    public static void ClearAll(Func<PaperbunkrDbContext> contextFactory)
    {
        using var context = contextFactory();
        context.Database.ExecuteSqlRaw("UPDATE Series SET GcdSeriesId = NULL, GcdMatchSource = NULL WHERE GcdSeriesId IS NOT NULL OR GcdMatchSource IS NOT NULL");
        context.Database.ExecuteSqlRaw("UPDATE Issues SET GcdIssueId = NULL WHERE GcdIssueId IS NOT NULL");
    }

    /// <summary>The one GCD series with this name key, start year and publisher; null when none or several fit.</summary>
    internal static int? FindByName(GcdDataStore store, string name, int? startYear, string? publisher)
    {
        string key = NameKey(name);
        string publisherKey = PublisherKey(publisher);
        if (key.Length == 0 || startYear is null || publisherKey.Length == 0)
        {
            return null;
        }

        var hits = store.FindSeriesByKey(key)
            .Where(g => g.YearBegan == startYear && PublisherKey(g.Publisher) == publisherKey)
            .Take(2).ToList();
        return hits.Count == 1 ? hits[0].Id : null;
    }

    /// <summary>The extract's <c>name_key</c> for a library series name: "(yyyy)" and volume markers dropped, then CE's strip-down.</summary>
    internal static string NameKey(string name)
    {
        string bare = RxVolume.Replace(RxYearSuffix.Replace(name, string.Empty), string.Empty);
        return TitleNormalizer.StripDown(bare).ToLowerInvariant();
    }

    /// <summary>The "(yyyy)" in the series name, else its earliest issue year.</summary>
    private static int? StartYear(string name, IEnumerable<IssueRow> issues)
    {
        var m = RxYearSuffix.Match(name);
        if (m.Success)
        {
            return int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return issues.Where(i => i.Year is > 0).Select(i => i.Year).Min();
    }

    internal static string PublisherKey(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return string.Empty;
        }

        string key = TitleNormalizer.StripDown(publisher).ToLowerInvariant();
        bool stripped;
        do
        {
            stripped = false;
            foreach (string suffix in PublisherSuffixes)
            {
                if (key.Length > suffix.Length && key.EndsWith(suffix, StringComparison.Ordinal))
                {
                    key = key[..^suffix.Length];
                    stripped = true;
                }
            }
        }
        while (stripped);

        return key;
    }

    /// <summary>"001" → "1", "½" → "1/2", GCD's dual numbering "12 [157]" → "12"; whitespace and '#' dropped.</summary>
    internal static string NormalizeNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            return string.Empty;
        }

        string s = number.Trim();
        int bracket = s.IndexOf('[');
        if (bracket > 0)
        {
            s = s[..bracket];
        }
        else if (bracket == 0)
        {
            s = s.Trim('[', ']');
        }

        s = new string(s.Replace("½", "1/2").Where(ch => !char.IsWhiteSpace(ch) && ch != '#').ToArray()).ToLowerInvariant();
        return RxLeadingZeros.Replace(s, string.Empty);
    }

    private static string? MostCommon(IEnumerable<string?> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).GroupBy(v => v!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

    private sealed record SeriesRow(int Id, string Name, string? Publisher, int? GcdSeriesId, GcdMatchSourceKind? GcdMatchSource);

    private sealed record IssueRow(int Id, int SeriesId, string? Number, int? Year, string? Publisher, int? GcdIssueId);
}
