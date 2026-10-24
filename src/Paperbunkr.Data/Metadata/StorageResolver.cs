using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Metadata;

/// <summary>
/// Storage and file-type breakdown (docs/superpowers/specs/2026-10-12-insights-pitch-completion-design.md §4). Comics only: <see cref="Issue.FileSize"/>
/// is the only stored size, and a book has none (adding the column and backfill is its own migration). Remote issues are excluded - not on this disk.
/// "File type" is the extension (cbz, cbr, pdf...), not the publication format the Composition card already charts.
/// Reclaimable space is the sum of the non-recommended copies in each duplicate group, via <see cref="DuplicateKeeperRanker"/>; groups whose
/// members are all <see cref="Issue.DuplicateAcknowledged"/> are skipped.
/// </summary>
public static class StorageResolver
{
    public const int LargestCount = 10;

    public const int PublisherCount = 8;

    public static StorageData Build(PaperbunkrDbContext context)
    {
        var issues = context.Issues.AsNoTracking()
            .Include(i => i.Series)
            .Where(i => !i.IsPlaceholder)
            .ToList();
        int books = context.Books.AsNoTracking().Count();
        return Compute(issues, books);
    }

    internal static StorageData Compute(IReadOnlyCollection<Issue> issues, int bookCount)
    {
        var measured = issues.Where(i => i.FileSize is > 0).ToList();

        string Publisher(Issue i) => !string.IsNullOrWhiteSpace(i.Publisher) ? i.Publisher.Trim()
            : !string.IsNullOrWhiteSpace(i.Series?.Publisher) ? i.Series!.Publisher!.Trim() : "Unknown";

        var byType = measured
            .GroupBy(i => i.FileFormat() ?? "unknown")
            .Select(g => new StorageSlice(g.Key.ToUpperInvariant(), g.Sum(i => i.FileSize!.Value), g.Count()))
            .OrderByDescending(s => s.Bytes)
            .ToList();

        var byPublisher = measured
            .GroupBy(Publisher, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StorageSlice(g.Key, g.Sum(i => i.FileSize!.Value), g.Count()))
            .OrderByDescending(s => s.Bytes).ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase)
            .Take(PublisherCount)
            .ToList();

        var largest = measured
            .OrderByDescending(i => i.FileSize!.Value).ThenBy(i => i.Id)
            .Take(LargestCount)
            .Select(i => new LargeFile(i.Id, i.SeriesId, TitleOf(i), i.FileSize!.Value, (i.FileFormat() ?? "?").ToUpperInvariant()))
            .ToList();

        long reclaimable = 0;
        int groups = 0;
        foreach (var group in SmartListQueryBuilder.BuildDuplicateGroups(issues.ToList()))
        {
            if (group.All(i => i.DuplicateAcknowledged))
            {
                continue;
            }

            var keeper = DuplicateKeeperRanker.Recommend(group);
            if (keeper is null)
            {
                continue;
            }

            long saved = group.Where(i => i.Id != keeper.IssueId).Sum(i => i.FileSize ?? 0);
            if (saved > 0)
            {
                reclaimable += saved;
                groups++;
            }
        }

        return new StorageData(
            measured.Sum(i => i.FileSize!.Value),
            measured.Count,
            issues.Count - measured.Count,
            bookCount,
            byType,
            byPublisher,
            largest,
            reclaimable,
            groups);
    }

    private static string TitleOf(Issue i)
    {
        string series = i.Series?.Name ?? i.Title ?? "Untitled";
        return string.IsNullOrWhiteSpace(i.Number) ? series : $"{series} #{i.Number}";
    }
}

public sealed record StorageData(
    long TotalBytes,
    int MeasuredFiles,
    int UnmeasuredFiles,
    int BookCount,
    IReadOnlyList<StorageSlice> ByFileType,
    IReadOnlyList<StorageSlice> ByPublisher,
    IReadOnlyList<LargeFile> Largest,
    long ReclaimableBytes,
    int DuplicateGroups)
{
    public bool HasData => MeasuredFiles > 0;

    public static StorageData Empty { get; } = new(
        0, 0, 0, 0, Array.Empty<StorageSlice>(), Array.Empty<StorageSlice>(), Array.Empty<LargeFile>(), 0, 0);
}

public sealed record StorageSlice(string Label, long Bytes, int Files);

public sealed record LargeFile(int IssueId, int SeriesId, string Title, long Bytes, string FileType);
