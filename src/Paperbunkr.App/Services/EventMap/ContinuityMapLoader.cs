using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>One story event touching the continuity: its date span and its members as map rows, in the event's own order.</summary>
public sealed record ContinuityEventData(int Id, string Name, int? Start, int? End, IReadOnlyList<EventMapRow> Members, int? SortKey = null, int? Rank = null,
    string? Description = null, string? CoverImageUrl = null, StoryEventCoverSource CoverSource = StoryEventCoverSource.None,
    IReadOnlyList<ContinuityEventTagData>? Tags = null, string? CbroListKey = null, string? CbroSourceName = null, int? CbroSourceYear = null,
    IReadOnlyList<CbroOverlapNoteData>? CbroOverlapNotes = null)
{
    /// <summary>The event as <see cref="EventChronology"/> orders it: its span plus where an outside reading order placed it.</summary>
    public EventSpan Span => new(Id, Name, Start, End, SortKey, Rank);
}

public sealed record ContinuityEventTagData(string Value, string Category, bool IsIssueDerived)
{
    public string DisplayValue => IsIssueDerived ? $"{Value} · Comic" : Value;
}
public sealed record ContinuityRelatedEventData(int EventId, string Name, string Relation);

/// <summary>A stored relation between two of the continuity's events, with who asserted it and why (for the connector tooltip).</summary>
public sealed record ContinuityRelationData(int SourceId, int TargetId, RelationType Type, bool IsYours, string Provenance);

/// <summary>Everything the continuity map needs from the database, before filtering (see <see cref="ContinuityMapBuilder"/>).</summary>
public sealed record ContinuityMapData(
    int ContinuityId,
    string Name,
    IReadOnlyList<ContinuityLane> MemberLanes,
    IReadOnlyList<ContinuityEventData> Events,
    IReadOnlyList<ContinuityRelationData> Relations,
    IReadOnlyList<EventMapRow> LooseIssues,
    IReadOnlyDictionary<int, IReadOnlyList<EventMapEventRef>> EventsByIssue,
    int PendingDuplicatePairs);

/// <summary>
/// The continuity map's database side (docs/superpowers/specs/2026-09-27-continuity-map-design.md §3): the continuity's member series
/// (lanes, in its own order), every event touching it with its members, the relations among those events, and the member-series issues
/// that are in no event. <see cref="ContinuityMapBuilder"/> turns this into rows and blocks, so changing a filter needs no new query.
/// </summary>
public static class ContinuityMapLoader
{
    private static IEnumerable<ContinuityEventTagData> EventTags(PaperbunkrDbContext context, int eventId, IEnumerable<Issue> members)
    {
        var curated = context.StoryEventTags.AsNoTracking().Where(t => t.StoryEventId == eventId)
            .Select(t => new ContinuityEventTagData(t.Value, t.Category, false)).ToList();
        var issueIds = members.Select(i => i.Id).Distinct().ToList();
        var derived = context.IssueTags.AsNoTracking().Where(t => issueIds.Contains(t.IssueId))
            .Select(t => new { t.Value, t.Category, t.Field }).ToList()
            .Select(t => new ContinuityEventTagData(t.Value, t.Category ?? (t.Field == IssueTagField.Genre ? "Genre" : "Uncategorized"), true));
        return curated.Concat(derived).GroupBy(t => (t.Value.Trim().ToUpperInvariant(), t.Category.Trim().ToUpperInvariant()))
            .Select(g => g.OrderBy(t => t.IsIssueDerived).First());
    }

