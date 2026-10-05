using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

/// <summary>A publisher the library names that has no logo to draw (it falls back to a letter chip or plain text).</summary>
public sealed record PublisherWithoutLogo(string Name, int Issues);

/// <summary>A series whose issues are missing a publisher, with the one its other issues name (null when none, or when two are tied).</summary>
public sealed record SeriesMissingPublisher(int SeriesId, string SeriesName, int IssuesMissing, string? Suggested);

public sealed record PublisherCoverageReport(
    int PublishersTotal,
    int PublishersWithLogo,
    IReadOnlyList<PublisherWithoutLogo> WithoutLogo,
    int IssuesWithoutPublisher,
    IReadOnlyList<SeriesMissingPublisher> SeriesMissing)
{
    /// <summary>Issues a fill from the series' other issues would set.</summary>
    public int FillableIssues => SeriesMissing.Where(s => s.Suggested is not null).Sum(s => s.IssuesMissing);
}

/// <summary>
/// The publisher gaps Library Health reports (docs/superpowers/specs/2026-10-04-publisher-icons-user-folder-and-gaps-design.md): which publishers in
/// the library have no logo, and which issues have no publisher at all. An issue's publisher is <see cref="Issue.Publisher"/>, falling back to
/// <see cref="Series.Publisher"/> (only populated for CE-migrated series) - the same order the rest of the app reads it in.
/// </summary>
public static class PublisherCoverage
{
    public static PublisherCoverageReport Scan(PaperbunkrDbContext context, Func<string, bool> hasLogo)
    {
        var seriesPublisher = context.Series.AsNoTracking().Select(s => new { s.Id, s.Name, s.Publisher }).ToDictionary(s => s.Id);
        var issues = context.Issues.AsNoTracking().Select(i => new { i.SeriesId, i.Publisher }).ToList();

        var perPublisher = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var missingPerSeries = new Dictionary<int, int>();
        var namedPerSeries = new Dictionary<int, Dictionary<string, int>>();
        int missing = 0;

        foreach (var issue in issues)
        {
            string? own = Clean(issue.Publisher);
            string? effective = own ?? (seriesPublisher.TryGetValue(issue.SeriesId, out var s) ? Clean(s.Publisher) : null);
            if (effective is null)
            {
                missing++;
                missingPerSeries[issue.SeriesId] = missingPerSeries.GetValueOrDefault(issue.SeriesId) + 1;
                continue;
            }

            perPublisher[effective] = perPublisher.GetValueOrDefault(effective) + 1;
            if (own is not null)
            {
                var named = namedPerSeries.TryGetValue(issue.SeriesId, out var d) ? d : namedPerSeries[issue.SeriesId] = new(StringComparer.OrdinalIgnoreCase);
                named[own] = named.GetValueOrDefault(own) + 1;
            }
        }

        var withoutLogo = perPublisher.Where(p => !hasLogo(p.Key))
            .OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PublisherWithoutLogo(p.Key, p.Value))
            .ToList();

        var series = missingPerSeries
            .Select(m => new SeriesMissingPublisher(
                m.Key, seriesPublisher.TryGetValue(m.Key, out var info) ? info.Name : "(unknown series)", m.Value,
                Suggest(namedPerSeries.GetValueOrDefault(m.Key))))
            .OrderByDescending(s => s.Suggested is not null).ThenByDescending(s => s.IssuesMissing)
            .ThenBy(s => s.SeriesName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PublisherCoverageReport(perPublisher.Count, perPublisher.Count - withoutLogo.Count, withoutLogo, missing, series);
    }

    /// <summary>For every series that has issues with and without a publisher, gives the blank ones the publisher the rest of the series names
    /// (the most common one; a tie is ambiguous and is skipped). Never overwrites an existing publisher and never touches a series with no named issue.
    /// Returns how many issues were set. Database only - the files are not rewritten.</summary>
    public static int FillFromSiblings(PaperbunkrDbContext context)
    {
        var report = Scan(context, _ => true);
        int updated = 0;
        foreach (var group in report.SeriesMissing.Where(s => s.Suggested is not null).GroupBy(s => s.Suggested!, StringComparer.OrdinalIgnoreCase))
        {
            var publisher = PublisherResolver.GetOrCreate(context, group.Key);
            var seriesIds = group.Select(s => s.SeriesId).ToHashSet();
            var blanks = context.Issues
                .Where(i => seriesIds.Contains(i.SeriesId) && (i.Publisher == null || i.Publisher.Trim() == string.Empty))
                .ToList();
            foreach (var issue in blanks)
            {
                issue.Publisher = publisher.Name;
                issue.PublisherEntityId = publisher.Id;
                updated++;
            }
        }

        context.SaveChanges();
        return updated;
    }

    private static string? Suggest(Dictionary<string, int>? named)
    {
        if (named is null || named.Count == 0)
        {
            return null;
        }

        var ordered = named.OrderByDescending(n => n.Value).ToList();
        return ordered.Count > 1 && ordered[0].Value == ordered[1].Value ? null : ordered[0].Key;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
