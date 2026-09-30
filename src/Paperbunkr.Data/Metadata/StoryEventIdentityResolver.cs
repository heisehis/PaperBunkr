using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>Why two story events look like the same arc, strongest first.</summary>
public enum DuplicateEvidence
{
    SharedProviderId,
    NameAndOverlap,
    NameOnly,
    OverlapOnly,
}

/// <summary>A possible duplicate pair. <see cref="IsSilent"/> pairs are merged without asking; the rest go to the review list.</summary>
public sealed record DuplicatePair(
    int EventAId, string EventAName, int EventBId, string EventBName,
    DuplicateEvidence Evidence, bool IsSilent, string Reason, string KeptName);

/// <summary>One row of the Story Events sidebar's "Possible duplicates": a pair, or a single event whose sources disagree.</summary>
public sealed record StoryEventReviewItem(
    int EventAId, string EventAName, int? EventBId, string? EventBName, string Reason, string? KeptName)
{
    public bool IsConflictOnly => EventBId is null;
}

/// <summary>
/// Finds story events that are the same arc (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §2). Local only - no
/// network; <see cref="StoryEventIdCompletion"/> fills the provider ids this reads. Candidates come only from shared ids, shared name
/// keys and shared member issues, never an all-against-all comparison.
/// </summary>
public static class StoryEventIdentityResolver
{
    /// <summary>Shared issues ÷ the smaller event's member count needed for "overlap".</summary>
    public const double OverlapThreshold = 0.5;

    private sealed record Snapshot(
        int Id, string Name, StoryEventOrigin Origin, string? ComicVineId, string? MetronId, string? Conflict,
        HashSet<string> Keys, HashSet<int> IssueIds, List<string> SeriesNames);

