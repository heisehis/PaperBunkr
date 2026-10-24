using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.App.Services;

/// <summary>How a manga series' chapters stand with their files.</summary>
public sealed record EmbeddedSyncSummary(int Total, int Owed, int Synced, int NeedAttention, int Conflicts, int Unsupported, int ReadOnlyOrMissing)
{
    public bool HasProblems => NeedAttention > 0 || Conflicts > 0;

    public string Describe()
    {
        var parts = new List<string>();
        if (Owed > 0) parts.Add($"{Owed} waiting to be written");
        if (Synced > 0) parts.Add($"{Synced} up to date");
        if (NeedAttention > 0) parts.Add($"{NeedAttention} need attention");
        if (Conflicts > 0) parts.Add($"{Conflicts} changed outside Paperbunkr");
        if (Unsupported > 0) parts.Add($"{Unsupported} in a format that can't be written");
        if (ReadOnlyOrMissing > 0) parts.Add($"{ReadOnlyOrMissing} read-only or missing");
        return parts.Count == 0 ? "No chapters have been written to their files yet." : string.Join(" · ", parts) + ".";
    }
}

/// <summary>Chapters whose file does not hold what the library expects, found by <see cref="EmbeddedSyncActions.VerifyAsync"/>.</summary>
public sealed record EmbeddedVerifyResult(int Checked, int Matching, IReadOnlyList<string> Mismatches, int Unreadable)
{
    public string Describe() => Mismatches.Count == 0 && Unreadable == 0
        ? $"All {Matching} checked files hold the expected metadata."
        : $"{Matching} of {Checked} match. {Mismatches.Count} differ ({string.Join("; ", Mismatches.Take(3))}{(Mismatches.Count > 3 ? "; …" : string.Empty)}). {Unreadable} could not be read.";
}

