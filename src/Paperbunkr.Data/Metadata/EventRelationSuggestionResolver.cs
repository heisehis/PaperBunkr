using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One "these two events look connected" candidate (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-event-relations-design.md's deferred "extending suggestion logic to relations between events").</summary>
public sealed record EventRelationSuggestion(StoryEvent Candidate, string Reason);

/// <summary>
/// Surfaces events that look like they should be connected to a given event, from three signals:
/// a shared significant word in the name, overlapping/adjacent date ranges, and a shared member
/// series. Propose-don't-assert, same as every other suggestion surface in this codebase - creates
/// no <see cref="EventRelation"/>.
/// </summary>
public static class EventRelationSuggestionResolver
{
    public static IReadOnlyList<EventRelationSuggestion> GetSuggestions(PaperbunkrDbContext context, int storyEventId)
    {
        var self = context.StoryEvents.FirstOrDefault(e => e.Id == storyEventId);
        if (self is null)
        {
            return Array.Empty<EventRelationSuggestion>();
        }

        var alreadyConnected = EventRelationResolver.GetRelatedEvents(context, storyEventId).Select(r => r.OtherEvent.Id).ToHashSet();
        alreadyConnected.Add(storyEventId);

        var selfWords = SignificantWords(self.Name);
        var selfSeriesIds = MemberSeriesIds(context, storyEventId);

        var suggestions = new List<EventRelationSuggestion>();
        foreach (var other in context.StoryEvents.Where(e => !alreadyConnected.Contains(e.Id)).ToList())
        {
            var reasons = new List<string>();

            var otherWords = SignificantWords(other.Name);
            var sharedWord = selfWords.FirstOrDefault(otherWords.Contains);
            if (sharedWord is not null)
            {
                reasons.Add($"both names contain \"{sharedWord}\"");
            }

            if (DateRangesOverlapOrAdjacent(self, other))
            {
                reasons.Add("overlapping or adjacent publication dates");
            }

            var otherSeriesIds = MemberSeriesIds(context, other.Id);
            int sharedSeries = selfSeriesIds.Count(otherSeriesIds.Contains);
            if (sharedSeries > 0)
            {
                reasons.Add(sharedSeries == 1 ? "a shared member series" : $"{sharedSeries} shared member series");
            }

            if (reasons.Count > 0)
            {
                suggestions.Add(new EventRelationSuggestion(other, string.Join(" · ", reasons)));
            }
        }

        return suggestions.OrderByDescending(s => s.Reason.Count(c => c == '·')).ThenBy(s => s.Candidate.Name).ToList();
    }

    /// <summary>Words longer than 3 chars, original casing preserved, matched case-insensitively.</summary>
    private static HashSet<string> SignificantWords(string name) =>
        name.Split(new[] { ' ', '-', ':', '(', ')', '/', ',', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static List<int> MemberSeriesIds(PaperbunkrDbContext context, int storyEventId) =>
        context.EventMemberships
            .Where(m => m.StoryEventId == storyEventId && m.Issue != null)
            .Select(m => m.Issue!.SeriesId)
            .Distinct()
            .ToList();

    private static bool DateRangesOverlapOrAdjacent(StoryEvent a, StoryEvent b)
    {
        // Needs at least one bounded range on each side to say anything.
        if ((a.StartDate ?? a.EndDate) is not DateTime aAnchor || (b.StartDate ?? b.EndDate) is not DateTime bAnchor)
        {
            return false;
        }

        DateTime aStart = a.StartDate ?? aAnchor;
        DateTime aEnd = a.EndDate ?? aAnchor;
        DateTime bStart = b.StartDate ?? bAnchor;
        DateTime bEnd = b.EndDate ?? bAnchor;

        // Overlap, or within ~2 years of each other.
        var gap = TimeSpan.FromDays(365 * 2);
        return aStart <= bEnd.Add(gap) && bStart <= aEnd.Add(gap);
    }
}
