using Microsoft.EntityFrameworkCore;
using Paperbunkr.Daemon.Clients;
using Paperbunkr.Daemon.Events;
using Paperbunkr.Daemon.Indexers;
using Paperbunkr.Daemon.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Daemon.Import;

/// <summary>
/// Imports a finished download (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md §6). The download's files are never modified:
/// each comic is <b>copied</b> to a temp folder, repacked to .cbz if needed and tagged with a <c>ComicInfo.xml</c> when it has no usable one, named from
/// the user's template, moved into the destination library folder, handed to the library scanner, and any reading-list placeholder for it is relinked.
/// A multi-file download (a pack) is inspected and each file matched to its own wanted issue - by file name, then by the archive's own ComicInfo -
/// never mapped wholesale onto one issue.
/// </summary>
public sealed class ImportProcessor(
    Func<PaperbunkrDbContext> createContext,
    ILibraryIngester ingester,
    IEventPublisher events,
    Func<DateTime>? now = null,
    string? tempRoot = null) : IImportProcessor
{
    private static readonly WantedIssueStatus[] Open =
        { WantedIssueStatus.Wanted, WantedIssueStatus.Snatched, WantedIssueStatus.Downloading, WantedIssueStatus.Failed };

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly string _tempRoot = tempRoot ?? Path.Combine(Path.GetTempPath(), "paperbunkr-import");

    public async Task<ImportOutcome> ImportAsync(int wantedIssueId, DownloadStatus download, IReadOnlyList<DownloadFile> files, CancellationToken cancellationToken)
    {
        AcquisitionSettings settings;
        WantedIssue primary;
        List<WantedIssue> openRows;
        using (var context = createContext())
        {
            settings = context.GetOrCreateAcquisitionSettings();
            primary = context.WantedIssues.Include(w => w.WatchedSeries).First(w => w.Id == wantedIssueId);
            openRows = context.WantedIssues.Include(w => w.WatchedSeries)
                .Where(w => w.WatchedSeriesId == primary.WatchedSeriesId && Open.Contains(w.Status)).ToList();
        }

        // Problems that are the user's to fix, not the release's: keep the download and try again later; never fail or blocklist.
        if (string.IsNullOrWhiteSpace(settings.DestinationFolderPath) || !Directory.Exists(settings.DestinationFolderPath))
        {
            return ImportOutcome.Deferred("Choose a destination library folder in Preferences → Acquisition so finished downloads can be imported.");
        }

        var templateError = ImportNaming.Validate(settings.RenameTemplate);
        if (templateError is not null)
        {
            return ImportOutcome.Deferred($"The naming template in Preferences → Acquisition is invalid: {templateError}");
        }

        if (string.IsNullOrEmpty(download.SavePath))
        {
            return ImportOutcome.Failed("qBittorrent didn't report where the download is saved.", BlocklistReason.Unreadable);
        }

        var candidates = files.Where(f => ComicArchive.IsArchive(f.Path)).ToList();
        if (candidates.Count == 0)
        {
            return ImportOutcome.Failed("The download contains no comic archives (.cbz/.cbr/.cb7...).", BlocklistReason.Unreadable);
        }

        var (pairs, unmatched) = MapFiles(primary, openRows, candidates, download);
        var imported = new List<int>();
        string? lastFailure = null;
        BlocklistReason? lastReason = null;

        foreach (var (file, wanted) in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ResolveSafely(download.SavePath, file.Path);
            if (source is null || !File.Exists(source))
            {
                lastFailure = $"{file.Path} is missing from the download folder.";
                lastReason = BlocklistReason.DownloadFailed;
                continue;
            }

            try
            {
                var destination = await ImportOneAsync(settings, wanted, source, cancellationToken).ConfigureAwait(false);
                imported.Add(wanted.Id);
                events.Publish(new IssueImportedEvent(wanted.Id, $"{wanted.WatchedSeries?.Name} #{wanted.IssueNumber}", destination));
            }
            catch (ImportFailureException ex)
            {
                lastFailure = ex.Message;
                lastReason = ex.Reason;
            }
            catch (IOException ex)
            {
                // A disk problem (full, locked, no permission) is not the release's fault: defer rather than blocklist a good file.
                return ImportOutcome.Deferred($"Couldn't write into the library folder: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                return ImportOutcome.Deferred($"Couldn't write into the library folder: {ex.Message}");
            }
        }

        if (imported.Count == 0)
        {
            return ImportOutcome.Failed(lastFailure ?? "Nothing in the download matched the issue you wanted.", lastReason ?? BlocklistReason.Unreadable) with
            {
                Unmatched = unmatched,
            };
        }

        // A pack that didn't contain the issue it was grabbed for must not be re-imported forever: settle that row.
        if (!imported.Contains(wantedIssueId))
        {
            SettleMissingPrimary(wantedIssueId, download);
        }

        return new ImportOutcome(imported) { Unmatched = unmatched, RemoveTorrent = settings.MoveOriginalOnImport && unmatched.Count == 0 };
    }

    /// <summary>Pairs each archive with the open wanted issue it satisfies. One file in a non-pack download belongs to the issue it was grabbed for.</summary>
    private static (List<(DownloadFile File, WantedIssue Wanted)> Pairs, List<string> Unmatched) MapFiles(
        WantedIssue primary, List<WantedIssue> openRows, List<DownloadFile> archives, DownloadStatus download)
    {
        var pairs = new List<(DownloadFile, WantedIssue)>();
        var unmatched = new List<string>();

        if (archives.Count == 1 && !PackDetector.Detect(download.Name).IsPack)
        {
            pairs.Add((archives[0], primary));
            return (pairs, unmatched);
        }

        var seriesName = primary.WatchedSeries?.Name ?? string.Empty;
        var taken = new HashSet<int>();

        foreach (var file in archives)
        {
            var title = Path.GetFileNameWithoutExtension(file.Path);
            var row = openRows.FirstOrDefault(w => !taken.Contains(w.Id) && ReleaseEvaluator.Evaluate(
                new IndexerRelease { Title = title, DownloadUrl = "x" }, new IndexerQuery(seriesName, w.IssueNumber), new ScoringOptions()) is not null);

            // A file name that gives nothing away ("scan_0007.cbz"): fall back to the archive's own ComicInfo.xml.
            if (row is null && download.SavePath is not null && ResolveSafely(download.SavePath, file.Path) is { } full && File.Exists(full)
                && ComicArchive.TryReadIdentity(full) is var (series, number))
            {
                row = openRows.FirstOrDefault(w => !taken.Contains(w.Id) && SeriesNames.Same(series, seriesName) && IssueNumbers.Equal(number, w.IssueNumber));
            }

            if (row is null)
            {
                unmatched.Add(file.Path);
                continue;
            }

            taken.Add(row.Id);
            pairs.Add((file, row));
        }

        return (pairs, unmatched);
    }

    private async Task<string> ImportOneAsync(AcquisitionSettings settings, WantedIssue wanted, string sourcePath, CancellationToken cancellationToken)
    {
        var watched = wanted.WatchedSeries!;
        var temp = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        try
        {
            var info = settings.WriteComicInfo
                ? new ComicInfoFields(watched.Name, wanted.IssueNumber, wanted.Name, wanted.StoreDate?.Year, wanted.StoreDate?.Month, wanted.StoreDate?.Day, watched.Publisher)
                : null;

            var (cbz, _) = ComicArchive.PrepareCbz(sourcePath, temp, info);

            var relative = ImportNaming.FormatPath(settings.RenameTemplate, watched, wanted, sourcePath, ".cbz");
            var destination = UniquePath(Path.Combine(settings.DestinationFolderPath, relative.Replace('/', Path.DirectorySeparatorChar)));

            // The template can never place a file outside the library folder.
            var root = Path.GetFullPath(settings.DestinationFolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(destination).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new ImportFailureException(BlocklistReason.Unreadable, "The naming template resolved to a path outside the library folder.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(cbz, destination);

            var issueId = await ingester.IngestAsync(destination, cancellationToken).ConfigureAwait(false);
            Record(wanted.Id, watched, wanted.IssueNumber, issueId);
            return destination;
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    private void Record(int wantedId, WatchedSeries watched, string number, int? issueId)
    {
        using var context = createContext();
        var wanted = context.WantedIssues.First(w => w.Id == wantedId);
        wanted.Status = WantedIssueStatus.Imported;
        wanted.IssueId = issueId;
        wanted.ImportedAt = _now();
        wanted.DownloadProgress = null;
        wanted.FailureReason = null;
        context.SaveChanges();

        if (issueId is int id)
        {
            PlaceholderRelinker.Relink(context, watched.Name, watched.SeriesId, number, id);
        }
    }

    private void SettleMissingPrimary(int wantedIssueId, DownloadStatus download)
    {
        using var context = createContext();
        var wanted = context.WantedIssues.Include(w => w.WatchedSeries).First(w => w.Id == wantedIssueId);
        const string reason = "The download didn't contain this issue.";
        BlocklistService.Add(context, wanted.GrabbedTitle ?? download.Name, wanted.TorrentHash, BlocklistReason.Unreadable, reason);
        wanted.Status = WantedIssueStatus.Failed;
        wanted.FailureReason = reason;
        wanted.DownloadProgress = null;
        context.SaveChanges();
        events.Publish(new IssueFailedEvent(wanted.Id, $"{wanted.WatchedSeries?.Name} #{wanted.IssueNumber}", reason));
    }

    /// <summary>The file path inside the download folder, or <c>null</c> if it would escape it (a hostile torrent with "..\" in a path).</summary>
    private static string? ResolveSafely(string savePath, string relative)
    {
        var root = Path.GetFullPath(savePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>Never overwrites: "X.cbz" becomes "X (2).cbz" when it exists.</summary>
    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int n = 2; n < 1000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Too many files with the same name.");
    }
}
