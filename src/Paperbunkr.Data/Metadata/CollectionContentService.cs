using System;
using System.Collections.Generic;
using System.Linq;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>What <see cref="CollectionContentService.MapVolumes"/> did.</summary>
/// <param name="Mapped">Files given a chapter range.</param>
/// <param name="Unmatched">Numbered files MangaDex has no volume of that number for.</param>
public sealed record VolumeMappingResult(int Mapped, int Unmatched);

/// <summary>
/// Reads and writes <see cref="CollectionContent"/> (Metadata Model Phase 7). The one automatic source is MangaDex's own volume list: when a
/// series' files are numbered by volume, "volume 5 = chapters 41-50" is exactly what MangaDex says, so each file is given that range. A range
/// the user entered (<see cref="CollectionContentSource.Manual"/>) is never replaced or cleared by it.
/// </summary>
public static class CollectionContentService
{
    /// <summary>
    /// Gives every numbered issue of <paramref name="seriesId"/> the chapters MangaDex files under the volume with that number. Replaces this
    /// source's earlier rows for those issues, so running it again after MangaDex's list changed is safe. The caller saves.
    /// </summary>
    public static VolumeMappingResult MapVolumes(PaperbunkrDbContext context, int seriesId, MangaDexAggregate aggregate, DateTime nowUtc)
    {
        var chaptersByVolume = aggregate.Chapters
            .Where(c => c.Volume is not null && MangaDexAggregateClient.TryParseNumber(c.Volume, out _) && MangaDexAggregateClient.TryParseNumber(c.Chapter, out _))
            .GroupBy(c =>
            {
                MangaDexAggregateClient.TryParseNumber(c.Volume, out float volume);
                return volume;
            })
            .ToDictionary(g => g.Key, g => g.OrderBy(c => Key(c.Chapter)).ToList());

        var issues = context.Issues.Where(i => i.SeriesId == seriesId).ToList();
        var issueIds = issues.Select(i => i.Id).ToList();
        var existing = context.CollectionContents.Where(c => issueIds.Contains(c.IssueId)).ToList();
        var manual = existing.Where(c => c.Source == CollectionContentSource.Manual).Select(c => c.IssueId).ToHashSet();
        context.CollectionContents.RemoveRange(existing.Where(c => c.Source == CollectionContentSource.MangaDexVolume));

        int mapped = 0, unmatched = 0;
        foreach (var issue in issues.Where(i => !manual.Contains(i.Id)))
        {
            if (issue.NumberSortKey() is not float number)
            {
                continue;
            }

            if (!chaptersByVolume.TryGetValue(number, out var chapters))
            {
                unmatched++;
                continue;
            }

            context.CollectionContents.Add(new CollectionContent
            {
                IssueId = issue.Id,
                FromNumber = chapters[0].Chapter,
                ToNumber = chapters[^1].Chapter,
                FromKey = Key(chapters[0].Chapter),
                ToKey = Key(chapters[^1].Chapter),
                Source = CollectionContentSource.MangaDexVolume,
                CreatedAt = nowUtc,
            });
            mapped++;
        }

        return new VolumeMappingResult(mapped, unmatched);
    }

    /// <summary>Removes the ranges <see cref="MapVolumes"/> wrote for a series (the user's own are kept); returns how many. The caller saves.</summary>
    public static int ClearMapped(PaperbunkrDbContext context, int seriesId)
    {
        var ids = context.Issues.Where(i => i.SeriesId == seriesId).Select(i => i.Id).ToList();
        var rows = context.CollectionContents.Where(c => ids.Contains(c.IssueId) && c.Source == CollectionContentSource.MangaDexVolume).ToList();
        context.CollectionContents.RemoveRange(rows);
        return rows.Count;
    }

    /// <summary>True when any file of the series has a range recorded.</summary>
    public static bool HasRanges(PaperbunkrDbContext context, int seriesId) =>
        context.CollectionContents.Any(c => context.Issues.Any(i => i.Id == c.IssueId && i.SeriesId == seriesId));

    private static float Key(string number) => MangaDexAggregateClient.TryParseNumber(number, out float value) ? value : float.MaxValue;
}
