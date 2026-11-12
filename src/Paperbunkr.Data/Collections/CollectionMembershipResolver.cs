using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Collections;

/// <summary>Which collections each series and each issue belongs to (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md, membership badges).</summary>
public sealed record CollectionMemberships(
    IReadOnlyDictionary<int, IReadOnlyList<int>> BySeries,
    IReadOnlyDictionary<int, IReadOnlyList<int>> ByIssue)
{
    public static CollectionMemberships Empty { get; } = new(new Dictionary<int, IReadOnlyList<int>>(), new Dictionary<int, IReadOnlyList<int>>());
}

/// <summary>
/// One pass over the manual <see cref="CollectionItem"/> rows, plus <see cref="CollectionResolver.GetMembers"/> only for collections that have a rule slot
/// (their matches count as memberships). Collection ids are listed in display order (<see cref="CollectionOrdering"/>), so a badge list reads like the sidebar.
/// </summary>
public static class CollectionMembershipResolver
{
    public static CollectionMemberships GetMemberships(PaperbunkrDbContext context)
    {
        var order = CollectionOrdering.Apply(context.Collections.AsNoTracking())
            .Select(c => new { c.Id, IsSmart = c.IssueSmartListId != null || c.SeriesSmartListId != null || c.NovelSmartListId != null })
            .ToList();
        var rank = order.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i);

        var bySeries = new Dictionary<int, HashSet<int>>();
        var byIssue = new Dictionary<int, HashSet<int>>();

        static void Add(Dictionary<int, HashSet<int>> map, int key, int collectionId)
        {
            if (!map.TryGetValue(key, out var set))
            {
                map[key] = set = new HashSet<int>();
            }

            set.Add(collectionId);
        }

        foreach (var item in context.CollectionItems.AsNoTracking().Select(ci => new { ci.CollectionId, ci.SeriesId, ci.IssueId }).ToList())
        {
            if (item.SeriesId is int seriesId)
            {
                Add(bySeries, seriesId, item.CollectionId);
            }
            else if (item.IssueId is int issueId)
            {
                Add(byIssue, issueId, item.CollectionId);
            }
        }

        foreach (var collection in order.Where(c => c.IsSmart))
        {
            foreach (var member in CollectionResolver.GetMembers(context, collection.Id))
            {
                if (member.CollectionItemId is not null)
                {
                    continue; // manual: already counted
                }

                if (member.Kind == CollectionMemberKind.Series)
                {
                    Add(bySeries, member.TargetId, collection.Id);
                }
                else if (member.Kind == CollectionMemberKind.Issue)
                {
                    Add(byIssue, member.TargetId, collection.Id);
                }
            }
        }

        static IReadOnlyDictionary<int, IReadOnlyList<int>> Freeze(Dictionary<int, HashSet<int>> map, Dictionary<int, int> rank) =>
            map.ToDictionary(
                p => p.Key,
                p => (IReadOnlyList<int>)p.Value.OrderBy(id => rank.GetValueOrDefault(id, int.MaxValue)).ToList());

        return new CollectionMemberships(Freeze(bySeries, rank), Freeze(byIssue, rank));
    }
}
