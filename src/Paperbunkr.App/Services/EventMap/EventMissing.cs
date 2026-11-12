using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>
/// Loads an event's <see cref="EventMissingData"/> (docs/superpowers/specs/2026-11-11-continuity-map-completeness-design.md §2, §5): its saved
/// provider lists resolved to ghosts against the members and the library, and the numbers the library owns in each member series.
/// Read-only. One <see cref="EventMissingLoader"/> serves every event of a continuity map, so the library's series names load once.
/// </summary>
public sealed class EventMissingLoader
{
    private readonly PaperbunkrDbContext _context;
    private List<(int Id, string Name)>? _seriesNames;

    public EventMissingLoader(PaperbunkrDbContext context) => _context = context;

    public EventMissingData Load(int storyEventId, IReadOnlyList<EventMapRow> members)
    {
        var identity = _context.StoryEvents.AsNoTracking().Where(e => e.Id == storyEventId)
            .Select(e => new { e.MetronArcId, e.ComicVineArcId, e.IdentityConflict }).FirstOrDefault();
        bool hasIdentity = identity is not null && (!string.IsNullOrWhiteSpace(identity.MetronArcId)
            || (!string.IsNullOrWhiteSpace(identity.ComicVineArcId) && identity.IdentityConflict is null));

        var seriesIds = members.Select(m => m.SeriesId).Where(id => id > 0).Distinct().ToList();
        var owned = _context.Issues.AsNoTracking()
            .Where(i => seriesIds.Contains(i.SeriesId) && !i.IsPlaceholder)
            .Select(i => new { i.SeriesId, i.Number })
            .ToList()
            .GroupBy(i => i.SeriesId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(i => i.Number ?? string.Empty).Where(n => n.Length > 0).ToList());

        var lists = EventArcSnapshots.Load(_context, new[] { storyEventId });
        if (lists.Count == 0)
        {
            return new EventMissingData(Array.Empty<EventGhost>(), null, hasIdentity, owned);
        }

        var entries = lists.SelectMany(l => l.Entries).ToList();
        var metronIds = entries.Select(e => e.MetronIssueId).OfType<string>().ToHashSet();
        var cvIds = entries.Select(e => e.ComicVineIssueId).OfType<string>().ToHashSet();
        var allIds = metronIds.Concat(cvIds).Distinct().ToList();
        var external = _context.ComicMetadataExternalIds.AsNoTracking()
            .Where(x => x.EntityKind == ComicMetadataEntityKind.Issue && allIds.Contains(x.ExternalId))
            .Select(x => new { x.Provider, x.ExternalId, x.EntityId })
            .ToList()
            .GroupBy(x => (x.Provider, x.ExternalId))
            .ToDictionary(g => g.Key, g => g.Select(x => x.EntityId).Distinct().ToList());
        int? ByExternalId(ComicProvider provider, string id) =>
            external.TryGetValue((provider, id), out var matches) && matches.Count == 1 ? matches[0] : null;

        // "Likely owned": an owned issue of a same-named series with the same number and a year within one.
        _seriesNames ??= _context.Series.AsNoTracking().Select(s => new { s.Id, s.Name }).ToList().Select(s => (s.Id, s.Name ?? string.Empty)).ToList();
        var entrySeries = entries.Select(e => e.Series).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var matched = entrySeries.ToDictionary(n => n, n => _seriesNames.Where(s => EventGhostResolver.SameSeries(s.Name, n)).Select(s => s.Id).ToList(),
            StringComparer.OrdinalIgnoreCase);
        var candidateSeries = matched.Values.SelectMany(v => v).Distinct().ToList();
        var candidates = _context.Issues.AsNoTracking()
            .Where(i => candidateSeries.Contains(i.SeriesId) && !i.IsPlaceholder)
            .Select(i => new { i.SeriesId, i.Number, i.Year })
            .ToList();
        bool LikelyOwned(string series, string number, int year) =>
            matched.TryGetValue(series, out var ids) && candidates.Any(i => ids.Contains(i.SeriesId) && IssueNumbers.Equal(i.Number, number)
                && (year <= 0 || i.Year is not int y || Math.Abs(y - year) <= 1));

        var ghostMembers = members.Where(m => !m.IsGhost).Select(m => new EventGhostMember(m.IssueId, m.SeriesId, m.SeriesName, m.Number)).ToList();
        var ghosts = EventGhostResolver.Resolve(lists, ghostMembers, ByExternalId, LikelyOwned);
        return new EventMissingData(ghosts, lists.Max(l => l.FetchedAt), hasIdentity, owned);
    }
}

