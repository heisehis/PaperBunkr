using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Read/write resolver for <see cref="EventMembership"/> (docs/superpowers/specs/2026-08-17-
/// metadata-model-phase4b-story-events-design.md) - the single tested home for reorder/duplicate
/// rules, same shape as <see cref="MediaRelationResolver"/>/<see cref="ContinuityResolver"/>.
/// </summary>
// internal - see MediaRelationResolver (Plugin API v3 §7). Plugins read through IMetadataGraph.
internal static class EventMembershipResolver
{
    public static IReadOnlyList<EventMembership> GetOrderedMembers(PaperbunkrDbContext context, int storyEventId) =>
        context.EventMemberships
            .Include(m => m.Issue).ThenInclude(i => i!.Series)
            .Where(m => m.StoryEventId == storyEventId)
            .OrderBy(m => m.Position)
            .ToList();

    public static IReadOnlyList<StoryEvent> GetEventsForSeries(PaperbunkrDbContext context, int seriesId) =>
        context.EventMemberships
            .Include(m => m.StoryEvent)
            .Where(m => m.Issue != null && m.Issue.SeriesId == seriesId)
            .Select(m => m.StoryEvent!)
            .Distinct()
            .ToList();

    /// <summary>Other series that share membership in at least one <see cref="StoryEvent"/> with <paramref name="seriesId"/> - deduplicated if more than one event is shared.</summary>
    public static IReadOnlyList<Series> GetOtherSeriesInSharedEvents(PaperbunkrDbContext context, int seriesId)
    {
        var eventIds = context.EventMemberships
            .Where(m => m.Issue != null && m.Issue.SeriesId == seriesId)
            .Select(m => m.StoryEventId)
            .Distinct()
            .ToList();

        if (eventIds.Count == 0)
        {
            return new List<Series>();
        }

        return context.EventMemberships
            .Include(m => m.Issue).ThenInclude(i => i!.Series)
            .Where(m => eventIds.Contains(m.StoryEventId) && m.Issue != null && m.Issue.SeriesId != seriesId)
            .Select(m => m.Issue!.Series!)
            .Distinct()
            .OrderBy(s => s.Name)
            .ToList();
    }

    /// <summary>Rejects an exact duplicate issue-in-event pair - an issue holds exactly one role in a given event; re-adding it should edit via remove-then-add rather than create a second row.</summary>
    public static bool AddMember(PaperbunkrDbContext context, int storyEventId, int issueId, EventMembershipRole role)
    {
        bool isDuplicate = context.EventMemberships.Any(m => m.StoryEventId == storyEventId && m.IssueId == issueId);
        if (isDuplicate)
        {
            return false;
        }

        int nextPosition = context.EventMemberships.Where(m => m.StoryEventId == storyEventId).Select(m => (int?)m.Position).Max() is int max ? max + 1 : 0;
        context.EventMemberships.Add(new EventMembership { StoryEventId = storyEventId, IssueId = issueId, Position = nextPosition, Role = role });
        context.SaveChanges();
        return true;
    }

    public static void RemoveMember(PaperbunkrDbContext context, int eventMembershipId)
    {
        var member = context.EventMemberships.Find(eventMembershipId);
        if (member is not null)
        {
            context.EventMemberships.Remove(member);
            context.SaveChanges();
        }
    }

    /// <summary>Swaps this member's <see cref="EventMembership.Position"/> with its adjacent neighbor - same swap-adjacent-values approach as <c>ReadingScreenViewModel.Reorder</c>.</summary>
    public static void Reorder(PaperbunkrDbContext context, int eventMembershipId, int offset)
    {
        var member = context.EventMemberships.Find(eventMembershipId);
        if (member is null)
        {
            return;
        }

        var ordered = context.EventMemberships.Where(m => m.StoryEventId == member.StoryEventId).OrderBy(m => m.Position).ToList();
        int index = ordered.FindIndex(m => m.Id == eventMembershipId);
        int swapWith = index + offset;
        if (index < 0 || swapWith < 0 || swapWith >= ordered.Count)
        {
            return;
        }

        (ordered[index].Position, ordered[swapWith].Position) = (ordered[swapWith].Position, ordered[index].Position);
        context.SaveChanges();
    }
}
