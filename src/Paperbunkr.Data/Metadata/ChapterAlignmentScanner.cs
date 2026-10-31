using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>One series in Library Health's "Chapter alignment" section.</summary>
public sealed record AlignmentRow(
    int SeriesId,
    string SeriesName,
    string Unit,
    int MissingCount,
    int ExtraCount,
    int ConflictCount,
    string Summary,
    string DismissalKey);

/// <summary>
/// Library-wide chapter alignment for Library Health (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 1).
/// <see cref="ReadCached"/> costs no network (it reads the cached aggregates), so opening the section is free; <see cref="RefreshAsync"/> is
/// the explicit "Check now" that fetches the missing or stale ones, one MangaDex request at a time.
/// </summary>
public static class ChapterAlignmentScanner
{
    /// <summary>A series is listed when it is missing chapters MangaDex has, or has numbers MangaDex lacks. A volume disagreement alone is advisory (MangaDex's volume numbers come from uploaders) and only adds to the summary.</summary>
    public static IReadOnlyList<AlignmentRow> ReadCached(PaperbunkrDbContext context)
    {
        var snapshots = context.ExternalMetadataSnapshots
            .Where(s => s.Provider == ExternalMetadataProvider.MangaDex && s.SchemaVersion == ChapterAlignmentResolver.SnapshotSchemaVersion)
            .ToList();
        if (snapshots.Count == 0)
        {
            return Array.Empty<AlignmentRow>();
        }

        var seriesIds = snapshots.Select(s => s.SeriesId).ToList();
        var linked = context.ExternalMediaIds
            .Where(e => e.Provider == ExternalMetadataProvider.MangaDex && seriesIds.Contains(e.SeriesId))
            .ToList()
            .GroupBy(e => e.SeriesId)
            .ToDictionary(g => g.Key, g => g.First().ExternalId);
        var names = context.Series.Where(s => seriesIds.Contains(s.Id)).ToDictionary(s => s.Id, s => s.Name);
        var issuesBySeries = context.Issues
            .Include(i => i.MetadataProposals)
            .Where(i => seriesIds.Contains(i.SeriesId))
            .ToList()
            .GroupBy(i => i.SeriesId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var contents = ChapterAlignmentResolver.LoadContents(context, issuesBySeries.Values.SelectMany(list => list.Select(i => i.Id)));
        var rows = new List<AlignmentRow>();
        foreach (var snapshot in snapshots)
        {
            // A snapshot whose link was since changed belongs to the old target; ignore it until a refresh replaces it.
            if (!names.TryGetValue(snapshot.SeriesId, out string? name)
                || !linked.TryGetValue(snapshot.SeriesId, out string? mangaId) || mangaId != snapshot.ExternalId
                || MangaDexAggregateClient.Parse(snapshot.Payload) is not { } aggregate)
            {
                continue;
            }

            var report = ChapterAlignmentResolver.Compare(issuesBySeries.GetValueOrDefault(snapshot.SeriesId) ?? new List<Issue>(), aggregate, contents);
            if (report.Missing.Count == 0 && report.ExtraLocally.Count == 0)
            {
                continue;
            }

            rows.Add(ToRow(snapshot.SeriesId, name, report));
        }

        return rows.OrderByDescending(r => r.MissingCount + r.ExtraCount).ThenBy(r => r.SeriesName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static AlignmentRow ToRow(int seriesId, string name, AlignmentReport report)
    {
        string unit = report.Unit == AlignmentUnit.Volumes ? "volume" : "chapter";
        var parts = new List<string>();
        if (report.Missing.Count > 0)
        {
            parts.Add($"{report.Missing.Count:N0} {unit}{(report.Missing.Count == 1 ? string.Empty : "s")} not in your library");
        }

        if (report.ExtraLocally.Count > 0)
        {
            parts.Add($"{report.ExtraLocally.Count:N0} not on MangaDex");
        }

        if (report.VolumeConflicts.Count > 0)
        {
            parts.Add($"{report.VolumeConflicts.Count:N0} in a different volume");
        }

        return new AlignmentRow(
            seriesId,
            name,
            unit,
            report.Missing.Count,
            report.ExtraLocally.Count,
            report.VolumeConflicts.Count,
            string.Join(" · ", parts),
            DismissalKeyFor(seriesId, report));
    }

    /// <summary>Series id + the missing and extra numbers, so a newly missing chapter brings a dismissed series back.</summary>
    public static string DismissalKeyFor(int seriesId, AlignmentReport report) =>
        $"{seriesId}:{string.Join(",", report.Missing.Select(m => m.Number))}|{string.Join(",", report.ExtraLocally)}";

    /// <summary>Series linked to MangaDex whose cached aggregate is missing, stale, or was fetched for a different link.</summary>
    public static IReadOnlyList<(int SeriesId, string MangaId)> NeedingRefresh(PaperbunkrDbContext context, DateTime nowUtc)
    {
        var links = context.ExternalMediaIds
            .Where(e => e.Provider == ExternalMetadataProvider.MangaDex)
            .Select(e => new { e.SeriesId, e.ExternalId })
            .ToList();
        var snapshots = context.ExternalMetadataSnapshots
            .Where(s => s.Provider == ExternalMetadataProvider.MangaDex && s.SchemaVersion == ChapterAlignmentResolver.SnapshotSchemaVersion)
            .Select(s => new { s.SeriesId, s.ExternalId, s.RetrievedAt })
            .ToList()
            .GroupBy(s => s.SeriesId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.RetrievedAt).First());

        return links
            .GroupBy(l => l.SeriesId)
            .Select(g => (SeriesId: g.Key, MangaId: g.First().ExternalId))
            .Where(l => !snapshots.TryGetValue(l.SeriesId, out var snap)
                || snap.ExternalId != l.MangaId
                || nowUtc - snap.RetrievedAt >= ChapterAlignmentResolver.CacheLifetime)
            .ToList();
    }

    /// <summary>
    /// Fetches the aggregate of every series <see cref="NeedingRefresh"/> lists, oldest link first, reporting progress. Stops early when MangaDex
    /// rate-limits or is unreachable (what was fetched is kept) and says so through the result. Returns the number of series fetched.
    /// </summary>
    public static async Task<AlignmentRefreshResult> RefreshAsync(
        Func<PaperbunkrDbContext> contextFactory,
        MangaDexAggregateClient client,
        DateTime nowUtc,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<(int SeriesId, string MangaId)> pending;
        using (var context = contextFactory())
        {
            pending = NeedingRefresh(context, nowUtc).ToList();
        }

        int done = 0;
        progress?.Report((0, pending.Count));
        foreach (var (seriesId, mangaId) in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var context = contextFactory();
            try
            {
                var language = ChapterAlignmentResolver.PreferredLanguage(context.Issues.Where(i => i.SeriesId == seriesId).ToList());
                await ChapterAlignmentResolver.GetAggregateAsync(context, seriesId, mangaId, language, client, nowUtc, cancellationToken).ConfigureAwait(false);
            }
            catch (MetadataProviderUnavailableException)
            {
                return new AlignmentRefreshResult(done, pending.Count, StoppedEarly: true);
            }

            progress?.Report((++done, pending.Count));
        }

        return new AlignmentRefreshResult(done, pending.Count, StoppedEarly: false);
    }
}

/// <summary>What a <see cref="ChapterAlignmentScanner.RefreshAsync"/> pass did.</summary>
public sealed record AlignmentRefreshResult(int Fetched, int Total, bool StoppedEarly);
