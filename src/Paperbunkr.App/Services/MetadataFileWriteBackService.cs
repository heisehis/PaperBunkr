using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Services;

public enum MetadataWriteBackResult
{
    Success,

    /// <summary>Fileless entry, no <see cref="Data.Entities.Issue.FilePath"/>, or the file is gone from disk.</summary>
    SkippedMissingFile,

    /// <summary>A format with no free writer - CBR/RAR, PDF, DjVu, EPUB. A deliberate, visible skip.</summary>
    SkippedUnsupportedFormat,

    /// <summary>The file exists but is marked read-only.</summary>
    SkippedReadOnly,

    Failed,
}

public readonly record struct MetadataWriteBackOutcome(MetadataWriteBackResult Result, string? FileName, string? ErrorMessage)
{
    public bool IsSkip => Result is MetadataWriteBackResult.SkippedMissingFile
        or MetadataWriteBackResult.SkippedUnsupportedFormat
        or MetadataWriteBackResult.SkippedReadOnly;
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

    public Task<MetadataWriteBackOutcome> WriteAsync(int issueId, bool includeSidecar, CancellationToken ct = default)
    {
        return Task.Run(() => Write(issueId, includeSidecar, ct), ct);
    }

    private MetadataWriteBackOutcome Write(int issueId, bool includeSidecar, CancellationToken ct)
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

        bool isFolder = Directory.Exists(path);
        if (!isFolder && !File.Exists(path))
        {
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.SkippedMissingFile, fileName, null);
        }

        bool isCbz = !isFolder && Path.GetExtension(path).Equals(".cbz", StringComparison.OrdinalIgnoreCase);
        if (!isFolder && !isCbz)
        {
            // .cb7/.cbt need a bundled 7z.exe we don't ship; .cbr/.pdf/.djvu have no free writer.
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.SkippedUnsupportedFormat, fileName, null);
        }

        if (!isFolder && new FileInfo(path).IsReadOnly)
        {
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.SkippedReadOnly, fileName, null);
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

            if (isFolder)
            {
                foreach (var entry in entries)
                {
                    string entryPath = Path.Combine(path, entry.Key);
                    FileWriteBackCoordinator.Suppress(entryPath, TimeSpan.FromSeconds(15));
                    File.WriteAllBytes(entryPath, entry.Value);
                }

                return new MetadataWriteBackOutcome(MetadataWriteBackResult.Success, fileName, null);
            }

            UpdateZipEntries(path, entries);
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.Success, fileName, null);
        }
        catch (Exception ex)
        {
            return new MetadataWriteBackOutcome(MetadataWriteBackResult.Failed, fileName, ex.Message);
        }
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

            File.Replace(tempPath, path, destinationBackupFileName: null);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
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
        catch (IOException)
        {
        }
    }
}
