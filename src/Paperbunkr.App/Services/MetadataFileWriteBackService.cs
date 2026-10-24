using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
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

public enum MetadataWriteBackResult
{
    Success,

    /// <summary>ComicInfo.xml (and the sidecar) were written, but building <c>MetronInfo.xml</c> failed - the message says why.</summary>
    SuccessWithoutMetronInfo,

    /// <summary>Fileless entry, no <see cref="Data.Entities.Issue.FilePath"/>, or the file is gone from disk.</summary>
    SkippedMissingFile,

    /// <summary>A format with no free writer - CBR/RAR, PDF, DjVu, EPUB. A deliberate, visible skip.</summary>
    SkippedUnsupportedFormat,

    /// <summary>The file exists but is marked read-only.</summary>
    SkippedReadOnly,

    Failed,

    /// <summary>Manga chapter whose file already holds exactly what would be written (same export fingerprint, file untouched since): nothing to do.</summary>
    SkippedAlreadyCurrent,

    /// <summary>Manga chapter whose file changed outside Paperbunkr since our last write. Left alone until the user chooses Write anyway.</summary>
    SkippedExternalModification,

    /// <summary>The chapter's series has embedded sync switched off.</summary>
    SkippedSyncDisabled,
}

public readonly record struct MetadataWriteBackOutcome(MetadataWriteBackResult Result, string? FileName, string? ErrorMessage)
{
    public bool IsSkip => Result is MetadataWriteBackResult.SkippedMissingFile
        or MetadataWriteBackResult.SkippedUnsupportedFormat
        or MetadataWriteBackResult.SkippedReadOnly
        or MetadataWriteBackResult.SkippedAlreadyCurrent
        or MetadataWriteBackResult.SkippedExternalModification
        or MetadataWriteBackResult.SkippedSyncDisabled;
}

