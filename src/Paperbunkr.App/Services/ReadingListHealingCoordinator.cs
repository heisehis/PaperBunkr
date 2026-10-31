using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists.Matching;

namespace Paperbunkr.App.Services;

/// <summary>Matching-relevant identity at a committed operation boundary.</summary>
internal sealed record ReadingListIssueIdentity(
    int IssueId,
    int SeriesId,
    string SeriesName,
    string? Number,
    string? Volume,
    int? Year,
    string? Format,
    string? ComicVineIssueId,
    string? ComicVineSeriesId,
    string? MetronIssueId,
    string? MetronSeriesId);

/// <summary>
/// Captures identity and runs the existing conservative healer after an operation has saved. The
/// changed issue set includes every issue in an affected series, so a series rename can find rows
/// that were waiting for any comic in that series.
/// </summary>
internal static class ReadingListHealingCoordinator
{
    public static IReadOnlyDictionary<int, ReadingListIssueIdentity> Capture(
        PaperbunkrDbContext context,
        IEnumerable<int> issueIds,
        bool includeSeriesIssues = false)
    {
        var ids = issueIds.Distinct().ToHashSet();
        if (ids.Count == 0)
        {
            return new Dictionary<int, ReadingListIssueIdentity>();
        }

        if (includeSeriesIssues)
        {
            var seriesIds = context.Issues.Where(i => ids.Contains(i.Id)).Select(i => i.SeriesId).Distinct().ToList();
            ids.UnionWith(context.Issues.Where(i => seriesIds.Contains(i.SeriesId)).Select(i => i.Id).ToList());
        }

        var issues = context.Issues.AsNoTracking().Include(i => i.Series)
            .Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.SeriesId, SeriesName = i.Series == null ? string.Empty : i.Series.Name, i.Number, i.Volume, i.Year, i.Format })
            .ToList();
        var external = context.ComicMetadataExternalIds.AsNoTracking()
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Issue && ids.Contains(e.EntityId))
            .Select(e => new { e.EntityId, e.Provider, e.ExternalId })
            .ToList()
            .GroupBy(e => e.EntityId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(e => e.Provider, e => e.ExternalId));
        var seriesIdsForIssues = issues.Select(i => i.SeriesId).Distinct().ToHashSet();
        var seriesExternal = context.ComicMetadataExternalIds.AsNoTracking()
            .Where(e => e.EntityKind == ComicMetadataEntityKind.Series && seriesIdsForIssues.Contains(e.EntityId))
            .Select(e => new { e.EntityId, e.Provider, e.ExternalId })
            .ToList()
            .GroupBy(e => e.EntityId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(e => e.Provider, e => e.ExternalId));

        string? Id(Dictionary<ComicProvider, string>? byProvider, ComicProvider provider) => byProvider?.GetValueOrDefault(provider);
        return issues.ToDictionary(i => i.Id, i => new ReadingListIssueIdentity(
            i.Id, i.SeriesId, i.SeriesName, i.Number, i.Volume, i.Year, i.Format,
            Id(external.GetValueOrDefault(i.Id), ComicProvider.ComicVine),
            Id(seriesExternal.GetValueOrDefault(i.SeriesId), ComicProvider.ComicVine),
            Id(external.GetValueOrDefault(i.Id), ComicProvider.Metron),
            Id(seriesExternal.GetValueOrDefault(i.SeriesId), ComicProvider.Metron)));
    }

    public static IReadOnlyList<int> Changed(
        IReadOnlyDictionary<int, ReadingListIssueIdentity> before,
        IReadOnlyDictionary<int, ReadingListIssueIdentity> after) =>
        before.Keys.Union(after.Keys)
            .Where(id => !before.TryGetValue(id, out var oldValue)
                || !after.TryGetValue(id, out var newValue)
                || oldValue != newValue)
            .ToList();

    public static HealResult Recheck(Func<PaperbunkrDbContext> contextFactory, IReadOnlyCollection<int> issueIds)
    {
        if (issueIds.Count == 0)
        {
            return HealResult.Nothing;
        }

        try
        {
            using var context = contextFactory();
            return ReadingListHealer.RecheckFor(context, issueIds);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or SqliteException)
        {
            // The metadata operation has already committed. A busy or stale read must never turn
            // that successful edit into an apparent failure; the list can be rechecked later.
            return HealResult.Nothing;
        }
    }

    public static void Report(HealResult result)
    {
        if (Summary(result) is not null)
        {
            LibraryFolderScanner.ReadingListsHealed?.Invoke(result);
        }
    }

    public static string? Summary(HealResult result)
    {
        if (result.Relinked == 0 && result.NeedChoice == 0 && result.StillMissing == 0 && result.AlreadyInList.Count == 0)
        {
            return null;
        }

        return $"Reading lists: {result.Summary}";
    }
}
