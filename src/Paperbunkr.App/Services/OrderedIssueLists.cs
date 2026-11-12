using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>Normal local reading lists for entity appearances and detected runs/arcs.</summary>
public static class OrderedIssueLists
{
    public static int Save(PaperbunkrDbContext context, string name, IEnumerable<int> issueIds)
    {
        var ordered = issueIds.Distinct().ToList();
        var existing = context.Issues.Where(i => ordered.Contains(i.Id) && !i.IsPlaceholder).Select(i => i.Id).ToHashSet();
        var list = new ReadingList { Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            SortOrder = (context.ReadingLists.Select(l => (int?)l.SortOrder).Max() ?? -1) + 1,
            Items = ordered.Where(existing.Contains).Select((id, index) => new ReadingListItem { IssueId = id, SortOrder = index }).ToList() };
        if (list.Items.Count == 0) throw new InvalidOperationException("There are no issues to save.");
        context.ReadingLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }
}