/// <summary>
/// Puts ghosts into an event's rows (§2 "Placement"): each one right after the member it follows in the provider list (before the first
/// member when it follows none), in its series' lane, or in a lane of its own for a series with nothing on the map. Rows are renumbered in
/// order; ghosts get negative ids so they can never be mistaken for a real membership or issue.
/// </summary>
public static class EventGhostPlacement
{
    /// <param name="nextId">Shared counter for negative ids (a continuity map places ghosts for many events).</param>
    /// <param name="pseudoSeries">Shared lane ids for series with nothing on the map, keyed by series name.</param>
    public static IReadOnlyList<EventMapRow> Inject(IReadOnlyList<EventMapRow> rows, IReadOnlyList<EventGhost> ghosts, ref int nextId,
        Dictionary<string, int> pseudoSeries)
    {
        if (ghosts.Count == 0)
        {
            return rows;
        }

        var after = ghosts.GroupBy(g => g.AfterIssueId ?? 0).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<EventMapRow>(rows.Count + ghosts.Count);
        int position = 0;
        void AddGhosts(int key, ref int id)
        {
            if (!after.TryGetValue(key, out var list))
            {
                return;
            }

            foreach (var ghost in list)
            {
                result.Add(ToRow(ghost, ++position, --id, pseudoSeries));
            }

            after.Remove(key);
        }

        AddGhosts(0, ref nextId);
        foreach (var row in rows)
        {
            result.Add(row with { Position = ++position });
            AddGhosts(row.IssueId, ref nextId);
        }

        // A ghost whose anchor is filtered off the map goes at the end rather than disappearing.
        foreach (var key in after.Keys.ToList())
        {
            AddGhosts(key, ref nextId);
        }

        return result;
    }

    private static EventMapRow ToRow(EventGhost ghost, int position, int id, Dictionary<string, int> pseudoSeries)
    {
        int seriesId = ghost.LaneSeriesId ?? PseudoSeries(ghost.Entry.Series, pseudoSeries);
        var (year, month) = YearMonth(ghost.Entry);
        return new EventMapRow(id, id, position, EventMembershipRole.Core, seriesId, ghost.Entry.Series, ghost.Entry.Number,
            year, month, FileIsMissing: false, EventMapReadState.Unread, Summary: ghost.Entry.Title, Ghost: ghost);
    }

    private static int PseudoSeries(string name, Dictionary<string, int> pseudoSeries)
    {
        string key = SeriesNames.Key(name);
        if (!pseudoSeries.TryGetValue(key, out int id))
        {
            id = -(pseudoSeries.Count + 1);
            pseudoSeries[key] = id;
        }

        return id;
    }

    private static (int? Year, int? Month) YearMonth(EventArcEntry entry)
    {
        foreach (var text in new[] { entry.CoverDate, entry.StoreDate })
        {
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                return (date.Year, date.Month);
            }
        }

        return (entry.Year > 0 ? entry.Year : null, null);
    }
}

/// <summary>One number missing from a lane (§5).</summary>
public sealed record EventGap(int SeriesId, string SeriesName, string Number);

/// <summary>
/// The gap check (§5), used only when the event has no saved provider list: integer issue numbers strictly between a lane's lowest and
/// highest numeric member numbers that the library doesn't own at all in that series. Owned non-members aren't gaps (the event may skip
/// them on purpose). Capped at <see cref="Cap"/>.
/// </summary>
public static class EventGapCheck
{
    public const int Cap = 50;

    public static IReadOnlyList<EventGap> Find(IEnumerable<EventMapRow> rows, IReadOnlyDictionary<int, IReadOnlyList<string>> ownedNumbers)
    {
        var gaps = new List<EventGap>();
        foreach (var lane in rows.Where(r => !r.IsGhost && r.SeriesId > 0).GroupBy(r => r.SeriesId))
        {
            var numbers = lane.Select(r => Whole(r.Number)).OfType<int>().ToList();
            if (numbers.Count < 2)
            {
                continue;
            }

            var owned = ownedNumbers.GetValueOrDefault(lane.Key) ?? Array.Empty<string>();
            var ownedWhole = owned.Select(Whole).OfType<int>().ToHashSet();
            for (int n = numbers.Min() + 1; n < numbers.Max(); n++)
            {
                if (!numbers.Contains(n) && !ownedWhole.Contains(n))
                {
                    gaps.Add(new EventGap(lane.Key, lane.First().SeriesName, n.ToString(CultureInfo.InvariantCulture)));
                    if (gaps.Count >= Cap)
                    {
                        return gaps;
                    }
                }
            }
        }

        return gaps;
    }

    private static int? Whole(string? number) =>
        int.TryParse(number?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;
}