    /// <param name="gcd">The installed Grand Comics Database extract, when there is one: matched issues are dated by GCD's on-sale dates
    /// (docs/superpowers/specs/2026-09-27-gcd-data-design.md §4).</param>
    public static ContinuityMapData? LoadData(PaperbunkrDbContext context, int continuityId, GcdDataStore? gcd = null)
    {
        var continuity = context.Continuities.AsNoTracking().FirstOrDefault(c => c.Id == continuityId);
        if (continuity is null)
        {
            return null;
        }

        var lanes = context.ContinuityMemberships.AsNoTracking()
            .Where(m => m.ContinuityId == continuityId)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Id)
            .Select(m => new { m.SeriesId, m.Series.Name })
            .AsEnumerable()
            .Select(m => new ContinuityLane(m.SeriesId, m.Name, Outside: false))
            .ToList();
        var memberSeries = lanes.Select(l => l.SeriesId).ToHashSet();
        var continuations = context.MediaRelations.AsNoTracking()
            .Where(r => r.RelationType == RelationType.Continuation && r.SourceSeriesId != null && r.TargetSeriesId != null
                        && memberSeries.Contains(r.SourceSeriesId.Value) && memberSeries.Contains(r.TargetSeriesId.Value))
            .Select(r => new { Newer = r.SourceSeriesId!.Value, Older = r.TargetSeriesId!.Value })
            .AsEnumerable()
            .Select(r => (r.Newer, r.Older))
            .ToList();
        lanes = OrderByContinuation(lanes, continuations);

        var eventIds = EventMapLoader.EventsInContinuity(context, continuityId).Select(e => e.StoryEventId).ToList();
        var spans = EventChronology.LoadSpans(context, eventIds, gcd);

        var memberships = context.EventMemberships.AsNoTracking()
            .Where(m => eventIds.Contains(m.StoryEventId))
            .Include(m => m.Issue).ThenInclude(i => i!.Series)
            .Include(m => m.Issue).ThenInclude(i => i!.MetadataProposals)
            .Include(m => m.Issue).ThenInclude(i => i!.Tags)
            .OrderBy(m => m.Position).ThenBy(m => m.Id)
            .ToList();

        var inAnyEvent = memberships.Select(m => m.IssueId).ToHashSet();
        var looseIssues = context.Issues.AsNoTracking()
            .Where(i => memberSeries.Contains(i.SeriesId) && !i.IsPlaceholder && !inAnyEvent.Contains(i.Id))
            .Include(i => i.Series)
            .Include(i => i.MetadataProposals)
            .ToList();
        var gcdDates = gcd?.DateKeysFor(memberships.Select(m => m.Issue?.GcdIssueId).Concat(looseIssues.Select(i => i.GcdIssueId)).OfType<int>());

