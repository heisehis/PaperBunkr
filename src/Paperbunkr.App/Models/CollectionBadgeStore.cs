using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// The prebuilt collection badges the Library tiles draw (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md). A static push-cache like
/// <c>CosmeticThumbnailSettings</c>: the Library load fills it (only when "collection badges" is on and no collection is the active filter) and clears it
/// otherwise, so a tile's <see cref="ITileCollectionSource"/> is one dictionary lookup with no per-tile state and no allocation.
/// </summary>
public static class CollectionBadgeStore
{
    private static readonly IReadOnlyList<CollectionBadge> None = Array.Empty<CollectionBadge>();
    private static IReadOnlyDictionary<int, IReadOnlyList<CollectionBadge>> _bySeries = new Dictionary<int, IReadOnlyList<CollectionBadge>>();
    private static IReadOnlyDictionary<int, IReadOnlyList<CollectionBadge>> _byIssue = new Dictionary<int, IReadOnlyList<CollectionBadge>>();

    /// <summary>True while a collection is the active Library filter: badges are not drawn inside a collection view.</summary>
    public static bool Suppressed { get; set; }

    public static void Clear()
    {
        _bySeries = new Dictionary<int, IReadOnlyList<CollectionBadge>>();
        _byIssue = new Dictionary<int, IReadOnlyList<CollectionBadge>>();
    }

    /// <summary>Builds the badge lists from the memberships and the collections (id, name, accent) and publishes them.</summary>
    public static void Set(CollectionMemberships memberships, IEnumerable<Collection> collections)
    {
        var byId = collections.ToDictionary(
            c => c.Id,
            c => new CollectionBadge(Color.TryParse(c.AccentColor ?? string.Empty, out var accent) ? accent : null, c.Name));

        IReadOnlyList<CollectionBadge> Resolve(IReadOnlyList<int> ids) =>
            ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

        _bySeries = memberships.BySeries.ToDictionary(p => p.Key, p => Resolve(p.Value));
        _byIssue = memberships.ByIssue.ToDictionary(p => p.Key, p => Resolve(p.Value));
    }

    public static IReadOnlyList<CollectionBadge> ForSeries(int seriesId) =>
        !Suppressed && _bySeries.TryGetValue(seriesId, out var badges) ? badges : None;

    /// <summary>An issue's own memberships followed by its series', without duplicates.</summary>
    public static IReadOnlyList<CollectionBadge> ForIssue(int issueId, int seriesId)
    {
        if (Suppressed)
        {
            return None;
        }

        bool own = _byIssue.TryGetValue(issueId, out var issueBadges);
        bool viaSeries = _bySeries.TryGetValue(seriesId, out var seriesBadges);
        if (!own && !viaSeries)
        {
            return None;
        }

        if (!own)
        {
            return seriesBadges!;
        }

        if (!viaSeries)
        {
            return issueBadges!;
        }

        return issueBadges!.Concat(seriesBadges!.Where(b => !issueBadges!.Contains(b))).ToList();
    }
}
