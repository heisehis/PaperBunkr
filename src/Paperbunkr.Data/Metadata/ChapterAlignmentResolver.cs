using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Metadata;

/// <summary>What a local issue number stands for: manga libraries hold either one file per chapter or one file per volume.</summary>
public enum AlignmentUnit
{
    Chapters,
    Volumes,
}

/// <summary>A chapter (or volume) MangaDex lists, with the volume it files it under.</summary>
public sealed record AlignmentEntry(string Number, string? Volume);

/// <summary>A local issue whose volume disagrees with MangaDex's for the same chapter. Advisory: MangaDex volume numbers come from scanlation uploads, not the publisher.</summary>
public sealed record AlignmentVolumeConflict(string Number, string? LocalVolume, string? RemoteVolume, int IssueId);

/// <summary>
/// Chapter/volume alignment for one series (onboarding.md section 9; docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 1).
/// Read-only: it only reports, it never edits an issue's Number or Volume.
/// </summary>
public sealed record AlignmentReport(
    AlignmentUnit Unit,
    int RemoteCount,
    int LocalCount,
    IReadOnlyList<AlignmentEntry> Missing,
    IReadOnlyList<string> ExtraLocally,
    IReadOnlyList<AlignmentVolumeConflict> VolumeConflicts)
{
    public int MatchedCount => RemoteCount - Missing.Count;

    public int MismatchCount => Missing.Count + ExtraLocally.Count + VolumeConflicts.Count;

    public bool HasMismatches => MismatchCount > 0;

    public static AlignmentReport Empty { get; } = new(AlignmentUnit.Chapters, 0, 0, Array.Empty<AlignmentEntry>(), Array.Empty<string>(), Array.Empty<AlignmentVolumeConflict>());
}

public static class ChapterAlignmentResolver
{
    /// <summary>Stored in <see cref="ExternalMetadataSnapshot.SchemaVersion"/> so these rows are never mistaken for the normal metadata snapshots.</summary>
    public const string SnapshotSchemaVersion = "aggregate-1";

    /// <summary>How long a cached aggregate is reused before the next report refetches it.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    /// <summary>The MangaDex id a series is linked to, or null when it isn't linked to MangaDex.</summary>
    public static string? FindMangaDexId(PaperbunkrDbContext context, int seriesId) =>
        context.ExternalMediaIds
            .Where(e => e.SeriesId == seriesId && e.Provider == ExternalMetadataProvider.MangaDex)
            .Select(e => e.ExternalId)
            .FirstOrDefault();

    /// <summary>The chapter ranges recorded for these issues (Phase 7), keyed by issue id; issues with none are absent.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<CollectionContent>> LoadContents(PaperbunkrDbContext context, IEnumerable<int> issueIds)
    {
        var ids = issueIds.ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<CollectionContent>>();
        }