    public static IReadOnlyList<DuplicatePair> FindPairs(PaperbunkrDbContext context)
    {
        var events = LoadSnapshots(context);
        var byId = events.ToDictionary(e => e.Id);
        var dismissed = context.StoryEventDuplicateDismissals
            .Select(d => new { d.LowerEventId, d.HigherEventId })
            .AsEnumerable()
            .Select(d => (d.LowerEventId, d.HigherEventId))
            .ToHashSet();

        var candidates = new HashSet<(int, int)>();
        void AddGroup(IEnumerable<int> ids)
        {
            var list = ids.Distinct().OrderBy(i => i).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    candidates.Add((list[i], list[j]));
                }
            }
        }

        foreach (var g in events.Where(e => e.ComicVineId is not null).GroupBy(e => e.ComicVineId)) AddGroup(g.Select(e => e.Id));
        foreach (var g in events.Where(e => e.MetronId is not null).GroupBy(e => e.MetronId)) AddGroup(g.Select(e => e.Id));
        foreach (var g in events.SelectMany(e => e.Keys.Select(k => (Key: k, e.Id))).GroupBy(x => x.Key)) AddGroup(g.Select(x => x.Id));
        foreach (var g in events.SelectMany(e => e.IssueIds.Select(i => (Issue: i, e.Id))).GroupBy(x => x.Issue)) AddGroup(g.Select(x => x.Id));

        var pairs = new List<DuplicatePair>();
        foreach (var (lowId, highId) in candidates.OrderBy(p => p))
        {
            if (dismissed.Contains((lowId, highId)) || Evaluate(byId[lowId], byId[highId]) is not { } pair)
            {
                continue;
            }

            pairs.Add(pair);
        }

        return pairs;
    }

    /// <summary>The review list: every non-silent pair, plus events whose sources disagree about their own ids.</summary>
    public static IReadOnlyList<StoryEventReviewItem> FindReviewItems(PaperbunkrDbContext context)
    {
        var items = FindPairs(context)
            .Where(p => !p.IsSilent)
            .Select(p => new StoryEventReviewItem(p.EventAId, p.EventAName, p.EventBId, p.EventBName, p.Reason, p.KeptName))
            .ToList();

        foreach (var e in context.StoryEvents.AsNoTracking().Where(e => e.IdentityConflict != null).OrderBy(e => e.Name).ToList())
        {
            items.Add(new StoryEventReviewItem(e.Id, e.Name, null, null, $"Sources disagree: {e.IdentityConflict}", null));
        }

        return items;
    }

    /// <summary>"Not the same": never propose or merge this pair again.</summary>
    public static void Dismiss(PaperbunkrDbContext context, int eventAId, int eventBId)
    {
        int low = Math.Min(eventAId, eventBId);
        int high = Math.Max(eventAId, eventBId);
        if (context.StoryEventDuplicateDismissals.Any(d => d.LowerEventId == low && d.HigherEventId == high))
        {
            return;
        }

        context.StoryEventDuplicateDismissals.Add(new StoryEventDuplicateDismissal { LowerEventId = low, HigherEventId = high });
        context.SaveChanges();
    }

    private static DuplicatePair? Evaluate(Snapshot a, Snapshot b)
    {
        bool sameCv = a.ComicVineId is not null && a.ComicVineId == b.ComicVineId;
        bool sameMetron = a.MetronId is not null && a.MetronId == b.MetronId;
        string? disagreement =
            a.ComicVineId is not null && b.ComicVineId is not null && a.ComicVineId != b.ComicVineId ? $"ComicVine arc {a.ComicVineId} vs {b.ComicVineId}"
            : a.MetronId is not null && b.MetronId is not null && a.MetronId != b.MetronId ? $"Metron arc {a.MetronId} vs {b.MetronId}"
            : null;

        // "Secret Wars (1984)" and "Secret Wars (2015)" share a year-stripped key but name two different arcs.
        bool nameMatch = a.Keys.Overlaps(b.Keys) && !DifferentYears(a.Name, b.Name);
        int smaller = Math.Min(a.IssueIds.Count, b.IssueIds.Count);
        int shared = a.IssueIds.Count(b.IssueIds.Contains);
        bool overlap = smaller > 0 && (double)shared / smaller >= OverlapThreshold;

        DuplicateEvidence evidence;
        string reason;
        if (sameCv || sameMetron)
        {
            evidence = DuplicateEvidence.SharedProviderId;
            reason = sameCv ? "Same ComicVine arc" : "Same Metron arc";
        }
        else if (nameMatch && overlap)
        {
            evidence = DuplicateEvidence.NameAndOverlap;
            reason = $"Names match, {shared} of {smaller} issues shared";
        }
        else if (nameMatch)
        {
            evidence = DuplicateEvidence.NameOnly;
            reason = shared > 0 ? $"Names match, {shared} of {smaller} issues shared" : "Names match";
        }
        else if (overlap)
        {
            evidence = DuplicateEvidence.OverlapOnly;
            reason = $"{shared} of {smaller} issues shared";
        }
        else
        {
            return null;
        }

        if (disagreement is not null)
        {
            reason = $"Sources disagree: {disagreement}";
        }

        bool silent = evidence is DuplicateEvidence.SharedProviderId or DuplicateEvidence.NameAndOverlap
            && a.Origin == StoryEventOrigin.Provider && b.Origin == StoryEventOrigin.Provider
            && a.Conflict is null && b.Conflict is null
            && disagreement is null;

        return new DuplicatePair(a.Id, a.Name, b.Id, b.Name, evidence, silent, reason, PredictKeptName(a, b));
    }

    /// <summary>The name the merged event will carry - mirrors <see cref="StoryEventMerger"/>'s survivor and name rules.</summary>
    private static string PredictKeptName(Snapshot a, Snapshot b)
    {
        bool aSurvives = a.Origin != b.Origin
            ? a.Origin == StoryEventOrigin.User
            : a.IssueIds.Count != b.IssueIds.Count ? a.IssueIds.Count > b.IssueIds.Count : a.Id < b.Id;
        var survivor = aSurvives ? a : b;
        var loser = aSurvives ? b : a;
        if (survivor.Origin == StoryEventOrigin.User || loser.Origin == StoryEventOrigin.User)
        {
            return survivor.Name;
        }

        var series = survivor.SeriesNames.Concat(loser.SeriesNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return EventNameKeys.WithoutSeriesPrefix(survivor.Name, series) is { } stripped
               && EventNameKeys.Key(stripped) == EventNameKeys.Key(loser.Name)
            ? loser.Name
            : survivor.Name;
    }

    private static List<Snapshot> LoadSnapshots(PaperbunkrDbContext context)
    {
        var rows = context.StoryEvents.AsNoTracking()
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.Origin,
                e.ComicVineArcId,
                e.MetronArcId,
                e.IdentityConflict,
                AliasKeys = e.Aliases.Select(a => a.Key).ToList(),
                Members = e.Members.Select(m => new { m.IssueId, SeriesName = m.Issue!.Series!.Name }).ToList(),
            })
            .ToList();

        return rows.Select(r =>
        {
            var series = r.Members.Select(m => m.SeriesName).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return new Snapshot(
                r.Id, r.Name, r.Origin, Blank(r.ComicVineArcId), Blank(r.MetronArcId), r.IdentityConflict,
                EventNameKeys.For(r.Name, series, r.AliasKeys),
                r.Members.Select(m => m.IssueId).ToHashSet(),
                series);
        }).ToList();
    }

    private static string? Blank(string? id) => string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    private static readonly System.Text.RegularExpressions.Regex YearSuffix =
        new(@"\((?<y>(?:19|20)\d{2})\)\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Both names end in an explicit, different year: different arcs, whatever else matches.</summary>
    internal static bool DifferentYears(string a, string b)
    {
        var ya = YearSuffix.Match(a);
        var yb = YearSuffix.Match(b);
        return ya.Success && yb.Success && ya.Groups["y"].Value != yb.Groups["y"].Value;
    }
}
