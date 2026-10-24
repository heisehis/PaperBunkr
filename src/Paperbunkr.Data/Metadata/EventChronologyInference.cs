using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// One relation the smart connector worked out. <see cref="IsStrong"/> ones are saved as inferred relations; weak ones are only
/// offered as suggestions. Source/Target/Type follow <see cref="EventChronology"/>'s direction conventions.
/// </summary>
public sealed record InferredEventRelation(int SourceEventId, int TargetEventId, RelationType Type, bool IsStrong, decimal Confidence, string Reason)
{
    public (int Low, int High) Pair => SourceEventId < TargetEventId ? (SourceEventId, TargetEventId) : (TargetEventId, SourceEventId);
}

/// <summary>
/// The smart connector's local half (docs/superpowers/specs/2026-09-27-continuity-map-design.md §2): which event comes before which,
/// read from the library itself. Strong: direct continuation (a shared series picks up at the next issue number), a name pattern
/// naming another event ("Prelude to X", "X: Aftermath"), shared-series order. The weak guesses (date order plus a shared name word, the
/// same name years apart, overlapping-date Crossover) were dropped on 2026-10-12: date order alone is placement, not a link
/// (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4.2). Pairs you related yourself and dismissed pairs are left alone.
/// </summary>
public static class EventChronologyInference
{
    public const decimal ContinuationConfidence = 0.9m;
    public const decimal NamePatternConfidence = 0.85m;
    public const decimal SharedOrderConfidence = 0.75m;