        return context.CollectionContents
            .Where(c => ids.Contains(c.IssueId))
            .OrderBy(c => c.FromKey)
            .ToList()
            .GroupBy(c => c.IssueId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CollectionContent>)g.ToList());
    }

    /// <summary>The language the series' files are in (most common <see cref="Issue.LanguageISO"/>), "en" when none is set.</summary>
    public static string PreferredLanguage(IEnumerable<Issue> issues) =>
        issues.Select(i => i.LanguageISO?.Trim().ToLowerInvariant())
            .Where(l => !string.IsNullOrEmpty(l))
            .GroupBy(l => l!)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "en";

    /// <summary>
    /// The pure comparison. Chapters are matched on their number (<c>10.5</c> is ten and a half, so "Ch. 12" style labels the issue
    /// editor normalizes still match). When no local number matches a chapter but every local number is a MangaDex volume, the files
    /// are volumes and the comparison runs against MangaDex's volume list instead.
    /// A file with <see cref="CollectionContent"/> rows (Phase 7) is a collected edition: it owns the chapters in those ranges, not the chapter
    /// its own number happens to equal, so it is compared as chapters and never reported as a number MangaDex lacks.
    /// </summary>
    public static AlignmentReport Compare(
        IEnumerable<Issue> localIssues,
        MangaDexAggregate remote,
        IReadOnlyDictionary<int, IReadOnlyList<CollectionContent>>? contents = null)
    {
        var all = localIssues.ToList();
        var mappedIssues = all.Where(i => contents is not null && contents.TryGetValue(i.Id, out var rows) && rows.Count > 0).ToList();
        var locals = all
            .Except(mappedIssues)
            .Select(issue => (Issue: issue, Key: issue.NumberSortKey()))
            .Where(x => x.Key is not null)
            .ToList();

        var chapterNumbers = new Dictionary<float, AlignmentEntry>();
        foreach (var chapter in remote.Chapters)
        {
            if (MangaDexAggregateClient.TryParseNumber(chapter.Chapter, out float number) && !chapterNumbers.ContainsKey(number))
            {
                chapterNumbers[number] = new AlignmentEntry(chapter.Chapter, chapter.Volume);
            }
        }

        var volumeNumbers = new Dictionary<float, AlignmentEntry>();
        foreach (string volume in remote.Volumes)
        {
            if (MangaDexAggregateClient.TryParseNumber(volume, out float number) && !volumeNumbers.ContainsKey(number))
            {
                volumeNumbers[number] = new AlignmentEntry(volume, volume);
            }
        }

        bool anyChapterMatch = locals.Any(x => chapterNumbers.ContainsKey(x.Key!.Value));
        bool volumesOnly = !anyChapterMatch
            && mappedIssues.Count == 0
            && locals.Count > 0
            && volumeNumbers.Count > 0
            && locals.All(x => volumeNumbers.ContainsKey(x.Key!.Value));

        var unit = volumesOnly ? AlignmentUnit.Volumes : AlignmentUnit.Chapters;
        var remoteMap = volumesOnly ? volumeNumbers : chapterNumbers;
        var localKeys = new HashSet<float>(locals.Select(x => x.Key!.Value));
        foreach (var issue in mappedIssues)
        {
            foreach (var range in contents![issue.Id])
            {
                localKeys.Add(range.FromKey);
                localKeys.Add(range.ToKey);
                foreach (float chapter in chapterNumbers.Keys.Where(c => c >= range.FromKey && c <= range.ToKey))
                {
                    localKeys.Add(chapter);
                }
            }
        }

        var missing = remoteMap
            .Where(pair => !localKeys.Contains(pair.Key))
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .ToList();

        var extra = locals
            .Where(x => !remoteMap.ContainsKey(x.Key!.Value))
            .Select(x => x.Issue.EffectiveNumber()!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => MangaDexAggregateClient.TryParseNumber(n, out float f) ? f : float.MaxValue)
            .ToList();

        var conflicts = new List<AlignmentVolumeConflict>();
        if (!volumesOnly)
        {
            foreach (var (issue, key) in locals)
            {
                if (!chapterNumbers.TryGetValue(key!.Value, out var entry))
                {
                    continue;
                }

                string? localVolume = issue.EffectiveVolume();
                if (string.IsNullOrWhiteSpace(localVolume) || entry.Volume is null || SameVolume(localVolume, entry.Volume))
                {
                    continue;
                }

                conflicts.Add(new AlignmentVolumeConflict(entry.Number, localVolume, entry.Volume, issue.Id));
            }
        }

        return new AlignmentReport(unit, remoteMap.Count, locals.Count + mappedIssues.Count, missing, extra, conflicts);
    }

    /// <summary>
    /// Builds the report for one series: reads the cached aggregate when it is fresh, otherwise fetches (and caches) a new one.
    /// Returns null when the series isn't linked to MangaDex. Throws <see cref="MetadataProviderUnavailableException"/> when
    /// MangaDex can't be reached and no cached copy exists; a stale cached copy is used in that case instead.
    /// </summary>
    public static async Task<AlignmentReport?> BuildAsync(
        PaperbunkrDbContext context,
        int seriesId,
        MangaDexAggregateClient client,
        DateTime nowUtc,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        string? mangaId = FindMangaDexId(context, seriesId);
        if (mangaId is null)
        {
            return null;
        }

        var issues = await context.Issues
            .Include(i => i.MetadataProposals)
            .Where(i => i.SeriesId == seriesId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var aggregate = await GetAggregateAsync(context, seriesId, mangaId, PreferredLanguage(issues), client, nowUtc, cancellationToken, forceRefresh).ConfigureAwait(false);
        return Compare(issues, aggregate, LoadContents(context, issues.Select(i => i.Id)));
    }

    /// <summary>
    /// The report from the cached aggregate alone (any age, no network), or null when the series isn't linked to MangaDex or has no cached
    /// list yet. This is what a screen shows on open; <see cref="BuildAsync"/> is the path that fetches.
    /// </summary>
    public static AlignmentReport? ReadCached(PaperbunkrDbContext context, int seriesId)
    {
        string? mangaId = FindMangaDexId(context, seriesId);
        if (mangaId is null)
        {
            return null;
        }

        var snapshot = context.ExternalMetadataSnapshots
            .Where(s => s.SeriesId == seriesId
                && s.Provider == ExternalMetadataProvider.MangaDex
                && s.SchemaVersion == SnapshotSchemaVersion
                && s.ExternalId == mangaId)
            .OrderByDescending(s => s.RetrievedAt)
            .FirstOrDefault();
        if (snapshot is null || MangaDexAggregateClient.Parse(snapshot.Payload) is not { } aggregate)
        {
            return null;
        }

        var issues = context.Issues.Include(i => i.MetadataProposals).Where(i => i.SeriesId == seriesId).ToList();
        return Compare(issues, aggregate, LoadContents(context, issues.Select(i => i.Id)));
    }

    /// <summary>The cached aggregate if it is younger than <see cref="CacheLifetime"/>, otherwise a fresh fetch written back to the cache.</summary>
    public static async Task<MangaDexAggregate> GetAggregateAsync(
        PaperbunkrDbContext context,
        int seriesId,
        string mangaId,
        string language,
        MangaDexAggregateClient client,
        DateTime nowUtc,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        var snapshot = await context.ExternalMetadataSnapshots
            .Where(s => s.SeriesId == seriesId
                && s.Provider == ExternalMetadataProvider.MangaDex
                && s.SchemaVersion == SnapshotSchemaVersion)
            .OrderByDescending(s => s.RetrievedAt)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!forceRefresh && snapshot is not null && snapshot.ExternalId == mangaId && nowUtc - snapshot.RetrievedAt < CacheLifetime
            && MangaDexAggregateClient.Parse(snapshot.Payload) is { } cached)
        {
            return cached;
        }

        MangaDexAggregate fresh;
        string raw;
        try
        {
            (fresh, raw) = await FetchWithPayloadAsync(client, mangaId, language, cancellationToken).ConfigureAwait(false);
        }
        catch (MetadataProviderUnavailableException) when (snapshot is not null && snapshot.ExternalId == mangaId
            && MangaDexAggregateClient.Parse(snapshot.Payload) is not null)
        {
            // Offline or rate limited: an old answer beats none.
            return MangaDexAggregateClient.Parse(snapshot.Payload)!;
        }

        if (snapshot is null)
        {
            snapshot = new ExternalMetadataSnapshot
            {
                SeriesId = seriesId,
                Provider = ExternalMetadataProvider.MangaDex,
                SchemaVersion = SnapshotSchemaVersion,
            };
            context.ExternalMetadataSnapshots.Add(snapshot);
        }

        snapshot.ExternalId = mangaId;
        snapshot.RetrievedAt = nowUtc;
        snapshot.Payload = raw;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return fresh;
    }

    private static async Task<(MangaDexAggregate Aggregate, string Raw)> FetchWithPayloadAsync(
        MangaDexAggregateClient client, string mangaId, string language, CancellationToken cancellationToken)
    {
        var aggregate = await client.FetchAsync(mangaId, language, cancellationToken).ConfigureAwait(false);
        // Re-serialized in the compact shape Parse reads back, so the cache never depends on MangaDex's full response.
        return (aggregate, Serialize(aggregate));
    }

    internal static string Serialize(MangaDexAggregate aggregate)
    {
        var volumes = new Dictionary<string, Dictionary<string, object>>();
        foreach (var group in aggregate.Chapters.GroupBy(c => c.Volume ?? "none"))
        {
            volumes[group.Key] = new Dictionary<string, object>
            {
                ["volume"] = group.Key,
                ["chapters"] = group.GroupBy(c => c.Chapter).ToDictionary(
                    g => g.Key,
                    g => (object)new Dictionary<string, object> { ["chapter"] = g.Key }),
            };
        }

        return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["result"] = "ok", ["volumes"] = volumes });
    }

    private static bool SameVolume(string local, string remote)
    {
        if (MangaDexAggregateClient.TryParseNumber(local, out float a) && MangaDexAggregateClient.TryParseNumber(remote, out float b))
        {
            return a == b;
        }

        return string.Equals(local.Trim(), remote.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
