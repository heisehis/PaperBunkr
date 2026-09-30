using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Get-or-create for <see cref="Entities.StoryEvent"/>, the dedup-by-name path
/// <see cref="EventMembershipResolver"/> never had (docs/superpowers/specs/2026-09-17-storyevent-
/// continuity-autopopulate-design.md) - needed so accepting two separate
/// <see cref="StoryArcGroupingResolver"/> candidates for the same arc name doesn't create duplicate
/// <see cref="Entities.StoryEvent"/> rows. Same case-insensitive shape as
/// <see cref="ContinuityResolver.GetOrCreate"/>. Only the provider paths (accepting a suggestion, Issue Properties' look-up) call
/// this, so a newly created event is <see cref="StoryEventOrigin.Provider"/>.
/// </summary>
internal static class StoryEventResolver
{
    /// <summary>
    /// Case-insensitive name match before inserting, so retyping ("Civil War" vs "civil war") doesn't create a near-duplicate event.
    /// Also matches name keys and aliases (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §4): a Metron "Planet Hulk"
    /// joins an existing "Hulk: Planet Hulk" when <paramref name="memberSeriesNames"/> (the incoming issues' series) include Hulk, and a
    /// spelling that was merged away is recognized through its alias.
    /// </summary>
    public static StoryEvent GetOrCreate(PaperbunkrDbContext context, string name, IEnumerable<string>? memberSeriesNames = null)
    {
        string trimmed = name.Trim();
        var events = context.StoryEvents.Include(e => e.Aliases).ToList();
        var existing = events.FirstOrDefault(e => string.Equals(e.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            // Punctuation-variant fold (docs/superpowers/specs/2026-09-17-series-name-matching-and-
            // empty-row-cleanup-design.md) - "Cataclysm: The Ultimates" vs "Cataclysm - The
            // Ultimates" shouldn't spawn a second StoryEvent for the same crossover.
            ?? events.FirstOrDefault(e => TitleNormalizer.NamesMatch(e.Name, trimmed, ignoreVolume: false))
            ?? FindByKeys(context, events, trimmed, memberSeriesNames?.ToList() ?? new List<string>());
        if (existing is not null)
        {
            return existing;
        }

        var storyEvent = new StoryEvent
        {
            Name = trimmed,
            Origin = StoryEventOrigin.Provider,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        return storyEvent;
    }

    /// <summary><see cref="GetOrCreate"/> with the incoming issues' series names looked up, so a series-prefixed spelling matches.</summary>
    public static StoryEvent GetOrCreateForIssues(PaperbunkrDbContext context, string name, IEnumerable<int> issueIds)
    {
        var ids = issueIds.ToList();
        var series = context.Issues.Where(i => ids.Contains(i.Id)).Select(i => i.Series!.Name)
            .AsEnumerable().OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return GetOrCreate(context, name, series);
    }

    private static StoryEvent? FindByKeys(PaperbunkrDbContext context, List<StoryEvent> events, string name, List<string> incomingSeries)
    {
        if (events.Count == 0)
        {
            return null;
        }

        var seriesByEvent = context.EventMemberships
            .Select(m => new { m.StoryEventId, SeriesName = m.Issue!.Series!.Name })
            .AsEnumerable()
            .Where(x => !string.IsNullOrEmpty(x.SeriesName))
            .GroupBy(x => x.StoryEventId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.SeriesName).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

        foreach (var e in events)
        {
            var series = seriesByEvent.TryGetValue(e.Id, out var s) ? s : new List<string>();
            var allSeries = series.Concat(incomingSeries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var eventKeys = EventNameKeys.For(e.Name, allSeries, e.Aliases.Select(a => a.Key));
            if (EventNameKeys.For(name, allSeries).Overlaps(eventKeys))
            {
                return e;
            }
        }

        return null;
    }
}