        var eventNames = spans.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
        var cbroEvidence = context.EventOrderEvidence.AsNoTracking()
            .Where(e => eventIds.Contains(e.StoryEventId) && e.Provider == EventOrderProvider.Cbro)
            .Include(e => e.CbroOverlapNotes)
            .ToDictionary(e => e.StoryEventId);
        var eventsByIssue = memberships
            .GroupBy(m => m.IssueId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EventMapEventRef>)g.Select(m => m.StoryEventId).Distinct()
                .Select(id => new EventMapEventRef(id, eventNames.GetValueOrDefault(id, "Event"))).ToList());

        var events = eventIds
            .Where(spans.ContainsKey)
            .Select(id => new ContinuityEventData(
                id, spans[id].Name, spans[id].Start, spans[id].End,
                memberships.Where(m => m.StoryEventId == id && m.Issue is not null)
                    .Select(m => ToRow(m.Issue!, m.Role, memberSeries, membershipId: m.IssueId, position: m.Position, gcdDates))
                    .ToList(),
                spans[id].SortKey, spans[id].Rank,
                context.StoryEvents.AsNoTracking().Where(e => e.Id == id).Select(e => e.Description).FirstOrDefault(),
                context.StoryEvents.AsNoTracking().Where(e => e.Id == id).Select(e => e.CoverImageUrl).FirstOrDefault(),
                context.StoryEvents.AsNoTracking().Where(e => e.Id == id).Select(e => e.CoverSource).FirstOrDefault(),
                EventTags(context, id, memberships.Where(m => m.StoryEventId == id).Select(m => m.Issue).OfType<Issue>()).ToList(),
                cbroEvidence.GetValueOrDefault(id)?.ListKey,
                cbroEvidence.GetValueOrDefault(id)?.SourceName,
                cbroEvidence.GetValueOrDefault(id)?.SourceYear,
                cbroEvidence.GetValueOrDefault(id)?.CbroOverlapNotes.Select(n => new CbroOverlapNoteData(n.OriginalText, n.TargetName, n.TargetYear)).ToList()))
            .ToList();

        var relations = context.EventRelations.AsNoTracking()
            .Include(r => r.Evidence)
            .Where(r => eventIds.Contains(r.SourceEventId) && eventIds.Contains(r.TargetEventId))
            .ToList()
            .Select(r =>
            {
                bool automatic = EventConnectorSweep.IsAutomatic(r);
                var evidence = r.Evidence.FirstOrDefault();
                string provenance = !automatic ? "yours"
                    : evidence?.Provider == RelationEvidenceProvider.Wikidata ? $"Wikidata {evidence.ProviderSourceId}"
                    : evidence?.Provider == RelationEvidenceProvider.Metron ? $"Metron · {evidence.ProviderRelationType}"
                    : $"inferred · {evidence?.ProviderRelationType}";
                return new ContinuityRelationData(r.SourceEventId, r.TargetEventId, r.RelationType, !automatic, provenance);
            })
            .ToList();

        var loose = looseIssues
            .Select(i => ToRow(i, EventMembershipRole.Core, memberSeries, membershipId: i.Id, position: 0, gcdDates))
            .ToList();

        int pending = 0;
        var eventSet = eventIds.ToHashSet();
        foreach (var item in StoryEventIdentityResolver.FindReviewItems(context))
        {
            if (item.EventBId is int b && eventSet.Contains(item.EventAId) && eventSet.Contains(b))
            {
                pending++;
            }
        }

        return new ContinuityMapData(continuity.Id, continuity.Name, lanes, events, relations, loose, eventsByIssue, pending);
    }

    /// <summary>
    /// Member lanes in the continuity's order, except that a series continued by another (a Continuation relation - GCD bonds become these)
    /// is followed directly by its continuation, older first, and says so in <see cref="ContinuityLane.ContinuesAs"/>.
    /// </summary>
    internal static List<ContinuityLane> OrderByContinuation(IReadOnlyList<ContinuityLane> lanes, IReadOnlyCollection<(int Newer, int Older)> links)
    {
        if (links.Count == 0)
        {
            return lanes.ToList();
        }

        var rank = lanes.Select((l, i) => (l.SeriesId, i)).ToDictionary(x => x.SeriesId, x => x.i);
        var byId = lanes.ToDictionary(l => l.SeriesId);
        var newerOf = links.Where(l => l.Newer != l.Older).GroupBy(l => l.Older)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Newer).Distinct().OrderBy(id => rank[id]).ToList());
        var olderOf = links.Where(l => l.Newer != l.Older).GroupBy(l => l.Newer)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Older).OrderBy(id => rank[id]).First());

        var result = new List<ContinuityLane>();
        var placed = new HashSet<int>();
        void Place(int id)
        {
            if (!placed.Add(id))
            {
                return;
            }

            result.Add(byId[id]);
            foreach (int newer in newerOf.GetValueOrDefault(id) ?? new List<int>())
            {
                Place(newer);
            }
        }

        foreach (var lane in lanes)
        {
            int root = lane.SeriesId;
            var seen = new HashSet<int> { root };
            while (olderOf.TryGetValue(root, out int older) && !placed.Contains(older) && seen.Add(older))
            {
                root = older;
            }

            Place(root);
            Place(lane.SeriesId);
        }

        for (int i = 0; i + 1 < result.Count; i++)
        {
            var next = result[i + 1];
            if (newerOf.TryGetValue(result[i].SeriesId, out var newers) && newers.Contains(next.SeriesId))
            {
                result[i] = result[i] with { ContinuesAs = next.Name };
            }
        }

        return result;
    }

    private static EventMapRow ToRow(Issue issue, EventMembershipRole role, IReadOnlySet<int> memberSeries, int membershipId, int position,
        IReadOnlyDictionary<int, int>? gcdDates) => new(
        membershipId,
        issue.Id,
        position,
        role,
        issue.SeriesId,
        issue.Series?.Name ?? "Unknown series",
        issue.EffectiveNumber() ?? string.Empty,
        issue.EffectiveYear(),
        issue.Month,
        issue.FileIsMissing,
        EventMapLoader.ReadStateOf(issue),
        CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize),
        issue.Summary,
        OutsideContinuity: !memberSeries.Contains(issue.SeriesId),
        SortDate: EventChronology.IssueDateKey(issue.EffectiveYear(), issue.Month, issue.GcdIssueId, gcdDates));
}