    private static readonly Regex[] PrequelPatterns =
    {
        new(@"^\s*(?:prelude|road|countdown|lead[\s-]?in)\s+to\s+(?<x>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(?<x>.+?)\s*(?::|\s[-–—])\s*(?:prelude|prologue)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private static readonly Regex[] SequelPatterns =
    {
        new(@"^\s*(?<x>.+?)\s*(?::|\s[-–—])\s*(?:aftermath|fallout|aftershocks?|epilogue)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(?:aftermath|fallout)\s+of\s+(?<x>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private sealed record Member(int IssueId, int SeriesId, string SeriesName, float? Number, string NumberLabel, int? Date);

    private sealed record Snapshot(int Id, string Name, HashSet<string> Keys, int? Start, int? End, List<Member> Members)
    {
        public HashSet<int> IssueIds { get; } = Members.Select(m => m.IssueId).ToHashSet();
    }

    /// <summary>All inferences library-wide, or only those touching <paramref name="focusEventId"/> (the Related events panel).</summary>
    public static IReadOnlyList<InferredEventRelation> Infer(PaperbunkrDbContext context, int? focusEventId = null, Gcd.GcdDataStore? gcd = null)
    {
        var events = LoadSnapshots(context, gcd);
        if (events.Count < 2)
        {
            return Array.Empty<InferredEventRelation>();
        }

        var byId = events.ToDictionary(e => e.Id);
        var excluded = ExcludedPairs(context);

        var results = new List<InferredEventRelation>();
        void Add(InferredEventRelation? r)
        {
            if (r is null || excluded.Contains(r.Pair))
            {
                return;
            }

            if (focusEventId is int focus && r.SourceEventId != focus && r.TargetEventId != focus)
            {
                return;
            }

            results.Add(r);
        }

        // Name patterns: resolved against every event, not just candidate pairs.
        foreach (var e in events)
        {
            Add(FromNamePattern(e, events));
        }

        foreach (var (low, high) in CandidatePairs(events))
        {
            var a = byId[low];
            var b = byId[high];
            Add(FromSharedSeries(a, b) ?? FromSharedSeries(b, a));
        }

        // One relation per pair: strong beats weak, then the higher confidence.
        return results
            .GroupBy(r => r.Pair)
            .Select(g => g.OrderByDescending(r => r.IsStrong).ThenByDescending(r => r.Confidence).First())
            .OrderBy(r => r.Pair)
            .ToList();
    }

    /// <summary>Pairs never inferred: ones with a relation you set yourself, and dismissed ones.</summary>
    private static HashSet<(int, int)> ExcludedPairs(PaperbunkrDbContext context)
    {
        var set = context.EventRelations.AsNoTracking()
            .Where(r => r.Evidence.Any(e => e.Provider == RelationEvidenceProvider.User || e.Provider == RelationEvidenceProvider.Other) || !r.Evidence.Any())
            .Select(r => new { r.SourceEventId, r.TargetEventId })
            .AsEnumerable()
            .Select(r => r.SourceEventId < r.TargetEventId ? (r.SourceEventId, r.TargetEventId) : (r.TargetEventId, r.SourceEventId))
            .ToHashSet();
        foreach (var d in context.EventRelationDismissals.AsNoTracking().Select(d => new { d.LowerEventId, d.HigherEventId }).ToList())
        {
            set.Add((d.LowerEventId, d.HigherEventId));
        }

        return set;
    }

    private static IEnumerable<(int, int)> CandidatePairs(List<Snapshot> events)
    {
        var pairs = new HashSet<(int, int)>();
        void AddGroup(IEnumerable<int> ids)
        {
            var list = ids.Distinct().OrderBy(i => i).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    pairs.Add((list[i], list[j]));
                }
            }
        }

        foreach (var g in events.SelectMany(e => e.Members.Select(m => (m.SeriesId, e.Id))).GroupBy(x => x.SeriesId)) AddGroup(g.Select(x => x.Id));
        return pairs;
    }

    private static InferredEventRelation? FromNamePattern(Snapshot e, List<Snapshot> all)
    {
        foreach (var (patterns, type) in new[] { (PrequelPatterns, RelationType.Prequel), (SequelPatterns, RelationType.Sequel) })
        {
            foreach (var pattern in patterns)
            {
                var match = pattern.Match(e.Name);
                if (!match.Success)
                {
                    continue;
                }

                var keys = EventNameKeys.For(match.Groups["x"].Value, e.Members.Select(m => m.SeriesName).Distinct().ToList());
                var targets = all.Where(o => o.Id != e.Id && o.Keys.Overlaps(keys)).ToList();
                if (targets.Count == 1)
                {
                    string label = type == RelationType.Prequel ? "prequel" : "sequel";
                    return new InferredEventRelation(e.Id, targets[0].Id, type, IsStrong: true, NamePatternConfidence,
                        $"The name “{e.Name}” marks it as the {label} of {targets[0].Name}");
                }
            }
        }

        return null;
    }

    /// <summary><paramref name="later"/> after <paramref name="earlier"/> in every shared series: Continuation when one picks up at the next number, else Sequel.</summary>
    private static InferredEventRelation? FromSharedSeries(Snapshot earlier, Snapshot later)
    {
        if (earlier.IssueIds.Overlaps(later.IssueIds))
        {
            return null;
        }

        if (earlier.Start is int es && later.Start is int ls && ls < es)
        {
            return null;       // dates disagree
        }

        var shared = earlier.Members.Select(m => m.SeriesId).Intersect(later.Members.Select(m => m.SeriesId)).ToList();
        if (shared.Count == 0)
        {
            return null;
        }

        string? continuation = null;
        var spans = new List<string>();
        foreach (int seriesId in shared)
        {
            var a = earlier.Members.Where(m => m.SeriesId == seriesId && m.Number is not null).OrderBy(m => m.Number).ToList();
            var b = later.Members.Where(m => m.SeriesId == seriesId && m.Number is not null).OrderBy(m => m.Number).ToList();
            if (a.Count == 0 || b.Count == 0)
            {
                continue;
            }

            if (a[^1].Number >= b[0].Number)
            {
                return null;       // this series doesn't have all of `earlier` before all of `later`
            }

            string series = a[0].SeriesName;
            if (b[0].Number == a[^1].Number + 1 && continuation is null)
            {
                continuation = $"{series} #{a[^1].NumberLabel} → #{b[0].NumberLabel}";
            }

            spans.Add($"{series} #{a[0].NumberLabel}–{a[^1].NumberLabel}, then #{b[0].NumberLabel}–{b[^1].NumberLabel}");
        }

        if (spans.Count == 0)
        {
            return null;
        }

        return continuation is not null
            ? new InferredEventRelation(later.Id, earlier.Id, RelationType.Continuation, true, ContinuationConfidence, $"Continues straight on: {continuation}")
            : new InferredEventRelation(later.Id, earlier.Id, RelationType.Sequel, true, SharedOrderConfidence, string.Join("; ", spans));
    }

    /// <param name="gcd">When given, matched issues are dated by GCD's on-sale dates (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4).</param>
    private static List<Snapshot> LoadSnapshots(PaperbunkrDbContext context, Gcd.GcdDataStore? gcd)
    {
        var rows = context.StoryEvents.AsNoTracking()
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.StartDate,
                e.EndDate,
                AliasKeys = e.Aliases.Select(a => a.Key).ToList(),
                Members = e.Members.Select(m => new
                {
                    m.IssueId,
                    m.Issue!.SeriesId,
                    SeriesName = m.Issue.Series!.Name,
                    m.Issue.Number,
                    m.Issue.Year,
                    m.Issue.Month,
                    m.Issue.GcdIssueId,
                }).ToList(),
            })
            .ToList();
        var gcdDates = gcd?.DateKeysFor(rows.SelectMany(r => r.Members).Select(m => m.GcdIssueId).OfType<int>());

        return rows.Select(r =>
        {
            var members = r.Members.Select(m => new Member(
                m.IssueId, m.SeriesId, m.SeriesName ?? string.Empty, ParseNumber(m.Number), (m.Number ?? string.Empty).Trim(),
                EventChronology.IssueDateKey(m.Year, m.Month, m.GcdIssueId, gcdDates))).ToList();
            var dates = members.Select(m => m.Date).OfType<int>().ToList();
            int? start = EventChronology.DateKey(r.StartDate) ?? (dates.Count > 0 ? dates.Min() : null);
            int? end = EventChronology.DateKey(r.EndDate) ?? (dates.Count > 0 ? dates.Max() : null);
            var series = members.Select(m => m.SeriesName).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new Snapshot(r.Id, r.Name, EventNameKeys.For(r.Name, series, r.AliasKeys), start, end, members);
        }).ToList();
    }

    private static float? ParseNumber(string? number) =>
        float.TryParse((number ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : null;
}
