using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Creator and publisher affinity (docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md §3): who you actually like, ranked by
/// <c>average rating x completion rate</c> rather than by how much of them you happen to own (which is what Top Lists already shows).
/// Creators come from the normalized <see cref="CreatorCredit"/> index so spelling variants merge. Comics and manga only - books carry no
/// rating or credits. The pitch's "unowned works by top creators" half is deliberately absent: nothing local knows a creator's unowned works.
/// </summary>
public static class AffinityResolver
{
    public const int MinSeries = 3;

    public const int MinRatedIssues = 3;

    public const int MaxEntries = 10;

    private static readonly HashSet<string> AuthorRoles = new(StringComparer.Ordinal) { "Writer" };

    private static readonly HashSet<string> ArtistRoles = new(StringComparer.Ordinal) { "Penciller", "Inker", "Colorist" };

    public static AffinityData Build(PaperbunkrDbContext context)
    {
        var credits = context.CreatorCredits.AsNoTracking()
            .Include(c => c.Creator)
            .Include(c => c.Issue!).ThenInclude(i => i.Series)
            .Where(c => c.Issue != null && !c.Issue.IsPlaceholder && c.Creator != null)
            .ToList();

        var issues = new Dictionary<int, AffinityIssue>();
        AffinityIssue Map(Issue i)
        {
            if (!issues.TryGetValue(i.Id, out var mapped))
            {
                mapped = new AffinityIssue(i.Id, i.SeriesId, i.Rating, i.HasBeenRead(), i.Publisher ?? i.Series?.Publisher);
                issues[i.Id] = mapped;
            }

            return mapped;
        }

        var rows = credits.Select(c => new AffinityCredit(c.CreatorId, c.Creator!.Name, c.Role ?? string.Empty, Map(c.Issue!))).ToList();

        // Publisher affinity needs every issue, not only credited ones.
        foreach (var i in context.Issues.AsNoTracking().Include(i => i.Series).Where(i => !i.IsPlaceholder).ToList())
        {
            Map(i);
        }

        return Compute(rows, issues.Values.ToList());
    }

    internal static AffinityData Compute(IReadOnlyCollection<AffinityCredit> credits, IReadOnlyCollection<AffinityIssue> allIssues)
    {
        var authors = RankCreators(credits.Where(c => AuthorRoles.Contains(c.Role)));
        var artists = RankCreators(credits.Where(c => ArtistRoles.Contains(c.Role)));

        var publishers = allIssues
            .Where(i => !string.IsNullOrWhiteSpace(i.Publisher))
            .GroupBy(i => i.Publisher!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => Entry(g.Key, g.ToList()))
            .Where(e => e is not null)
            .Select(e => e!)
            .OrderByDescending(e => e.Score).ThenByDescending(e => e.OwnedIssues).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxEntries)
            .ToList();

        return new AffinityData(authors, artists, publishers);
    }

    private static IReadOnlyList<AffinityEntry> RankCreators(IEnumerable<AffinityCredit> credits) => credits
        .GroupBy(c => c.CreatorId)
        .Select(g => Entry(g.First().Name, g.Select(c => c.Issue).DistinctBy(i => i.IssueId).ToList()))
        .Where(e => e is not null)
        .Select(e => e!)
        .OrderByDescending(e => e.Score).ThenByDescending(e => e.OwnedIssues).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .Take(MaxEntries)
        .ToList();

    private static AffinityEntry? Entry(string name, List<AffinityIssue> issues)
    {
        int series = issues.Select(i => i.SeriesId).Distinct().Count();
        var rated = issues.Where(i => i.Rating is > 0).ToList();
        if (series < MinSeries || rated.Count < MinRatedIssues)
        {
            return null;
        }

        double avg = rated.Average(i => (double)i.Rating!.Value);
        double completion = (double)issues.Count(i => i.Read) / issues.Count;
        return new AffinityEntry(name, avg * completion, avg, completion, series, issues.Count);
    }
}

internal sealed record AffinityIssue(int IssueId, int SeriesId, float? Rating, bool Read, string? Publisher);

internal sealed record AffinityCredit(int CreatorId, string Name, string Role, AffinityIssue Issue);

public sealed record AffinityEntry(string Name, double Score, double AverageRating, double CompletionRate, int Series, int OwnedIssues);

public sealed record AffinityData(
    IReadOnlyList<AffinityEntry> Authors, IReadOnlyList<AffinityEntry> Artists, IReadOnlyList<AffinityEntry> Publishers)
{
    public bool HasData => Authors.Count + Artists.Count + Publishers.Count > 0;

    public static AffinityData Empty { get; } = new(Array.Empty<AffinityEntry>(), Array.Empty<AffinityEntry>(), Array.Empty<AffinityEntry>());
}
