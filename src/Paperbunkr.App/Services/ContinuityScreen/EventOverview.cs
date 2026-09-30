using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.ContinuityScreen;

/// <summary>An event linked to this one by a Prequel/Sequel/Continuation relation, from this event's side.</summary>
public sealed record EventNeighbourInput(int EventId, string Name, int? Start, int? End, bool IsRead, bool IsEarlier);

/// <summary>A card in the "Follows ← This event → Followed by" strip.</summary>
public sealed record EventNeighbour(int EventId, string Name, string Years, bool IsRead);

/// <summary>A continuity this event's series belong to (the "in Earth-616" chip).</summary>
public sealed record EventContinuityRef(int ContinuityId, string Name);

/// <summary>Everything <see cref="EventOverviewBuilder"/> needs, loaded by <see cref="EventOverviewBuilder.Load"/>.</summary>
public sealed record EventOverviewInput(
    int EventId,
    string Name,
    int? Start,
    int? End,
    IReadOnlyList<EventMapRow> Members,
    string? ComicVineArcId,
    string? MetronArcId,
    StoryEventOrigin Origin,
    IReadOnlyList<EventContinuityRef> Continuities,
    IReadOnlyList<EventNeighbourInput> Neighbours,
    EventMapEventRef? PossibleDuplicate);

/// <summary>What an event's Overview shows around its reading list.</summary>
public sealed record EventOverview(
    string StatsLine,
    string? SourceLabel,
    IReadOnlyList<EventContinuityRef> Continuities,
    IReadOnlyList<EventNeighbour> Follows,
    IReadOnlyList<EventNeighbour> FollowedBy,
    EventMapEventRef? PossibleDuplicate,
    ContinueTarget? Continue,
    IReadOnlyList<string> CollageKeys);

/// <summary>
/// The event page's Overview (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md): stats with the core/tie-in split,
/// where the event came from, its continuities, the events before and after it, the Continue target and the hero collage. The attention
/// items are built from the page's live lists (<see cref="Attention"/>) so they follow each edit.
/// </summary>
public static class EventOverviewBuilder
{
    public static EventOverview Build(EventOverviewInput input)
    {
        var issues = input.Members.GroupBy(m => m.IssueId).Select(g => g.First()).ToList();
        int core = issues.Count(m => m.Role == EventMembershipRole.Core);
        int tieIns = issues.Count(m => m.Role == EventMembershipRole.TieIn);
        int read = issues.Count(m => m.ReadState == EventMapReadState.Read);

        var parts = new List<string>();
        string years = ContinuityOverviewBuilder.YearsLabel(input.Start / 100, input.End / 100);
        if (years.Length > 0)
        {
            parts.Add(years);
        }

        string issueText = ContinuityOverviewBuilder.Plural(issues.Count, "issue", "issues");
        if (core > 0 || tieIns > 0)
        {
            issueText += $" ({core} core · {ContinuityOverviewBuilder.Plural(tieIns, "tie-in", "tie-ins")})";
        }

        parts.Add(issueText);
        parts.Add(ContinuityOverviewBuilder.Plural(issues.Select(m => m.SeriesId).Distinct().Count(), "series", "series"));
        if (issues.Count > 0)
        {
            parts.Add($"{ContinuityOverviewBuilder.Percent(read, issues.Count)}% read");
        }

        var neighbours = input.Neighbours
            .GroupBy(n => (n.EventId, n.IsEarlier)).Select(g => g.First())
            .OrderBy(n => n.Start ?? int.MaxValue).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        EventNeighbour Card(EventNeighbourInput n) => new(n.EventId, n.Name, ContinuityOverviewBuilder.YearsLabel(n.Start / 100, n.End / 100), n.IsRead);

        var next = input.Members.FirstOrDefault(m => m.ReadState != EventMapReadState.Read && !m.FileIsMissing);
        var collage = input.Members.Where(m => m.Role == EventMembershipRole.Core && m.CoverKey is not null)
            .Concat(input.Members.Where(m => m.Role != EventMembershipRole.Core && m.CoverKey is not null))
            .Select(m => m.CoverKey!)
            .Distinct()
            .Take(ContinuityOverviewBuilder.CollageSize)
            .ToList();

        return new EventOverview(
            string.Join(" · ", parts),
            SourceLabel(input.ComicVineArcId, input.MetronArcId, input.Origin),
            input.Continuities,
            neighbours.Where(n => n.IsEarlier).Select(Card).ToList(),
            neighbours.Where(n => !n.IsEarlier).Select(Card).ToList(),
            input.PossibleDuplicate,
            next is null ? null : new ContinueTarget(next.IssueId, ContinuityOverviewBuilder.IssueLabel(next.SeriesName, next.Number), input.EventId),
            collage);
    }