/// <summary>
/// Turns <see cref="ContinuityMapData"/> into the map's ordered rows and blocks (docs/superpowers/specs/2026-09-27-continuity-map-design.md
/// §3). Pure - every filter change rebuilds from the same data. Events mode: events in <see cref="EventChronology"/> order; before each,
/// a between-events block of loose issues dated before it starts; the event's own members (an issue already placed is skipped and noted
/// "Also in"); a final between-events block for the rest. Publication mode (no events): every issue by date in year blocks.
/// </summary>
public static class ContinuityMapBuilder
{
    public static ContinuityMapSource Build(ContinuityMapData data, ContinuityMapOptions options)
    {
        var seriesOrder = data.MemberLanes.Select((l, i) => (l.SeriesId, i)).ToDictionary(x => x.SeriesId, x => x.i);
        var rows = new List<EventMapRow>();
        var blocks = new List<EventMapBlock>();
        var placed = new HashSet<int>();
        var eventBlock = new Dictionary<int, int>();
        var allEventsById = data.Events.ToDictionary(e => e.Id);

        void AddBlock(EventMapBlockKind kind, string label, int? eventId, IReadOnlyList<EventMapRow> blockRows)
        {
            if (blockRows.Count == 0)
            {
                return;
            }

            int index = blocks.Count;
            int first = rows.Count;
            foreach (var row in blockRows)
            {
                var others = data.EventsByIssue.GetValueOrDefault(row.IssueId)?.Where(e => e.Id != eventId).ToList();
                rows.Add(row with
                {
                    OwnerEventId = eventId,
                    OwnerEventName = eventId is int ownerId ? allEventsById.GetValueOrDefault(ownerId)?.Name ?? label : null,
                    OwnerEventCbroNotes = eventId is int noteOwner && allEventsById.GetValueOrDefault(noteOwner)?.CbroOverlapNotes is { } notes
                        ? notes.Select(n => n.OriginalText).ToList()
                        : null,
                    AlsoIn = others is { Count: > 0 } ? others : null,
                    BlockIndex = index,
                });
                placed.Add(row.IssueId);
            }

            blocks.Add(new EventMapBlock(index, kind, label, eventId, first, rows.Count - 1));
            if (eventId is int id)
            {
                eventBlock[id] = index;
            }
        }

        var directionalEventIds = data.Relations
            .Where(r => EventChronology.Direction(r.SourceId, r.TargetId, r.Type) is not null)
            .SelectMany(r => new[] { r.SourceId, r.TargetId })
            .ToHashSet();
        var unplaced = data.Events.Where(e => e.Start is null && e.End is null && e.SortKey is null && e.Rank is null
                && !directionalEventIds.Contains(e.Id) && !options.HiddenEventIds.Contains(e.Id))
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Id)
            .ToList();
        var unplacedIds = unplaced.Select(e => e.Id).ToHashSet();
        var visible = data.Events.Where(e => !options.HiddenEventIds.Contains(e.Id) && !unplacedIds.Contains(e.Id)).ToList();
        bool publicationOrder = data.Events.Count == 0;
        int looseShown = 0;

