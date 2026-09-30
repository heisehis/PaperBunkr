using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>What a merge did, for the Activity summary.</summary>
public sealed record StoryEventMergeResult(int SurvivorId, string SurvivorName, int RemovedId, string RemovedName, int IssuesAdded);

/// <summary>
/// Merges two story events that are the same arc (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §2). Everything
/// that points at the removed event is re-pointed at the survivor first - deleting an event cascades its memberships, relations
/// and dismissals, so nothing may still reference it by then. One transaction.
/// </summary>
public static class StoryEventMerger
{
    /// <summary>The User-origin event if exactly one is; else the one with more members; else the lower id.</summary>
    public static bool FirstSurvives(StoryEvent a, int aMembers, StoryEvent b, int bMembers)
    {
        if (a.Origin != b.Origin)
        {
            return a.Origin == StoryEventOrigin.User;
        }

        if (aMembers != bMembers)
        {
            return aMembers > bMembers;
        }

        return a.Id < b.Id;
    }

    public static StoryEventMergeResult Merge(PaperbunkrDbContext context, int eventAId, int eventBId)
    {
        if (eventAId == eventBId)
        {
            throw new ArgumentException("Cannot merge an event with itself.", nameof(eventBId));
        }

        using var transaction = context.Database.BeginTransaction();

        var events = context.StoryEvents
            .Include(e => e.Members).ThenInclude(m => m.Issue).ThenInclude(i => i!.Series)
            .Include(e => e.Aliases)
            .Where(e => e.Id == eventAId || e.Id == eventBId)
            .ToList();
        var a = events.Single(e => e.Id == eventAId);
        var b = events.Single(e => e.Id == eventBId);
        bool aSurvives = FirstSurvives(a, a.Members.Count, b, b.Members.Count);
        var survivor = aSurvives ? a : b;
        var loser = aSurvives ? b : a;
        string removedName = loser.Name;

        var memberSeries = survivor.Members.Concat(loser.Members)
            .Select(m => m.Issue?.Series?.Name)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        MergeNameAndAliases(context, survivor, loser, memberSeries);
        MergeFields(survivor, loser);
        int issuesAdded = MergeMembers(context, survivor, loser);
        RepointReferences(context, survivor.Id, loser.Id);

        survivor.IdentityMemberKey = null;       // members changed: re-check its ids
        survivor.UpdatedAt = DateTime.UtcNow;

        // Save the re-pointing first: removing the loser cascades immediately to whatever the change tracker still believes belongs
        // to it, and moved members must not be among them.
        context.SaveChanges();
        context.StoryEvents.Remove(loser);
        context.SaveChanges();
        transaction.Commit();

        return new StoryEventMergeResult(survivor.Id, survivor.Name, loser.Id, removedName, issuesAdded);
    }

    private static void MergeNameAndAliases(PaperbunkrDbContext context, StoryEvent survivor, StoryEvent loser, IReadOnlyList<string> memberSeries)
    {
        string oldSurvivorName = survivor.Name;
        if (survivor.Origin != StoryEventOrigin.User && loser.Origin != StoryEventOrigin.User)
        {
            // Prefer the spelling without a series prefix: "Planet Hulk" over "Hulk: Planet Hulk".
            if (EventNameKeys.WithoutSeriesPrefix(survivor.Name, memberSeries) is { } stripped
                && EventNameKeys.Key(stripped) == EventNameKeys.Key(loser.Name))
            {
                survivor.Name = loser.Name;
            }
        }
        else if (survivor.Origin != StoryEventOrigin.User && loser.Origin == StoryEventOrigin.User)
        {
            survivor.Name = loser.Name;     // unreachable with FirstSurvives, kept so a user-set name can never be lost
        }

        if (loser.Origin == StoryEventOrigin.User || survivor.Origin == StoryEventOrigin.User)
        {
            survivor.Origin = StoryEventOrigin.User;
        }

        string finalKey = EventNameKeys.Key(survivor.Name);
        var existingKeys = survivor.Aliases.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        existingKeys.Add(finalKey);

        var incoming = new List<(string Name, string Key)>();
        if (!string.Equals(oldSurvivorName, survivor.Name, StringComparison.Ordinal))
        {
            incoming.Add((oldSurvivorName, EventNameKeys.Key(oldSurvivorName)));
        }

        incoming.Add((loser.Name, EventNameKeys.Key(loser.Name)));
        incoming.AddRange(loser.Aliases.Select(x => (x.Name, x.Key)));

        foreach (var (name, key) in incoming)
        {
            if (key.Length > 0 && existingKeys.Add(key))
            {
                survivor.Aliases.Add(new StoryEventAlias { Name = name, Key = key, Source = StoryEventAliasSource.Merge });
            }
        }
    }

    private static void MergeFields(StoryEvent survivor, StoryEvent loser)
    {
        survivor.ComicVineArcId ??= loser.ComicVineArcId;
        survivor.MetronArcId ??= loser.MetronArcId;
        survivor.SpineSeriesId ??= loser.SpineSeriesId;

        if (loser.StartDate is DateTime start && (survivor.StartDate is null || start < survivor.StartDate))
        {
            survivor.StartDate = start;
        }

        if (loser.EndDate is DateTime end && (survivor.EndDate is null || end > survivor.EndDate))
        {
            survivor.EndDate = end;
        }

        if ((loser.Description?.Trim().Length ?? 0) > (survivor.Description?.Trim().Length ?? 0))
        {
            survivor.Description = loser.Description;
        }
    }