    /// <summary>Where the event came from, by its stored arc ids; "Yours" for an event made by hand with none.</summary>
    public static string? SourceLabel(string? comicVineArcId, string? metronArcId, StoryEventOrigin origin) =>
        (string.IsNullOrWhiteSpace(comicVineArcId), string.IsNullOrWhiteSpace(metronArcId)) switch
        {
            (false, false) => "ComicVine + Metron",
            (false, true) => "ComicVine",
            (true, false) => "Metron",
            _ => origin == StoryEventOrigin.User ? "Yours" : null,
        };

    /// <summary>The event page's "Needs attention" items, from its live lists.</summary>
    public static IReadOnlyList<AttentionItem> Attention(int roleSuggestions, int issueSuggestions, int connections, string? duplicateName)
    {
        var items = new List<AttentionItem>();
        if (roleSuggestions > 0)
        {
            items.Add(new AttentionItem(AttentionKind.RoleSuggestions,
                ContinuityOverviewBuilder.Plural(roleSuggestions, "role suggestion to review", "role suggestions to review"), roleSuggestions));
        }

        if (issueSuggestions > 0)
        {
            items.Add(new AttentionItem(AttentionKind.IssueSuggestions,
                ContinuityOverviewBuilder.Plural(issueSuggestions, "issue suggestion", "issue suggestions"), issueSuggestions));
        }

        if (connections > 0)
        {
            items.Add(new AttentionItem(AttentionKind.Connections,
                ContinuityOverviewBuilder.Plural(connections, "connection to review", "connections to review"), connections));
        }

        if (duplicateName is not null)
        {
            items.Add(new AttentionItem(AttentionKind.Duplicate, $"Possible duplicate: {duplicateName}", 1));
        }

        return items;
    }

    /// <summary>The database side, meant to run off the UI thread with a context of its own. Null when the event no longer exists.</summary>
    public static EventOverview? Load(PaperbunkrDbContext context, int storyEventId, GcdDataStore? gcd)
    {
        var storyEvent = context.StoryEvents.AsNoTracking().FirstOrDefault(e => e.Id == storyEventId);
        var source = EventMapLoader.Load(context, storyEventId);
        if (storyEvent is null || source is null)
        {
            return null;
        }

        var relations = context.EventRelations.AsNoTracking()
            .Where(r => (r.SourceEventId == storyEventId || r.TargetEventId == storyEventId)
                        && (r.RelationType == RelationType.Prequel || r.RelationType == RelationType.Sequel || r.RelationType == RelationType.Continuation))
            .Select(r => new { r.SourceEventId, r.TargetEventId, r.RelationType })
            .ToList();

        var others = relations.Select(r => r.SourceEventId == storyEventId ? r.TargetEventId : r.SourceEventId).Distinct().ToList();
        var spans = EventChronology.LoadSpans(context, others.Append(storyEventId).ToList(), gcd);
        var readOthers = others.ToDictionary(id => id, id => IsAllRead(context, id));

        var neighbours = new List<EventNeighbourInput>();
        foreach (var r in relations)
        {
            if (EventChronology.Direction(r.SourceEventId, r.TargetEventId, r.RelationType) is not { } d)
            {
                continue;
            }

            int other = d.Earlier == storyEventId ? d.Later : d.Earlier;
            if (other == storyEventId || !spans.TryGetValue(other, out var span))
            {
                continue;
            }

            neighbours.Add(new EventNeighbourInput(other, span.Name, span.Start, span.End, readOthers.GetValueOrDefault(other), IsEarlier: d.Later == storyEventId));
        }

        var seriesIds = source.Rows.Select(r => r.SeriesId).Distinct().ToList();
        var continuities = context.ContinuityMemberships.AsNoTracking()
            .Where(m => seriesIds.Contains(m.SeriesId))
            .Select(m => new { m.ContinuityId, m.Continuity.Name })
            .Distinct()
            .AsEnumerable()
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new EventContinuityRef(c.ContinuityId, c.Name))
            .ToList();

        EventMapEventRef? duplicate = null;
        foreach (var item in StoryEventIdentityResolver.FindReviewItems(context))
        {
            if (item.EventBId is int b && (item.EventAId == storyEventId || b == storyEventId))
            {
                duplicate = item.EventAId == storyEventId ? new EventMapEventRef(b, item.EventBName ?? "Event") : new EventMapEventRef(item.EventAId, item.EventAName);
                break;
            }
        }

        var own = spans.GetValueOrDefault(storyEventId);
        return Build(new EventOverviewInput(storyEventId, storyEvent.Name, own?.Start, own?.End, source.Rows,
            storyEvent.ComicVineArcId, storyEvent.MetronArcId, storyEvent.Origin, continuities, neighbours, duplicate));
    }

    private static bool IsAllRead(PaperbunkrDbContext context, int storyEventId)
    {
        var pages = context.EventMemberships.AsNoTracking()
            .Where(m => m.StoryEventId == storyEventId && m.Issue != null)
            .Select(m => new { m.Issue!.LastPageRead, m.Issue.PageCount })
            .ToList();
        return pages.Count > 0 && pages.All(p => new Issue { LastPageRead = p.LastPageRead, PageCount = p.PageCount }.HasBeenRead());
    }
}