        if (publicationOrder)
        {
            foreach (var group in SortLoose(data.LooseIssues, seriesOrder).GroupBy(r => SortKey(r) / 100))
            {
                AddBlock(EventMapBlockKind.Year, group.Key?.ToString(CultureInfo.InvariantCulture) ?? "Undated", null, group.ToList());
            }

            looseShown = rows.Count;
        }
        else
        {
            var order = EventChronology.Order(
                visible.Select(e => e.Span).ToList(),
                data.Relations.Select(r => (r.SourceId, r.TargetId, r.Type)));
            var byId = visible.ToDictionary(e => e.Id);
            var loose = SortLoose(data.LooseIssues, seriesOrder);
            int next = 0;

            foreach (int id in order)
            {
                var e = byId[id];
                var before = new List<EventMapRow>();
                while (next < loose.Count && SortKey(loose[next]) is int date && e.Start is int start && date < start)
                {
                    before.Add(loose[next++]);
                }

                if (!options.EventsOnly)
                {
                    AddBlock(EventMapBlockKind.Between, BetweenLabel(before), null, before);
                    looseShown += before.Count;
                }

                var members = e.Members
                    .Where(m => !placed.Contains(m.IssueId))
                    .Where(m => !options.HideOptional || m.Role != EventMembershipRole.Optional)
                    .ToList();
                AddBlock(EventMapBlockKind.Event, e.Name, e.Id, members);
            }

            if (!options.EventsOnly)
            {
                var rest = loose.Skip(next).ToList();
                AddBlock(EventMapBlockKind.Between, BetweenLabel(rest), null, rest);
                looseShown += rest.Count;
            }

            foreach (var e in unplaced)
            {
                var members = e.Members
                    .Where(m => !placed.Contains(m.IssueId))
                    .Where(m => !options.HideOptional || m.Role != EventMembershipRole.Optional)
                    .ToList();
                AddBlock(EventMapBlockKind.Unplaced, $"Unplaced · {e.Name}", e.Id, members);
            }
        }

        // Lanes with rows: member series in the continuity's order, then outside series by first appearance.
        var withRows = rows.Select(r => r.SeriesId).ToHashSet();
        var lanes = data.MemberLanes.Where(l => withRows.Contains(l.SeriesId)).ToList();
        foreach (var row in rows.Where(r => r.OutsideContinuity))
        {
            if (lanes.All(l => l.SeriesId != row.SeriesId))
            {
                lanes.Add(new ContinuityLane(row.SeriesId, row.SeriesName, Outside: true));
            }
        }

        var connectors = new List<EventMapConnector>();
        foreach (var r in data.Relations)
        {
            if (!eventBlock.TryGetValue(r.SourceId, out int sourceBlock) || !eventBlock.TryGetValue(r.TargetId, out int targetBlock))
            {
                continue;
            }

            string typeLabel = RelationLabel(r.Type);
            string tooltip = $"{typeLabel} · {r.Provenance}";
            if (EventChronology.Direction(r.SourceId, r.TargetId, r.Type) is { } direction)
            {
                connectors.Add(new EventMapConnector(eventBlock[direction.Earlier], eventBlock[direction.Later],
                    r.Type == RelationType.Continuation ? "Continues" : typeLabel, IsOrdered: true, r.IsYours, tooltip));
            }
            else if (r.Type == RelationType.Crossover)
            {
                connectors.Add(new EventMapConnector(Math.Min(sourceBlock, targetBlock), Math.Max(sourceBlock, targetBlock),
                    typeLabel, IsOrdered: false, r.IsYours, tooltip));
            }
        }