/// <summary>
/// Writes an <see cref="Data.Entities.Issue"/>'s current metadata back into its comic file's
/// embedded <c>ComicInfo.xml</c> (and, optionally, a <c>paperbunkr.json</c> sidecar) -
/// docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md. Replaces the narrow,
/// Genre/Tags-only <c>ComicInfoWriteBackService</c>.
///
/// Loads the file's <i>current</i> embedded <c>ComicInfo.xml</c> via
/// <see cref="EmbeddedComicInfoReader"/>, overlays every Paperbunkr-modeled field via
/// <see cref="IssueToComicInfoMapper"/> (so unmodeled elements - exotic fields, the
/// <c>&lt;Pages&gt;</c> list - survive), then replaces just those one or two entries in the archive.
/// Pages are never re-encoded. The update goes to a sibling temp copy that is atomically swapped in,
/// so a crash mid-write leaves the original intact.
///
/// <b>Format support (v1):</b> <c>.cbz</c> (via <see cref="ZipArchive"/> update mode) and an image
/// folder (files written directly into it). <c>.cb7</c>/<c>.cbt</c> are a visible skip - the ported
/// engine's <c>7z u</c> path needs a bundled <c>7z.exe</c> Paperbunkr doesn't ship (only
/// <c>7z.dll</c>, for reading), so writing those is deferred rather than faked.
///
/// <b>Manga chapters</b> (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §10) additionally keep their sync state on the
/// <see cref="Issue"/> row: an export fingerprint so an unchanged file is never rewritten, a file stamp so an outside edit is noticed and not
/// overwritten, and a state the UI can show. Chapters of any other series are written exactly as before.
///
/// v1 does not write per-page type/rotation - those live in the sidecar's scope but are deferred
/// (see <see cref="PaperbunkrSidecar"/>). Never throws: every failure is an
/// <see cref="MetadataWriteBackOutcome"/>.
/// </summary>
public class MetadataFileWriteBackService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public MetadataFileWriteBackService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    internal MetadataFileWriteBackService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public Task<MetadataWriteBackOutcome> WriteAsync(int issueId, bool includeSidecar, CancellationToken ct = default) =>
        WriteAsync(issueId, includeSidecar, includeMetronInfo: false, ct);

    /// <param name="includeMetronInfo">Also write a <c>MetronInfo.xml</c> (docs/superpowers/specs/2026-10-05-
    /// metroninfo-write-back-design.md). When false, one the file already has is left exactly as it is.</param>
    public Task<MetadataWriteBackOutcome> WriteAsync(int issueId, bool includeSidecar, bool includeMetronInfo, CancellationToken ct = default) =>
        WriteAsync(issueId, includeSidecar, includeMetronInfo, force: false, ct);

    /// <param name="force">The user asked for this write ("Write metadata to files", "Write anyway"): skip the manga-only checks that would otherwise leave the file alone
    /// (already current, edited outside Paperbunkr, series sync switched off).</param>
    public Task<MetadataWriteBackOutcome> WriteAsync(int issueId, bool includeSidecar, bool includeMetronInfo, bool force, CancellationToken ct = default)
    {
        return Task.Run(() => Write(issueId, includeSidecar, includeMetronInfo, force, ct), ct);
    }

    /// <summary>What the file should hold for <paramref name="issue"/>: the export fingerprint two writes are compared by. Needs <see cref="Issue.Series"/>, <see cref="Issue.Tags"/> and <see cref="Issue.MetadataProposals"/> loaded.</summary>
    public static string ComputeFingerprint(Issue issue, bool includeSidecar, bool includeMetronInfo)
    {
        var info = new ComicInfo();
        IssueToComicInfoMapper.Apply(issue, info);
        ApplyMangaFallbacks(issue, info);
        if (issue.PageCount is int pageCount && pageCount > 0)
        {
            info.PageCount = pageCount;
        }

        using var hash = SHA256.Create();
        byte[] infoBytes = info.ToArray();
        hash.TransformBlock(infoBytes, 0, infoBytes.Length, null, 0);
        if (includeSidecar)
        {
            byte[] sidecar = PaperbunkrSidecar.FromIssue(issue).ToJsonBytes();
            hash.TransformBlock(sidecar, 0, sidecar.Length, null, 0);
        }

        byte[] flags = { (byte)(includeSidecar ? 1 : 0), (byte)(includeMetronInfo ? 1 : 0) };
        hash.TransformFinalBlock(flags, 0, flags.Length);
        return Convert.ToHexString(hash.Hash!);
    }

    /// <summary>A manga chapter with no synopsis of its own exports the series summary instead (never copied into the chapter itself).</summary>
    private static void ApplyMangaFallbacks(Issue issue, ComicInfo info)
    {
        if (IsMangaChapter(issue) && string.IsNullOrWhiteSpace(issue.Summary) && !string.IsNullOrWhiteSpace(issue.Series!.Summary))
        {
            info.Summary = issue.Series.Summary!;
        }
    }

    private static bool IsMangaChapter(Issue issue) => issue.Series is not null && MangaInheritedFields.IsMangaFamily(issue.Series.ContentType);

    /// <summary>"size:modifiedTicks" - cheap, and different whenever something else rewrites the file.</summary>
    public static string? FileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? $"{info.Length}:{info.LastWriteTimeUtc.Ticks}" : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Records a manga chapter's sync state. A chapter of any other series is left untouched.</summary>
    private static void Record(PaperbunkrDbContext context, Issue issue, EmbeddedSyncState state, string? message = null, bool? dirty = null)
    {
        if (!IsMangaChapter(issue))
        {
            return;
        }

        issue.EmbeddedSyncState = state;
        issue.EmbeddedSyncMessage = message;
        if (dirty is bool value)
        {
            issue.EmbeddedDirty = value;
        }

        try
        {
            context.SaveChanges();
        }
        catch (Exception)
        {
            // Recording a state must never turn a finished write (or a skip) into an error.
        }
    }

    private MetadataWriteBackOutcome Write(int issueId, bool includeSidecar, bool includeMetronInfo, bool force, CancellationToken ct)
    {
        using var context = _contextFactory();
        var issue = context.Issues
            .Include(i => i.Series)
            .Include(i => i.Tags)
            .Include(i => i.MetadataProposals)
            .AsSplitQuery()
            .FirstOrDefault(i => i.Id == issueId);

        if (issue is null || issue.IsPlaceholder || string.IsNullOrWhiteSpace(issue.FilePath))
        {
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.SkippedMissingFile, null, null);
        }

        string path = issue.FilePath!;
        string fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        bool manga = IsMangaChapter(issue);

        if (manga && !force && !issue.Series!.EmbeddedSyncEnabled)
        {
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.SkippedSyncDisabled, fileName, null);
        }

        bool isFolder = Directory.Exists(path);
        if (!isFolder && !File.Exists(path))
        {
            Record(context, issue, EmbeddedSyncState.SkippedMissingFile, "The file is not on disk.", dirty: false);
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.SkippedMissingFile, fileName, null);
        }

        bool isCbz = !isFolder && Path.GetExtension(path).Equals(".cbz", StringComparison.OrdinalIgnoreCase);
        if (!isFolder && !isCbz)
        {
            // .cb7/.cbt need a bundled 7z.exe we don't ship; .cbr/.pdf/.djvu have no free writer.
            Record(context, issue, EmbeddedSyncState.UnsupportedFormat, "This format cannot be written to; its metadata is kept in the library only.", dirty: false);
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.SkippedUnsupportedFormat, fileName, null);
        }

        if (!isFolder && new FileInfo(path).IsReadOnly)
        {
            // Dirty stays set: the user may make the file writable and it is tried again next launch.
            Record(context, issue, EmbeddedSyncState.SkippedReadOnly, "The file is read-only.");
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.SkippedReadOnly, fileName, null);
        }

        if (manga && !force)
        {
            string? stampNow = isFolder ? null : FileStamp(path);
            if (!isFolder && issue.EmbeddedFileStamp is not null && stampNow != issue.EmbeddedFileStamp)
            {
                Record(context, issue, EmbeddedSyncState.ConflictExternalModification, "The file was changed outside Paperbunkr after it was last written. Choose Write anyway to replace it.");
                return new MetadataWriteBackOutcome(MetadataWriteBackResult.SkippedExternalModification, fileName, null);
            }

            if (issue.EmbeddedFingerprint is not null && issue.EmbeddedFingerprint == ComputeFingerprint(issue, includeSidecar, includeMetronInfo) && (isFolder || stampNow == issue.EmbeddedFileStamp))
            {
                Record(context, issue, EmbeddedSyncState.Synced, dirty: false);
                return new MetadataWriteBackOutcome(MetadataWriteBackResult.SkippedAlreadyCurrent, fileName, null);
            }
        }

        ct.ThrowIfCancellationRequested();

        // Tell the live-folder watcher to ignore the file churn we're about to cause (copy to a
        // .pbwrite-*.tmp sibling, then File.Replace it back) - otherwise it reads the replace as a
        // Deleted event and flags the issue FileIsMissing. Window covers the whole write plus the
        // watcher's 2s debounce with margin.
        FileWriteBackCoordinator.Suppress(path, TimeSpan.FromSeconds(15));

        try
        {
            var info = EmbeddedComicInfoReader.TryRead(path) ?? new ComicInfo();
            IssueToComicInfoMapper.Apply(issue, info);
            ApplyMangaFallbacks(issue, info);

            // PageCount isn't a metadata-editor field (IssueToComicInfoMapper leaves it alone), but
            // writing a fresh ComicInfo.xml that omits it would read back as 0. Carry the DB's known
            // count through so the written file stays correct.
            if (issue.PageCount is int pageCount && pageCount > 0)
            {
                info.PageCount = pageCount;
            }

            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["ComicInfo.xml"] = info.ToArray(),
            };
            if (includeSidecar)
            {
                entries["paperbunkr.json"] = PaperbunkrSidecar.FromIssue(issue).ToJsonBytes();
            }

            // A MetronInfo.xml that can't be built must not cost the user the ComicInfo.xml write.
            string? metronInfoError = null;
            if (includeMetronInfo)
            {
                try
                {
                    var metron = ReadExistingMetronInfo(path, isFolder) ?? new MetronInfo();
                    IssueToMetronInfoMapper.Apply(issue, metron, MetronIdContext.FromDatabase(context, issue));
                    entries["MetronInfo.xml"] = metron.ToArray();
                }
                catch (Exception ex)
                {
                    metronInfoError = ex.Message;
                }
            }

            var written = metronInfoError is null
                ? new MetadataWriteBackOutcome(MetadataWriteBackResult.Success, fileName, null)
                : new MetadataWriteBackOutcome(MetadataWriteBackResult.SuccessWithoutMetronInfo, fileName, metronInfoError);

            if (isFolder)
            {
                foreach (var entry in entries)
                {
                    string entryPath = Path.Combine(path, entry.Key);
                    FileWriteBackCoordinator.Suppress(entryPath, TimeSpan.FromSeconds(15));
                    File.WriteAllBytes(entryPath, entry.Value);
                }

                if (manga)
                {
                    issue.EmbeddedFingerprint = ComputeFingerprint(issue, includeSidecar, includeMetronInfo);
                    issue.EmbeddedFileStamp = null;
                    issue.EmbeddedSyncState = EmbeddedSyncState.Synced;
                    issue.EmbeddedSyncMessage = null;
                    issue.EmbeddedDirty = false;
                    context.SaveChanges();
                }

                return written;
            }

            using (FileWriteBackCoordinator.BeginWrite(path))
            {
                UpdateZipEntries(path, entries);
            }

            // The rewrite changed the file's size and modified time - keep the DB's copy current
            // (only these columns are dirty, so this can't clobber a concurrent metadata edit).
            bool statsChanged = Paperbunkr.Data.Library.IssueFileStats.TryApply(issue);
            if (manga)
            {
                issue.EmbeddedFingerprint = ComputeFingerprint(issue, includeSidecar, includeMetronInfo);
                issue.EmbeddedFileStamp = FileStamp(path);
                issue.EmbeddedSyncState = EmbeddedSyncState.Synced;
                issue.EmbeddedSyncMessage = metronInfoError;
                issue.EmbeddedDirty = false;
            }

            if (statsChanged || manga)
            {
                context.SaveChanges();
            }

            return written;
        }
        catch (Exception ex)
        {
            Record(context, issue, EmbeddedSyncState.RetryScheduled, ex.Message);
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.Failed, fileName, ex.Message);
        }
    }

    /// <summary>The queue gave up on a chapter after repeated failures: shown as needing attention and retried on the next launch (it stays dirty).</summary>
    public void MarkNeedsAttention(int issueId, string? message)
    {
        using var context = _contextFactory();
        var issue = context.Issues.Include(i => i.Series).FirstOrDefault(i => i.Id == issueId);
        if (issue is not null)
        {
            Record(context, issue, EmbeddedSyncState.NeedsAttention, message);
        }
    }

    /// <summary>
    /// The file's current <c>MetronInfo.xml</c>, so what Paperbunkr holds nothing for (prices, universes,
    /// reprints, another tool's ids) survives the rewrite. Null when there is none or it doesn't parse.
    /// </summary>
    private static MetronInfo? ReadExistingMetronInfo(string path, bool isFolder)
    {
        if (isFolder)
        {
            string entryPath = Path.Combine(path, "MetronInfo.xml");
            if (!File.Exists(entryPath))
            {
                return null;
            }

            using var file = File.OpenRead(entryPath);
            return MetronInfo.TryRead(file);
        }

        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, "MetronInfo.xml", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        return MetronInfo.TryRead(stream);
    }

    /// <summary>
    /// Add-or-replace the named entries in a zip (CBZ) without re-encoding any other entry. Works on
    /// a sibling temp copy, then atomically swaps it in - a crash mid-write leaves the original file
    /// untouched.
    /// </summary>
    private static void UpdateZipEntries(string path, IReadOnlyDictionary<string, byte[]> entries)
    {
        string tempPath = path + ".pbwrite-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(path, tempPath, overwrite: true);

            using (var zip = ZipFile.Open(tempPath, ZipArchiveMode.Update))
            {
                foreach (string name in entries.Keys)
                {
                    // Remove any existing entry with this name (case-insensitive - a file might carry
                    // "comicinfo.xml") before writing the canonical-cased replacement.
                    foreach (var existing in zip.Entries
                        .Where(e => string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase))
                        .ToList())
                    {
                        existing.Delete();
                    }

                    var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using var stream = entry.Open();
                    stream.Write(entries[name], 0, entries[name].Length);
                }
            }

            // The organizer may have moved or renamed the comic while this copy was being written: replacing would fail anyway,
            // and saying so plainly beats a FileNotFoundException from deep inside ReplaceFile.
            if (!File.Exists(path))
            {
                throw new IOException($"'{Path.GetFileName(path)}' was moved or removed while its metadata was being written.");
            }

            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        finally
        {
            // After a successful Replace the scratch no longer exists; after any failure it must not be left beside the comic
            // (docs/superpowers/specs/2026-10-21-scraper-organizer-a-reliability-design.md, "Write-back hygiene").
            TryDelete(tempPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked or not ours to delete: SwapFileSweeper removes it on a later pass.
        }
    }
}
