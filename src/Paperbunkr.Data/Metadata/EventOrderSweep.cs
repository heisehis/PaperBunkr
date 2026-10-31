using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Metadata;

public sealed record EventOrderSummary(int CbroChecked, int CbroMatched, int MetronChecked, int MetronMatched, string? Problem)
{
    public string Text
    {
        get
        {
            var parts = new List<string>();
            if (CbroChecked > 0)
            {
                parts.Add($"{CbroMatched} of {CbroChecked} events found in Comic Book Reading Orders");
            }

            if (MetronChecked > 0)
            {
                parts.Add($"{MetronMatched} of {MetronChecked} events found in Metron reading lists");
            }

            if (Problem is not null)
            {
                parts.Add(Problem);
            }

            return parts.Count == 0 ? "reading orders up to date" : string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Collects what the outside reading orders say about where each story event sits (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §4):
/// the event's position in the Comic Book Reading Orders Marvel/DC timelines, and the lists before and after it in Metron's EVENT reading lists. Only
/// writes <see cref="EventOrderEvidence"/>; <see cref="EventChronology"/> reads the ranks and <see cref="EventConnectorSweep"/> turns the Metron
/// neighbours into relations. An event nothing matched is recorded as "looked, found nothing" so it is not looked up again until the next refresh.
/// </summary>
public static class EventOrderSweep
{
    public static readonly TimeSpan CbroRefreshAfter = TimeSpan.FromDays(7);

    public static readonly TimeSpan MetronRecheckAfter = TimeSpan.FromDays(30);

    public const int DefaultMetronBatchSize = 20;

    /// <summary>When an event has several same-named timeline entries ("Secret Wars" 1984 / 2015), only one whose year is within this many years of the event's is taken.</summary>
    private const int YearTolerance = 3;

    internal sealed record EventInfo(int Id, string Name, HashSet<string> Keys, int? Year);

    public static async Task<EventOrderSummary> RunAsync(
        Func<PaperbunkrDbContext> contextFactory, ICbroTimeline? cbro, IMetronEventLists? metron, int metronBatchSize, DateTime nowUtc, CancellationToken cancellationToken)
    {
        int cbroChecked = 0, cbroMatched = 0, metronChecked = 0, metronMatched = 0;
        string? problem = null;

        if (cbro is not null)
        {
            try
            {
                (cbroChecked, cbroMatched) = await RunCbroAsync(contextFactory, cbro, nowUtc, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ReadingListSourceException or System.Net.Http.HttpRequestException)
            {
                problem = "Comic Book Reading Orders could not be read right now";
            }
        }

        if (metron is not null)
        {
            string? stopped;
            (metronChecked, metronMatched, stopped) = await RunMetronAsync(contextFactory, metron, Math.Max(1, metronBatchSize), nowUtc, cancellationToken).ConfigureAwait(false);
            if (stopped is not null)
            {
                // A rate limit, daily quota or login hold: the rest of the batch waits for the next run.
                problem = problem is null ? $"Metron stopped early: {stopped}" : problem;
            }
        }

        return new EventOrderSummary(cbroChecked, cbroMatched, metronChecked, metronMatched, problem);
    }

    private static async Task<(int Checked, int Matched)> RunCbroAsync(Func<PaperbunkrDbContext> contextFactory, ICbroTimeline cbro, DateTime nowUtc, CancellationToken cancellationToken)
    {
        List<EventInfo> events;
        bool refresh;
        using (var context = contextFactory())
        {
            events = LoadEvents(context);
            var checkedAt = context.EventOrderEvidence.AsNoTracking()
                .Where(e => e.Provider == EventOrderProvider.Cbro)
                .Select(e => new { e.StoryEventId, e.CheckedAt })
                .ToList();
            var known = checkedAt.Select(c => c.StoryEventId).ToHashSet();
            refresh = events.Any(e => !known.Contains(e.Id)) || checkedAt.Count == 0 || nowUtc - checkedAt.Min(c => c.CheckedAt) >= CbroRefreshAfter;
        }

        if (!refresh || events.Count == 0)
        {
            return (0, 0);
        }

        var entries = await cbro.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return (0, 0);       // an unreadable page is not "no event is listed": keep what we had
        }

        var byKey = new Dictionary<string, List<CbroTimelineEntry>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (string key in new[] { EventNameKeys.Key(entry.Name) }.Where(k => k.Length > 0).Distinct())
            {
                if (!byKey.TryGetValue(key, out var list))
                {
                    byKey[key] = list = new List<CbroTimelineEntry>();
                }

                list.Add(entry);
            }
        }

        int matched = 0;
        using var write = contextFactory();
        var rows = write.EventOrderEvidence.Where(e => e.Provider == EventOrderProvider.Cbro)
            .Include(e => e.CbroOverlapNotes).ToDictionary(e => e.StoryEventId);
        foreach (var e in events)
        {
            var entry = Match(e, byKey);
            if (!rows.TryGetValue(e.Id, out var row))
            {
                row = new EventOrderEvidence { StoryEventId = e.Id, Provider = EventOrderProvider.Cbro };
                write.EventOrderEvidence.Add(row);
            }

            row.CheckedAt = nowUtc;
            row.SourceId = entry is null ? null : entry.Url ?? $"{entry.ListKey}:{entry.Rank}";
            row.SourceName = entry?.Name;
            row.ListKey = entry?.ListKey;
            row.Rank = entry?.Rank;
            row.SourceYear = entry?.Year;
            row.CbroOverlapNotes.Clear();
            foreach (var note in entry?.OverlapNotes ?? Array.Empty<CbroTimelineOverlapNote>())
            {
                row.CbroOverlapNotes.Add(new EventOrderOverlapNote
                {
                    OriginalText = note.OriginalText,
                    TargetName = note.TargetName,
                    TargetYear = note.TargetYear,
                });
            }
            if (entry is not null)
            {
                matched++;
            }
        }

        write.SaveChanges();
        return (events.Count, matched);
    }

    /// <summary>The one timeline entry an event is, or null: no entry, or several that cannot be told apart by year.</summary>
    private static CbroTimelineEntry? Match(EventInfo e, Dictionary<string, List<CbroTimelineEntry>> byKey)
    {
        var candidates = e.Keys.SelectMany(k => byKey.GetValueOrDefault(k) ?? new List<CbroTimelineEntry>()).Distinct().ToList();
        if (candidates.Count <= 1)
        {
            return candidates.FirstOrDefault();
        }

        if (e.Year is not int year)
        {
            return null;
        }

        var near = candidates.Where(c => c.Year is int y && Math.Abs(y - year) <= YearTolerance).ToList();
        return near.Count == 1 ? near[0] : null;
    }

    private static async Task<(int Checked, int Matched, string? Stopped)> RunMetronAsync(
        Func<PaperbunkrDbContext> contextFactory, IMetronEventLists metron, int batchSize, DateTime nowUtc, CancellationToken cancellationToken)
    {
        List<EventInfo> due;
        using (var context = contextFactory())
        {
            var events = LoadEvents(context);
            var checkedAt = context.EventOrderEvidence.AsNoTracking()
                .Where(e => e.Provider == EventOrderProvider.MetronList)
                .Select(e => new { e.StoryEventId, e.CheckedAt })
                .ToDictionary(e => e.StoryEventId, e => e.CheckedAt);
            due = events
                .Where(e => !checkedAt.TryGetValue(e.Id, out var at) || nowUtc - at >= MetronRecheckAfter)
                .OrderBy(e => checkedAt.TryGetValue(e.Id, out var at) ? at : DateTime.MinValue).ThenBy(e => e.Id)
                .Take(batchSize)
                .ToList();
        }

        int matched = 0, done = 0;
        string? stopped = null;
        foreach (var e in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetronEventListInfo? info;
            try
            {
                info = await metron.FindEventListAsync(e.Name, cancellationToken).ConfigureAwait(false);
            }
            catch (ComicVineException ex)
            {
                stopped = ex.Message;
                break;
            }

            done++;

            using var write = contextFactory();
            var row = write.EventOrderEvidence.FirstOrDefault(x => x.StoryEventId == e.Id && x.Provider == EventOrderProvider.MetronList);
            if (row is null)
            {
                row = new EventOrderEvidence { StoryEventId = e.Id, Provider = EventOrderProvider.MetronList };
                write.EventOrderEvidence.Add(row);
            }

            row.CheckedAt = nowUtc;
            row.SourceId = info?.Id;
            row.SourceName = info?.Name;
            row.PreviousName = info?.PreviousName;
            row.NextName = info?.NextName;
            write.SaveChanges();
            if (info is not null)
            {
                matched++;
            }
        }

        return (done, matched, stopped);
    }

    internal static List<EventInfo> LoadEvents(PaperbunkrDbContext context)
    {
        // Flat queries joined in memory: one projection with nested collections and a Min needs SQL APPLY, which SQLite has not got.
        var events = context.StoryEvents.AsNoTracking().Select(e => new { e.Id, e.Name, e.StartDate }).ToList();
        var aliases = context.StoryEventAliases.AsNoTracking().Select(a => new { a.StoryEventId, a.Key }).ToList()
            .GroupBy(a => a.StoryEventId).ToDictionary(g => g.Key, g => g.Select(a => a.Key).ToList());
        var members = context.EventMemberships.AsNoTracking()
            .Select(m => new { m.StoryEventId, SeriesName = m.Issue!.Series!.Name, m.Issue.Year })
            .ToList()
            .GroupBy(m => m.StoryEventId)
            .ToDictionary(g => g.Key, g => (Series: g.Select(m => m.SeriesName).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList(), Year: g.Min(m => m.Year)));

        return events.Select(e =>
        {
            var (series, year) = members.GetValueOrDefault(e.Id, (new List<string>(), (int?)null));
            return new EventInfo(
                e.Id, e.Name, EventNameKeys.For(e.Name, series, aliases.GetValueOrDefault(e.Id)),
                e.StartDate?.Year ?? year);
        }).ToList();
    }
}
