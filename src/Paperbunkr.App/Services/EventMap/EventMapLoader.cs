using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>An event listed in a continuity's "Events in this continuity" section.</summary>
public sealed record ContinuityEventEntry(int StoryEventId, string Name, int MemberCount);

/// <summary>
/// The Event Map's database side (docs/superpowers/specs/2026-09-25-event-map-design.md §1 "Loading", §4). Every method
/// takes the caller's context so the view model can run <see cref="Load"/> off the UI thread with a context of its own.
/// </summary>
public static class EventMapLoader
{
    /// <summary>The event and its memberships in <c>(Position, Id)</c> order, or null when the event no longer exists.</summary>
    public static EventMapSource? Load(PaperbunkrDbContext context, int storyEventId)
    {
        var storyEvent = context.StoryEvents.AsNoTracking().FirstOrDefault(e => e.Id == storyEventId);
        if (storyEvent is null)
        {
            return null;
        }

        var memberships = context.EventMemberships.AsNoTracking()
            .Where(m => m.StoryEventId == storyEventId)
            .Include(m => m.Issue).ThenInclude(i => i!.Series)
            .Include(m => m.Issue).ThenInclude(i => i!.MetadataProposals)
            .OrderBy(m => m.Position).ThenBy(m => m.Id)
            .ToList();

        var rows = memberships
            .Where(m => m.Issue is not null)
            .Select(m => ToRow(m, m.Issue!))
            .ToList();

        return new EventMapSource(storyEvent.Id, storyEvent.Name, storyEvent.SpineSeriesId, rows);
    }

    /// <summary>Unread at 0 %, Read at the CE read threshold (<see cref="IssueMetadataExtensions.HasBeenRead"/>), otherwise in progress.</summary>
    public static EventMapReadState ReadStateOf(Issue issue)
    {
        if (issue.ReadPercentage() <= 0)
        {
            return EventMapReadState.Unread;
        }

        return issue.HasBeenRead() ? EventMapReadState.Read : EventMapReadState.InProgress;
    }

    private static EventMapRow ToRow(EventMembership membership, Issue issue) => new(
        membership.Id,
        issue.Id,
        membership.Position,
        membership.Role,
        issue.SeriesId,
        issue.Series?.Name ?? "Unknown series",
        issue.EffectiveNumber() ?? string.Empty,
        issue.EffectiveYear(),
        issue.Month,
        issue.FileIsMissing,
        ReadStateOf(issue),
        CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize),
        issue.Summary);

    /// <summary>Writes the spine picker's choice: null = automatic, <see cref="SpineResolver.ForceRelay"/> = never, otherwise a series id.</summary>
    public static void SaveSpine(PaperbunkrDbContext context, int storyEventId, int? spineSeriesId)
    {
        var storyEvent = context.StoryEvents.Find(storyEventId);
        if (storyEvent is null)
        {
            return;
        }

        storyEvent.SpineSeriesId = spineSeriesId;
        storyEvent.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();
    }

    /// <summary>Marks an issue read or unread through <see cref="IssueReadStateResolver"/> and returns its new read state (null if the issue is gone).</summary>
    public static EventMapReadState? SetRead(PaperbunkrDbContext context, int issueId, bool read)
    {
        var issue = context.Issues.Find(issueId);
        if (issue is null)
        {
            return null;
        }

        if (read)
        {
            IssueReadStateResolver.MarkAsRead(issue);
        }
        else
        {
            IssueReadStateResolver.MarkAsUnread(issue);
        }

        context.SaveChanges();
        return ReadStateOf(issue);
    }

    /// <summary>Distinct events with at least one member issue whose series belongs to the continuity, by name.</summary>
    public static IReadOnlyList<ContinuityEventEntry> EventsInContinuity(PaperbunkrDbContext context, int continuityId)
    {
        var seriesIds = context.ContinuityMemberships
            .Where(m => m.ContinuityId == continuityId)
            .Select(m => m.SeriesId);

        return context.StoryEvents.AsNoTracking()
            .Where(e => e.Members.Any(m => seriesIds.Contains(m.Issue!.SeriesId)))
            .Select(e => new { e.Id, e.Name, Count = e.Members.Count })
            .AsEnumerable()
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(e => new ContinuityEventEntry(e.Id, e.Name, e.Count))
            .ToList();
    }
}
