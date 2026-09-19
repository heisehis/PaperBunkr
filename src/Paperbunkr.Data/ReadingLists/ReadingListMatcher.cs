using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>
/// Resolves a reading-list entry's match key (Series/Number/Volume/Year/Format) against the
/// library — shared by CBL import, CSV import, and the manual "Add Issue" search
/// (docs/superpowers/specs/2026-08-06-reading-lists-design.md §3). Series+Number match
/// case-insensitively; Volume/Year/Format narrow only when a series+number pair is ambiguous —
/// same shape as CE's own <c>ComicInfo.SeriesEquals</c>-based matching and CBLManager's
/// <c>ArcMatcher.FuzzyMatch</c>, confirmed via both sources rather than invented. Evaluated
/// in-memory over the materialized issue set, matching the same architecture choice
/// <c>SmartListQueryBuilder</c> made (and for the same reason — personal-library scale, avoids
/// SQL-translation risk for string comparisons).
/// </summary>
public static class ReadingListMatcher
{
    /// <summary>Read-only lookup — used by the manual "Add Issue" search, which only ever offers issues already in the library.</summary>
    public static Issue? FindExisting(
        PaperbunkrDbContext context, string seriesName, string number, string? volume = null, int? year = null, string? format = null)
    {
        var series = FindSeriesByCascade(context, seriesName);
        if (series is null)
        {
            return null;
        }

        var candidates = context.Issues
            .Include(i => i.Series)
            .Include(i => i.MetadataProposals)
            .Where(i => i.SeriesId == series.Id)
            .AsEnumerable()
            .Where(i => string.Equals(i.EffectiveNumber(), number, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Prefer a real Issue over a placeholder standing in for the same Series+Number - matters
        // for arc Refresh (docs/superpowers/specs/2026-08-22-cbl-manager-arc-lookup-design.md §4),
        // where a placeholder created by an earlier Create/Refresh and a genuinely-added real copy
        // can both exist as separate Issue rows for the same key.
        return Narrow(candidates, volume, year, format)
            .OrderBy(i => i.IsPlaceholder ? 1 : 0)
            .FirstOrDefault();
    }

    /// <summary>
    /// Used by CBL/CSV import: falls back to creating the <see cref="Series"/> (if needed) and a
    /// placeholder <see cref="Issue"/> (<see cref="Issue.FileIsMissing"/> and
    /// <see cref="Issue.IsPlaceholder"/> both true) when <see cref="FindExisting"/> comes back empty.
    /// </summary>
    public static Issue ResolveOrCreatePlaceholder(
        PaperbunkrDbContext context, string seriesName, string number, string? volume = null, int? year = null, string? format = null)
    {
        var existing = FindExisting(context, seriesName, number, volume, year, format);
        if (existing is not null)
        {
            return existing;
        }

        var series = FindSeriesByCascade(context, seriesName)
            ?? CreateSeries(context, seriesName);

        var placeholder = new Issue
        {
            SeriesId = series.Id,
            Number = number,
            Volume = volume,
            Year = year,
            Format = format,
            FileIsMissing = true,
            IsPlaceholder = true,
        };
        context.Issues.Add(placeholder);
        context.SaveChanges();
        return placeholder;
    }

    /// <summary>
    /// Additive overload for the manual "add a physical book" Library-screen entry point
    /// (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md §3) -
    /// the 5-argument overload above is untouched, so CBL/CSV import behavior doesn't change.
    /// <paramref name="wasCreated"/> is <see langword="false"/> when <see cref="FindExisting"/>
    /// matched something already in the library (a real book or an existing placeholder) - callers
    /// must only treat the row as safely deletable when this is <see langword="true"/>.
    /// </summary>
    public static Issue ResolveOrCreatePlaceholder(
        PaperbunkrDbContext context, string seriesName, string number, string? volume, int? year, string? format, out bool wasCreated)
    {
        var existing = FindExisting(context, seriesName, number, volume, year, format);
        if (existing is not null)
        {
            wasCreated = false;
            return existing;
        }

        wasCreated = true;
        return ResolveOrCreatePlaceholder(context, seriesName, number, volume, year, format);
    }

    /// <summary>
    /// Exact case-insensitive match first; on a miss, retries every existing series name through
    /// <see cref="TitleNormalizer.NamesMatch"/> (docs/superpowers/specs/2026-09-17-series-name-
    /// matching-and-empty-row-cleanup-design.md) - in-memory, same "personal-library scale" choice
    /// this file's own header comment already documents for <see cref="Narrow"/>. Prevents a CBL/CSV
    /// import naming a punctuation-variant of an already-owned series (<c>"X: Y"</c> vs
    /// <c>"X - Y"</c>) from silently spawning a duplicate placeholder series.
    /// </summary>
    private static Series? FindSeriesByCascade(PaperbunkrDbContext context, string seriesName)
    {
        var all = context.Series.ToList();
        return all.FirstOrDefault(s => string.Equals(s.Name, seriesName, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(s => TitleNormalizer.NamesMatch(s.Name, seriesName));
    }

    private static Series CreateSeries(PaperbunkrDbContext context, string seriesName)
    {
        var series = new Series { Name = seriesName, SortName = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        return series;
    }

    private static IEnumerable<Issue> Narrow(List<Issue> candidates, string? volume, int? year, string? format)
    {
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        var narrowed = candidates;
        if (!string.IsNullOrEmpty(volume))
        {
            var byVolume = narrowed.Where(i => string.Equals(i.EffectiveVolume(), volume, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byVolume.Count > 0)
            {
                narrowed = byVolume;
            }
        }

        if (narrowed.Count > 1 && year is int y)
        {
            var byYear = narrowed.Where(i => i.EffectiveYear().HasValue && Math.Abs(i.EffectiveYear()!.Value - y) <= 1).ToList();
            if (byYear.Count > 0)
            {
                narrowed = byYear;
            }
        }

        if (narrowed.Count > 1 && !string.IsNullOrEmpty(format))
        {
            var byFormat = narrowed.Where(i => string.Equals(i.Format, format, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byFormat.Count > 0)
            {
                narrowed = byFormat;
            }
        }

        return narrowed;
    }
}