/// <summary>
/// The user-facing sync actions for a manga series (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §10): a status summary, Retry failed,
/// Write anyway (for files changed outside Paperbunkr) and Verify embedded metadata, which only reads. Also the startup catch-up.
/// </summary>
public sealed class EmbeddedSyncActions
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly MetadataWriteBackQueue _queue;

    public EmbeddedSyncActions(Func<PaperbunkrDbContext> contextFactory, MetadataWriteBackQueue queue)
    {
        _contextFactory = contextFactory;
        _queue = queue;
    }

    public EmbeddedSyncSummary GetSummary(int seriesId)
    {
        using var context = _contextFactory();
        var rows = context.Issues.AsNoTracking().Where(i => i.SeriesId == seriesId)
            .Select(i => new { i.EmbeddedDirty, i.EmbeddedSyncState })
            .ToList();
        return new EmbeddedSyncSummary(
            rows.Count,
            rows.Count(r => r.EmbeddedDirty && r.EmbeddedSyncState is EmbeddedSyncState.None or EmbeddedSyncState.RetryScheduled),
            rows.Count(r => r.EmbeddedSyncState == EmbeddedSyncState.Synced && !r.EmbeddedDirty),
            rows.Count(r => r.EmbeddedSyncState == EmbeddedSyncState.NeedsAttention || r.EmbeddedSyncState == EmbeddedSyncState.RetryScheduled && !r.EmbeddedDirty),
            rows.Count(r => r.EmbeddedSyncState == EmbeddedSyncState.ConflictExternalModification),
            rows.Count(r => r.EmbeddedSyncState == EmbeddedSyncState.UnsupportedFormat),
            rows.Count(r => r.EmbeddedSyncState is EmbeddedSyncState.SkippedReadOnly or EmbeddedSyncState.SkippedMissingFile));
    }

    /// <summary>Queues every chapter of the series that failed, needs attention, was read-only, or is still owed. Not forced: the settings and the changed-outside guard still apply.</summary>
    public int RetryFailed(int seriesId)
    {
        List<int> ids;
        using (var context = _contextFactory())
        {
            ids = context.Issues.AsNoTracking()
                .Where(i => i.SeriesId == seriesId && (i.EmbeddedDirty || i.EmbeddedSyncState == EmbeddedSyncState.NeedsAttention || i.EmbeddedSyncState == EmbeddedSyncState.RetryScheduled || i.EmbeddedSyncState == EmbeddedSyncState.SkippedReadOnly)
                    && i.EmbeddedSyncState != EmbeddedSyncState.ConflictExternalModification)
                .Select(i => i.Id)
                .ToList();
        }

        _queue.Enqueue(ids);
        return ids.Count;
    }

    /// <summary>Forces the write of every chapter whose file was changed outside Paperbunkr: the library's metadata replaces what is in the file.</summary>
    public int WriteAnyway(int seriesId)
    {
        List<int> ids;
        using (var context = _contextFactory())
        {
            ids = context.Issues.AsNoTracking()
                .Where(i => i.SeriesId == seriesId && i.EmbeddedSyncState == EmbeddedSyncState.ConflictExternalModification)
                .Select(i => i.Id)
                .ToList();
        }

        _queue.Enqueue(ids, manual: true);
        return ids.Count;
    }

    /// <summary>Reads each writable file's embedded ComicInfo and compares the fields chapters inherit (and the series name and summary) with what the library expects. Writes nothing.</summary>
    public Task<EmbeddedVerifyResult> VerifyAsync(int seriesId, CancellationToken ct = default) => Task.Run(() =>
    {
        using var context = _contextFactory();
        var issues = context.Issues.Include(i => i.Series).Include(i => i.Tags).Include(i => i.MetadataProposals).AsSplitQuery()
            .Where(i => i.SeriesId == seriesId && i.FilePath != null && !i.IsPlaceholder)
            .OrderBy(i => i.Id)
            .ToList();

        int checkedCount = 0, matching = 0, unreadable = 0;
        var mismatches = new List<string>();
        foreach (var issue in issues)
        {
            ct.ThrowIfCancellationRequested();
            string path = issue.FilePath!;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                continue;
            }

            var embedded = EmbeddedComicInfoReader.TryRead(path);
            if (embedded is null)
            {
                unreadable++;
                continue;
            }

            checkedCount++;
            var expected = new ComicInfo();
            IssueToComicInfoMapper.Apply(issue, expected);
            if (string.IsNullOrWhiteSpace(issue.Summary) && !string.IsNullOrWhiteSpace(issue.Series?.Summary) && MangaInheritedFields.IsMangaFamily(issue.Series!.ContentType))
            {
                expected.Summary = issue.Series.Summary!;
            }

            var differing = Differences(expected, embedded);
            if (differing.Count == 0)
            {
                matching++;
            }
            else
            {
                mismatches.Add($"{Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}: {string.Join(", ", differing)}");
            }
        }

        return new EmbeddedVerifyResult(checkedCount, matching, mismatches, unreadable);
    }, ct);

    private static List<string> Differences(ComicInfo expected, ComicInfo embedded)
    {
        var differing = new List<string>();
        void Check(string name, string? want, string? have)
        {
            if (!string.Equals((want ?? string.Empty).Trim(), (have ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            {
                differing.Add(name);
            }
        }

        Check("Series", expected.Series, embedded.Series);
        Check("Publisher", expected.Publisher, embedded.Publisher);
        Check("Imprint", expected.Imprint, embedded.Imprint);
        Check("Writer", expected.Writer, embedded.Writer);
        Check("Penciller", expected.Penciller, embedded.Penciller);
        Check("Inker", expected.Inker, embedded.Inker);
        Check("Colorist", expected.Colorist, embedded.Colorist);
        Check("Letterer", expected.Letterer, embedded.Letterer);
        Check("CoverArtist", expected.CoverArtist, embedded.CoverArtist);
        Check("Editor", expected.Editor, embedded.Editor);
        Check("Genre", expected.Genre, embedded.Genre);
        Check("Tags", expected.Tags, embedded.Tags);
        Check("AgeRating", expected.AgeRating, embedded.AgeRating);
        Check("Summary", expected.Summary, embedded.Summary);
        return differing;
    }

    /// <summary>
    /// Startup catch-up: removes leftover <c>.pbwrite-*.tmp</c> files from a write that was interrupted, for the chapters that still owe one, then queues them. Never touches a
    /// chapter nobody edited. Returns how many were queued.
    /// </summary>
    public int ResumeOwedWrites()
    {
        List<string> paths;
        using (var context = _contextFactory())
        {
            paths = context.Issues.AsNoTracking().Where(i => i.EmbeddedDirty && i.FilePath != null).Select(i => i.FilePath!).ToList();
        }

        CleanLeftoverTempFiles(paths);
        return _queue.EnqueueDirty();
    }

    /// <summary>
    /// Deletes leftover swap/scratch copies beside each given archive (folded into <see cref="Paperbunkr.Data.Library.SwapFileSweeper"/>,
    /// docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md): only when the original is intact, never a copy that is
    /// the only good one left.
    /// </summary>
    public static int CleanLeftoverTempFiles(IEnumerable<string> archivePaths) =>
        Paperbunkr.Data.Library.SwapFileSweeper.SweepBeside(archivePaths).Deleted;
}