        var overlapPairs = new HashSet<(int Low, int High)>();
        foreach (var sourceEvent in visible.Concat(unplaced))
        {
            if (string.IsNullOrWhiteSpace(sourceEvent.CbroListKey))
            {
                continue;
            }

            foreach (var note in sourceEvent.CbroOverlapNotes ?? Array.Empty<CbroOverlapNoteData>())
            {
                string targetKey = EventNameKeys.Key(note.TargetName);
                if (targetKey.Length == 0)
                {
                    continue;
                }

                var matches = data.Events.Where(candidate => candidate.Id != sourceEvent.Id
                    && string.Equals(candidate.CbroListKey, sourceEvent.CbroListKey, StringComparison.OrdinalIgnoreCase)
                    && EventNameKeys.Key(candidate.CbroSourceName ?? string.Empty) == targetKey
                    && (note.TargetYear is null || candidate.CbroSourceYear == note.TargetYear))
                    .ToList();
                if (matches.Count != 1 || !eventBlock.TryGetValue(sourceEvent.Id, out int sourceBlock)
                    || !eventBlock.TryGetValue(matches[0].Id, out int targetBlock) || sourceBlock == targetBlock)
                {
                    continue;
                }

                var pair = sourceBlock < targetBlock ? (sourceBlock, targetBlock) : (targetBlock, sourceBlock);
                if (!overlapPairs.Add(pair))
                {
                    int existingIndex = connectors.FindIndex(c => c.Kind == EventMapConnectorKind.Overlap
                        && c.FromBlock == pair.Item1 && c.ToBlock == pair.Item2);
                    if (existingIndex >= 0)
                    {
                        var existing = connectors[existingIndex];
                        connectors[existingIndex] = existing with { Tooltip = existing.Tooltip + Environment.NewLine + note.OriginalText };
                    }

                    continue;
                }

                connectors.Add(new EventMapConnector(pair.Item1, pair.Item2, "Occurs during", IsOrdered: false, IsYours: false,
                    $"Occurs during · CBRO · {sourceEvent.Name}: {note.OriginalText}", EventMapConnectorKind.Overlap));
            }
        }

        int eventIssues = rows.Count(r => r.OwnerEventId is not null);
        var events = data.Events.Select(e => new EventMapEventRef(e.Id, e.Name)).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new ContinuityMapSource(data.ContinuityId, data.Name, publicationOrder, rows, blocks, lanes, connectors, events,
            eventIssues, publicationOrder ? rows.Count : data.LooseIssues.Count, data.PendingDuplicatePairs);
    }

    /// <summary>A row's publication date key: <see cref="EventMapRow.SortDate"/> (GCD on-sale date when matched), else its cover date.</summary>
    internal static int? SortKey(EventMapRow row) => row.SortDate ?? EventChronology.DateKey(row.Year, row.Month);

    /// <summary>Publication order: date (<see cref="SortKey"/>), then the series' place in the continuity, then issue number; undated last.</summary>
    internal static List<EventMapRow> SortLoose(IEnumerable<EventMapRow> rows, IReadOnlyDictionary<int, int> seriesOrder) => rows
        .OrderBy(r => SortKey(r) ?? int.MaxValue)
        .ThenBy(r => seriesOrder.GetValueOrDefault(r.SeriesId, int.MaxValue))
        .ThenBy(r => float.TryParse(r.Number, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? n : float.MaxValue)
        .ThenBy(r => r.Number, StringComparer.OrdinalIgnoreCase)
        .ThenBy(r => r.IssueId)
        .ToList();

    private static string BetweenLabel(IReadOnlyList<EventMapRow> rows)
    {
        var years = rows.Select(r => r.Year).OfType<int>().ToList();
        if (years.Count == 0)
        {
            return "Outside events · undated";
        }

        int min = years.Min();
        int max = years.Max();
        return min == max ? $"Between events · {min}" : $"Between events · {min}–{max}";
    }

    private static string RelationLabel(RelationType type) => type switch
    {
        RelationType.Prequel => "Prequel",
        RelationType.Sequel => "Sequel",
        RelationType.Continuation => "Continuation",
        RelationType.Crossover => "Crossover",
        RelationType.TieIn => "Tie-in",
        RelationType.MainEventSeries => "Main event",
        _ => type.ToString(),
    };
}
