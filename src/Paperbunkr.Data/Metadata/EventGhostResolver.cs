using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

public enum EventGhostKind
{
    /// <summary>The library has no copy.</summary>
    Missing,

    /// <summary>The library owns it (exact provider id) but it isn't a member of this event.</summary>
    OwnedNotMember,
}

/// <summary>A member of the event as the ghost resolver needs it, in the event's reading order.</summary>
public sealed record EventGhostMember(int IssueId, int SeriesId, string SeriesName, string Number);

/// <summary>
/// One issue from a provider list that isn't on the map (docs/superpowers/specs/2026-11-11-continuity-map-completeness-design.md §2).
/// <see cref="AfterIssueId"/> is the member issue it follows in the provider's list (null = before the first member);
/// <see cref="LaneSeriesId"/> is the member series whose name it matches (null = a series with nothing on the map).
/// </summary>
public sealed record EventGhost(EventGhostKind Kind, EventArcEntry Entry, string Provider, DateTime FetchedAt, int? OwnedIssueId,
    int? AfterIssueId, int? LaneSeriesId)
{
    /// <summary>This provider's issue id for the entry (the one Wanted requests through).</summary>
    public string? ProviderIssueId => Provider == EventArcSnapshots.Metron ? Entry.MetronIssueId : Entry.ComicVineIssueId;

    /// <summary>This provider's series (volume) id for the entry.</summary>
    public string? ProviderSeriesId => Provider == EventArcSnapshots.Metron ? Entry.MetronSeriesId : Entry.ComicVineSeriesId;
}

/// <summary>
/// Matches a saved provider list against an event (§2). Pure: the library lookups are passed in. Per entry, in list order:
/// an exact provider id that resolves to a member anchors the next ghosts; one that resolves to an owned non-member is an
/// "In your library" ghost; a member with the same series name and number also anchors (provider ids are often missing); an
/// owned issue with the same series name, number and a year within one is "likely owned" and shows nothing; anything else is a
/// "Not in your library" ghost. Metron's list is used first; a ComicVine list only adds entries whose ComicVine id no Metron entry
/// carries. Nothing is written.
/// </summary>
public static class EventGhostResolver
{
    public static IReadOnlyList<EventGhost> Resolve(
        IReadOnlyList<EventArcList> lists,
        IReadOnlyList<EventGhostMember> members,
        Func<ComicProvider, string, int?> ownedByExternalId,
        Func<string, string, int, bool> likelyOwned)
    {
        var ghosts = new List<EventGhost>();
        var memberIds = members.Select(m => m.IssueId).ToHashSet();
        var metron = lists.FirstOrDefault(l => l.Provider == EventArcSnapshots.Metron);
        var metronCvIds = metron?.Entries.Select(e => e.ComicVineIssueId).OfType<string>().ToHashSet() ?? new HashSet<string>();
        var seen = new HashSet<int>();

        foreach (var list in lists.OrderBy(l => l.Provider == EventArcSnapshots.Metron ? 0 : 1))
        {
            bool extrasOnly = list.Provider != EventArcSnapshots.Metron && metron is not null;
            int? anchor = null;
            foreach (var entry in list.Entries)
            {
                int? owned = Owned(entry, ownedByExternalId);
                if (owned is int id && memberIds.Contains(id))
                {
                    anchor = id;
                    continue;
                }

                var byName = owned is null ? members.FirstOrDefault(m => SameSeries(m.SeriesName, entry.Series) && IssueNumbers.Equal(m.Number, entry.Number)) : null;
                if (byName is not null)
                {
                    anchor = byName.IssueId;
                    continue;
                }

                if (extrasOnly && entry.ComicVineIssueId is { } cv && metronCvIds.Contains(cv))
                {
                    continue;                       // Metron already lists this issue
                }

                int? lane = members.FirstOrDefault(m => SameSeries(m.SeriesName, entry.Series))?.SeriesId;
                if (owned is int ownedId)
                {
                    if (seen.Add(ownedId))
                    {
                        ghosts.Add(new EventGhost(EventGhostKind.OwnedNotMember, entry, list.Provider, list.FetchedAt, ownedId, anchor, lane));
                    }

                    continue;
                }

                if (likelyOwned(entry.Series, entry.Number, entry.Year))
                {
                    continue;
                }

                ghosts.Add(new EventGhost(EventGhostKind.Missing, entry, list.Provider, list.FetchedAt, null, anchor, lane));
            }
        }

        return ghosts;
    }

    /// <summary>Series names match ignoring volume markers and a trailing "(2015)"-style year.</summary>
    public static bool SameSeries(string a, string b) =>
        TitleNormalizer.NamesMatch(EventSpineResolver.StripYear(a), EventSpineResolver.StripYear(b), ignoreVolume: true);

    private static int? Owned(EventArcEntry entry, Func<ComicProvider, string, int?> ownedByExternalId)
    {
        if (entry.MetronIssueId is { Length: > 0 } metron && ownedByExternalId(ComicProvider.Metron, metron) is int a)
        {
            return a;
        }

        return entry.ComicVineIssueId is { Length: > 0 } cv ? ownedByExternalId(ComicProvider.ComicVine, cv) : null;
    }
}
