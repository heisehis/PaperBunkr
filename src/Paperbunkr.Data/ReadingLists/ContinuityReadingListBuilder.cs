using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.ReadingLists;

/// <summary>
/// Materializes a <see cref="ReadingList"/> from a <c>Continuity</c>'s member series in publication
/// order (docs/superpowers/specs/2026-08-27-metadata-model-phase4f-continuity-browse-design.md's
/// deferred "continuity-scoped reading lists"). One-shot snapshot - it does not stay linked or
/// auto-refresh when the continuity's membership changes.
/// </summary>
public static class ContinuityReadingListBuilder
{
    public static ReadingList CreateFromContinuity(PaperbunkrDbContext context, int continuityId)
    {
        var continuity = context.Continuities.FirstOrDefault(c => c.Id == continuityId)
            ?? throw new InvalidOperationException($"Continuity {continuityId} not found.");

        var memberSeriesIds = context.ContinuityMemberships
            .Where(m => m.ContinuityId == continuityId)
            .Select(m => m.SeriesId)
            .ToList();
        var series = context.Series
            .Include(s => s.Issues)
            .Where(s => memberSeriesIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToList();

        var now = DateTime.UtcNow;
        var list = new ReadingList
        {
            Name = $"{continuity.Name} (continuity)",
            SortOrder = context.ReadingLists.Count(),
            CreatedAt = now,
            UpdatedAt = now,
            Type = ReadingListType.PublicationOrder,
            Description = string.IsNullOrWhiteSpace(continuity.Description) ? null : continuity.Description,
        };

        // Chronological across every member series (interleaved by publication date), not series
        // block by series block - that's what "publication order" means for a whole universe.
        int sortOrder = 0;
        var chronological = series
            .SelectMany(s => s.Issues.Select(i => (Issue: i, SeriesName: s.Name)))
            .OrderBy(x => x.Issue.EffectiveYear() ?? int.MaxValue)
            .ThenBy(x => x.Issue.Month ?? 0)
            .ThenBy(x => x.Issue.Day ?? 0)
            .ThenBy(x => x.SeriesName)
            .ThenBy(x => x.Issue.NumberSortKey() ?? float.MaxValue);

        foreach (var (issue, seriesName) in chronological)
        {
            list.Items.Add(new ReadingListItem { IssueId = issue.Id, SortOrder = sortOrder++, GroupLabel = seriesName });
        }

        context.ReadingLists.Add(list);
        context.SaveChanges();
        return list;
    }
}
