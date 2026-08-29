using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One reviewable "this issue looks like it belongs to this event" suggestion (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md).</summary>
public sealed record EventSuggestion(Issue Issue, FormatSignalStrength Strength, EventMembershipRole? SuggestedRole, string Reason);

/// <summary>
/// Surfaces a reviewable queue of issues that look like they belong to a given <see cref="StoryEvent"/>
/// (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md) -
/// "propose, don't assert", same posture as <c>MetadataProposal</c> and Phase 3's manual-only
/// relation creation. Creates nothing; the Story Events screen turns a suggestion into a real
/// <see cref="EventMembership"/> only on the user's explicit Add.
/// </summary>
// internal - see MediaRelationResolver (Plugin API v3 §7). Plugins read through IMetadataGraph.
internal static class EventSuggestionResolver
{
    public static IReadOnlyList<EventSuggestion> GetSuggestions(PaperbunkrDbContext context, int storyEventId)
    {
        var storyEvent = context.StoryEvents.FirstOrDefault(e => e.Id == storyEventId);
        if (storyEvent is null)
        {
            return Array.Empty<EventSuggestion>();
        }

        var memberIssueIds = context.EventMemberships
            .Where(m => m.StoryEventId == storyEventId)
            .Select(m => m.IssueId)
            .ToHashSet();

        // Persisted dismissals (docs/superpowers/specs/2026-08-27-metadata-model-phase4e-format-
        // signal-suggestions-design.md - the "don't suggest this again" state that phase deferred).
        var dismissedIssueIds = context.EventSuggestionDismissals
            .Where(d => d.StoryEventId == storyEventId)
            .Select(d => d.IssueId)
            .ToHashSet();

        // Format is a signal, not a filter on its own - requiring at least one of (year in range)
        // or (event name in the issue's Series Group / Story Arc text) keeps this from surfacing
        // every Annual in the whole library for every event.
        bool hasRange = storyEvent.StartDate.HasValue || storyEvent.EndDate.HasValue;
        bool nameSet = !string.IsNullOrWhiteSpace(storyEvent.Name);

        var candidates = context.Issues
            .Include(i => i.Series)
            .Where(i => i.Format != null && i.Format != "")
            .AsEnumerable()
            .Where(i => !memberIssueIds.Contains(i.Id) && !dismissedIssueIds.Contains(i.Id));

        var result = new List<EventSuggestion>();
        foreach (var issue in candidates)
        {
            var signal = FormatSignalCatalog.Resolve(issue.Format);
            if (signal.Strength == FormatSignalStrength.None)
            {
                continue;
            }

            bool yearInRange = hasRange && issue.Year is int year
                && (storyEvent.StartDate is not DateTime start || year >= start.Year)
                && (storyEvent.EndDate is not DateTime end || year <= end.Year);

            bool seriesGroupMatch = nameSet && (issue.SeriesGroup ?? string.Empty).Contains(storyEvent.Name, StringComparison.OrdinalIgnoreCase);
            bool storyArcMatch = nameSet && (issue.StoryArc ?? string.Empty).Contains(storyEvent.Name, StringComparison.OrdinalIgnoreCase);

            if (!yearInRange && !seriesGroupMatch && !storyArcMatch)
            {
                continue;
            }

            var reasons = new List<string> { $"Format: {issue.Format}" };
            if (yearInRange)
            {
                reasons.Add($"published {issue.Year}, within event range");
            }

            if (seriesGroupMatch)
            {
                reasons.Add("Series Group matches event name");
            }

            if (storyArcMatch)
            {
                reasons.Add("Story Arc matches event name");
            }

            result.Add(new EventSuggestion(issue, signal.Strength, signal.SuggestedRole, string.Join(" · ", reasons)));
        }

        return result
            .OrderByDescending(s => s.Strength)
            .ThenBy(s => s.Issue.Year ?? int.MaxValue)
            .ThenBy(s => s.Issue.Series?.Name)
            .ToList();
    }

    /// <summary>Persists "never suggest this issue for this event again" - idempotent.</summary>
    public static void Dismiss(PaperbunkrDbContext context, int storyEventId, int issueId)
    {
        bool already = context.EventSuggestionDismissals.Any(d => d.StoryEventId == storyEventId && d.IssueId == issueId);
        if (already)
        {
            return;
        }

        context.EventSuggestionDismissals.Add(new EventSuggestionDismissal { StoryEventId = storyEventId, IssueId = issueId });
        context.SaveChanges();
    }

    /// <summary>Clears a persisted dismissal so the issue can be suggested again - a no-op if it isn't dismissed.</summary>
    public static void Restore(PaperbunkrDbContext context, int storyEventId, int issueId)
    {
        var row = context.EventSuggestionDismissals.FirstOrDefault(d => d.StoryEventId == storyEventId && d.IssueId == issueId);
        if (row is not null)
        {
            context.EventSuggestionDismissals.Remove(row);
            context.SaveChanges();
        }
    }

    /// <summary>The issues currently dismissed for this event, for a "show dismissed" affordance.</summary>
    public static IReadOnlyList<Issue> GetDismissed(PaperbunkrDbContext context, int storyEventId) =>
        context.EventSuggestionDismissals
            .Include(d => d.Issue).ThenInclude(i => i!.Series)
            .Where(d => d.StoryEventId == storyEventId && d.Issue != null)
            .OrderByDescending(d => d.DismissedAt)
            .Select(d => d.Issue!)
            .ToList();
}
