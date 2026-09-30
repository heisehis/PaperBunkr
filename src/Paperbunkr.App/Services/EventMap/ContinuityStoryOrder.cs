using System.Collections.Generic;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.Services.EventMap;

/// <summary>
/// "Story order" for a continuity reading list (docs/superpowers/specs/2026-09-28-reading-lists-build-from-events-design.md §2, decision
/// Q17): the Continuity map read left to right - events in smart-connector order, each event's issues in its own order, issues between
/// events by date - so the list and the map always agree. Reuses <see cref="ContinuityMapBuilder"/> with the default options (nothing
/// hidden, optional issues included). Group labels are the map's block labels. An issue in two events stays at its first appearance.
/// </summary>
public static class ContinuityStoryOrder
{
    public static IReadOnlyList<ContinuityOrderEntry> Compute(ContinuityMapData data)
    {
        var source = ContinuityMapBuilder.Build(data, ContinuityMapOptions.Default);
        var order = new List<ContinuityOrderEntry>();
        var seen = new HashSet<int>();
        foreach (var block in source.Blocks)
        {
            for (int r = block.FirstRow; r <= block.LastRow; r++)
            {
                int issueId = source.Rows[r].IssueId;
                if (seen.Add(issueId))
                {
                    order.Add(new ContinuityOrderEntry(issueId, string.IsNullOrWhiteSpace(block.Label) ? null : block.Label));
                }
            }
        }

        return order;
    }
}

/// <summary>Computes either continuity order - what Create and Rebuild share.</summary>
public static class ContinuityListOrders
{
    public static IReadOnlyList<ContinuityOrderEntry> Compute(PaperbunkrDbContext context, int continuityId, ContinuityOrderKind kind)
    {
        if (kind == ContinuityOrderKind.PublicationOrder)
        {
            return ContinuityReadingListBuilder.PublicationOrder(context, continuityId);
        }

        using var gcd = GcdDataStore.TryOpen();
        var data = ContinuityMapLoader.LoadData(context, continuityId, gcd);
        return data is null ? new List<ContinuityOrderEntry>() : ContinuityStoryOrder.Compute(data);
    }
}