    /// <summary>Survivor order kept; the loser's own issues slotted in by cover date (undated ones appended); positions renumbered 1..n.</summary>
    private static int MergeMembers(PaperbunkrDbContext context, StoryEvent survivor, StoryEvent loser)
    {
        var order = survivor.Members.OrderBy(m => m.Position).ThenBy(m => m.Id).ToList();
        var byIssue = order.GroupBy(m => m.IssueId).ToDictionary(g => g.Key, g => g.First());
        int added = 0;
        var undated = new List<EventMembership>();

        foreach (var incoming in loser.Members.OrderBy(m => m.Position).ThenBy(m => m.Id).ToList())
        {
            if (byIssue.TryGetValue(incoming.IssueId, out var existing))
            {
                if (RoleStrength(incoming) > RoleStrength(existing))
                {
                    CopyRole(incoming, existing);
                }

                context.EventMemberships.Remove(incoming);
                continue;
            }

            incoming.StoryEventId = survivor.Id;
            byIssue[incoming.IssueId] = incoming;
            added++;

            if (CoverDate(incoming) is not int date)
            {
                undated.Add(incoming);
                continue;
            }

            order.Insert(InsertIndex(order, date), incoming);
        }

        order.AddRange(undated);
        for (int i = 0; i < order.Count; i++)
        {
            order[i].Position = i + 1;
        }

        return added;
    }

    /// <summary>After the last dated member dated on or before <paramref name="date"/>; before the first later one when none is.</summary>
    private static int InsertIndex(List<EventMembership> order, int date)
    {
        int lastOnOrBefore = -1;
        int firstAfter = -1;
        for (int i = 0; i < order.Count; i++)
        {
            if (CoverDate(order[i]) is not int d)
            {
                continue;
            }

            if (d <= date)
            {
                lastOnOrBefore = i;
            }
            else if (firstAfter < 0)
            {
                firstAfter = i;
            }
        }

        if (lastOnOrBefore >= 0)
        {
            return lastOnOrBefore + 1;
        }

        return firstAfter >= 0 ? firstAfter : order.Count;
    }

    private static int? CoverDate(EventMembership m) =>
        m.Issue?.Year is int year ? (year * 100) + (m.Issue.Month is >= 1 and <= 12 ? m.Issue.Month.Value : 0) : null;

    /// <summary>A non-Core role beats Core; between equals, a user-set role (source User or legacy null) beats a detected one.</summary>
    internal static int RoleStrength(EventMembership m) =>
        (m.Role != EventMembershipRole.Core ? 2 : 0) + (m.RoleSource != RoleAssignmentSource.Auto ? 1 : 0);

    private static void CopyRole(EventMembership from, EventMembership to)
    {
        to.Role = from.Role;
        to.RoleSource = from.RoleSource;
        to.RoleReason = from.RoleReason;
        to.SuggestedRole = from.SuggestedRole;
        to.SuggestedReason = from.SuggestedReason;
        to.RoleSuggestionDismissed = from.RoleSuggestionDismissed;
    }

    private static void RepointReferences(PaperbunkrDbContext context, int survivorId, int loserId)
    {
        // Relations: re-point, then drop self-relations and same-type duplicates (either direction).
        var relations = context.EventRelations
            .Where(r => r.SourceEventId == loserId || r.TargetEventId == loserId
                     || r.SourceEventId == survivorId || r.TargetEventId == survivorId)
            .ToList();
        foreach (var r in relations)
        {
            if (r.SourceEventId == loserId) r.SourceEventId = survivorId;
            if (r.TargetEventId == loserId) r.TargetEventId = survivorId;
        }

        var kept = new HashSet<(int, int, RelationType)>();
        foreach (var r in relations.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id))
        {
            var pair = r.SourceEventId < r.TargetEventId ? (r.SourceEventId, r.TargetEventId) : (r.TargetEventId, r.SourceEventId);
            if (r.SourceEventId == r.TargetEventId || !kept.Add((pair.Item1, pair.Item2, r.RelationType)))
            {
                context.EventRelations.Remove(r);
            }
        }

        // Issue-suggestion dismissals: unique per (event, issue).
        var survivorDismissed = context.EventSuggestionDismissals.Where(d => d.StoryEventId == survivorId).Select(d => d.IssueId).ToHashSet();
        foreach (var d in context.EventSuggestionDismissals.Where(d => d.StoryEventId == loserId).ToList())
        {
            if (survivorDismissed.Add(d.IssueId))
            {
                d.StoryEventId = survivorId;
            }
            else
            {
                context.EventSuggestionDismissals.Remove(d);
            }
        }

        foreach (var list in context.ReadingLists.Where(l => l.StoryEventId == loserId).ToList())
        {
            list.StoryEventId = survivorId;
        }

        // "Not the same" answers about an event that no longer exists mean nothing; the cascade would drop them anyway.
        context.StoryEventDuplicateDismissals.RemoveRange(
            context.StoryEventDuplicateDismissals.Where(d => d.LowerEventId == loserId || d.HigherEventId == loserId));
    }
}
