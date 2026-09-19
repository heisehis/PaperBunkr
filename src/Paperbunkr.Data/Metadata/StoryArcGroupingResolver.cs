using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One issue's proposed position within a <see cref="StoryEventCandidate"/>, from local <see cref="Issue.StoryArcNumber"/> pairing (null when unavailable).</summary>
public sealed record StoryEventCandidateMember(Issue Issue, int? Position);

/// <summary>
/// One reviewable "these issues look like a story event" candidate, grouped purely from local
/// metadata (docs/superpowers/specs/2026-09-17-storyevent-continuity-autopopulate-design.md) -
/// "propose, don't assert", same posture as <see cref="EventSuggestionResolver.EventSuggestion"/>.
/// Creates nothing; the Story Events screen turns a candidate into a real <see cref="StoryEvent"/>
/// only on the user's explicit Accept.
/// </summary>
public sealed record StoryEventCandidate(
    string ArcName,
    string Publisher,
    IReadOnlyList<StoryEventCandidateMember> Members,
    FormatSignalStrength Strength,
    string Reason,
    string? ComicVineArcId = null,
    string? MetronArcId = null);

/// <summary>
/// Groups already-tagged <see cref="Issue.StoryArc"/> values into <see cref="StoryEventCandidate"/>s
/// (docs/superpowers/specs/2026-09-17-storyevent-continuity-autopopulate-design.md, Phase 1). Local-
/// data-only: works with zero network access, zero credentials. External verification against
/// ComicVine/Metron is a separate, strictly additive step - see <see cref="ArcExternalVerificationService"/>.
/// </summary>
internal static class StoryArcGroupingResolver
{
    /// <summary>
    /// Small, deliberately narrow alias table scoped to this resolver only - confirmed via repo-wide
    /// grep that no publisher normalization exists anywhere else in this codebase
    /// (<c>Issue.Publisher</c>/<c>Series.Publisher</c> are read as raw strings everywhere else,
    /// including <c>SmartListCatalog</c>/<c>SeriesSmartListCatalog</c>). Not promoted to a shared
    /// entity until a second caller actually needs one.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PublisherAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Marvel Comics"] = "Marvel",
        ["Marvel Worldwide"] = "Marvel",
        ["Marvel Worldwide, Inc."] = "Marvel",
        ["DC Comics"] = "DC",
    };

    /// <summary>Trims and collapses known variants to one canonical form; unknown values pass through trimmed/unchanged.</summary>
    public static string NormalizePublisher(string? publisher)
    {
        string trimmed = (publisher ?? string.Empty).Trim();
        return trimmed.Length == 0 ? string.Empty : PublisherAliases.GetValueOrDefault(trimmed, trimmed);
    }

    /// <summary>
    /// Splits <see cref="Issue.StoryArc"/> and <see cref="Issue.StoryArcNumber"/> on comma in
    /// parallel, pairing token <c>N</c> of one with token <c>N</c> of the other by index - the real
    /// ComicInfo.xml multi-arc convention this codebase never implemented before (confirmed by
    /// repo-wide grep: no existing split logic on either field). Ragged lists (arc name with no
    /// matching number, or vice versa) pair with a <see langword="null"/> position rather than
    /// throwing or silently misaligning.
    /// </summary>
    private static IReadOnlyList<(string ArcName, int? Position)> SplitArcs(string? storyArc, string? storyArcNumber)
    {
        var names = (storyArc ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var numbers = (storyArcNumber ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = new List<(string, int?)>(names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            int? position = i < numbers.Length && int.TryParse(numbers[i], out int parsed) ? parsed : null;
            result.Add((names[i], position));
        }

        return result;
    }

    /// <summary>
    /// Groups the library's already-tagged issues into candidate story events. A group needs at
    /// least 2 issues - a single issue carrying a unique arc name is that issue's own story title,
    /// not a multi-issue event. Excludes issues already members of an existing <see cref="StoryEvent"/>
    /// whose name matches the group's arc name, and groups matching a persisted
    /// <see cref="StoryEventCandidateDismissal"/>.
    /// </summary>
    public static IReadOnlyList<StoryEventCandidate> GetCandidates(PaperbunkrDbContext context)
    {
        var dismissed = context.StoryEventCandidateDismissals
            .Select(d => new { d.ArcName, d.Publisher })
            .AsEnumerable()
            .Select(d => (ArcName: d.ArcName.ToLowerInvariant(), Publisher: d.Publisher.ToLowerInvariant()))
            .ToHashSet();

        // Existing StoryEvents by lowercased name -> the issue ids already members, so an issue
        // that's already tracked under this arc name isn't proposed again. Grouped rather than
        // ToDictionary'd: two StoryEvents can legitimately share a name (the Story Events screen's
        // own "New" button creates repeated "New Story Event" rows), and ToDictionary would throw on
        // the duplicate key and take the whole scan down with it.
        var existingMemberIssueIdsByName = context.StoryEvents
            .Select(e => new { e.Name, MemberIssueIds = e.Members.Select(m => m.IssueId).ToList() })
            .AsEnumerable()
            .GroupBy(e => e.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => (IReadOnlySet<int>)g.SelectMany(e => e.MemberIssueIds).ToHashSet());

        var groups = new Dictionary<(string ArcKey, string PublisherKey), (string ArcName, string Publisher, List<StoryEventCandidateMember> Members)>();

        var issuesWithArc = context.Issues.Where(i => i.StoryArc != null && i.StoryArc != string.Empty).AsEnumerable();
        foreach (var issue in issuesWithArc)
        {
            string publisher = NormalizePublisher(issue.Publisher);
            foreach (var (arcName, position) in SplitArcs(issue.StoryArc, issue.StoryArcNumber))
            {
                string arcKey = arcName.ToLowerInvariant();
                string publisherKey = publisher.ToLowerInvariant();

                if (dismissed.Contains((arcKey, publisherKey)))
                {
                    continue;
                }

                if (existingMemberIssueIdsByName.TryGetValue(arcKey, out var memberIds) && memberIds.Contains(issue.Id))
                {
                    continue;
                }

                var key = (arcKey, publisherKey);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = (arcName, publisher, new List<StoryEventCandidateMember>());
                    groups[key] = group;
                }

                group.Members.Add(new StoryEventCandidateMember(issue, position));
            }
        }

        return groups.Values
            .Where(g => g.Members.Count >= 2)
            .Select(g => new StoryEventCandidate(
                g.ArcName,
                g.Publisher,
                g.Members.OrderBy(m => m.Position ?? int.MaxValue).ThenBy(m => m.Issue.Year ?? int.MaxValue).ToList(),
                FormatSignalStrength.Weak,
                $"{g.Members.Count} issues tagged with Story Arc \"{g.ArcName}\""))
            .OrderByDescending(c => c.Members.Count)
            .ThenBy(c => c.ArcName)
            .ToList();
    }

    /// <summary>Persists "never suggest this arc as a story event again" - idempotent.</summary>
    public static void Dismiss(PaperbunkrDbContext context, string arcName, string publisher)
    {
        bool already = context.StoryEventCandidateDismissals.Any(d => d.ArcName == arcName && d.Publisher == publisher);
        if (already)
        {
            return;
        }

        context.StoryEventCandidateDismissals.Add(new StoryEventCandidateDismissal { ArcName = arcName, Publisher = publisher });
        context.SaveChanges();
    }

    /// <summary>Clears a persisted dismissal so the arc can be suggested again - a no-op if it isn't dismissed.</summary>
    public static void Restore(PaperbunkrDbContext context, string arcName, string publisher)
    {
        var row = context.StoryEventCandidateDismissals.FirstOrDefault(d => d.ArcName == arcName && d.Publisher == publisher);
        if (row is not null)
        {
            context.StoryEventCandidateDismissals.Remove(row);
            context.SaveChanges();
        }
    }

    public static IReadOnlyList<StoryEventCandidateDismissal> GetDismissed(PaperbunkrDbContext context) =>
        context.StoryEventCandidateDismissals.OrderByDescending(d => d.DismissedAt).ToList();
}
