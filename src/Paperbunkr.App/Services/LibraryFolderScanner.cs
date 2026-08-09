using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Paperbunkr.Data;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>Result of a completed <see cref="LibraryFolderScanner"/> run.</summary>
public record LibraryFolderScanResult(int IssuesAdded, int SeriesTouched);

/// <summary>Result of a completed <see cref="LibraryFolderScanner.SyncMetadataAsync"/> run.</summary>
public record LibraryMetadataSyncResult(int IssuesUpdated);

/// <summary>
/// On-demand folder scan-and-import (docs/superpowers/specs/2026-08-07-preferences-libraries-tab-design.md
/// §2, embedded-metadata follow-up in docs/superpowers/specs/
/// 2026-08-09-embedded-metadata-and-migration-relocation-design.md §1) - Paperbunkr's first
/// non-migration way to add comics to the library. Reads embedded ComicInfo.xml when present (via
/// the same <see cref="IInfoStorage"/> cast <c>ComicBook.RefreshInfoFromFile</c> uses internally) -
/// embedded metadata wins per-field over <see cref="ComicNameInfo.FromFilePath(string)"/> filename
/// parsing, which stays as the fallback for files with no embedded info or an unsupported format.
/// No live <c>FileSystemWatcher</c> auto-import - still a real, deliberately deferred follow-up.
/// </summary>
public class LibraryFolderScanner
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public LibraryFolderScanner()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal LibraryFolderScanner(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<LibraryFolderScanResult> ScanAllAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        return await Task.Run(() => ScanAll(progress, ct), ct);
    }

    private LibraryFolderScanResult ScanAll(IProgress<(int Done, int Total)> progress, CancellationToken ct)
    {
        using var context = _contextFactory();

        var supportedExtensions = new HashSet<string>(Providers.Readers.GetFileExtensions(), StringComparer.OrdinalIgnoreCase);
        var existingPaths = new HashSet<string>(
            context.Issues.Where(i => i.FilePath != null).Select(i => i.FilePath!),
            StringComparer.OrdinalIgnoreCase);

        var candidateFiles = new List<string>();
        foreach (string folder in context.WatchedFolders.Select(w => w.Path).ToList())
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            candidateFiles.AddRange(
                Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(f => supportedExtensions.Contains(Path.GetExtension(f)) && !existingPaths.Contains(f)));
        }

        int total = candidateFiles.Count;
        int done = 0;
        progress.Report((0, total));

        // Loaded once and updated in-memory as new series are created within this run, so multiple
        // new issues for the same not-yet-existing series in one scan land on the same Series row
        // instead of creating a duplicate per file.
        var seriesByName = context.Series.ToList().ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);
        var seriesTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int issuesAdded = 0;

        foreach (string file in candidateFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var nameInfo = ComicNameInfo.FromFilePath(file);
                var embeddedInfo = TryReadEmbeddedInfo(file);

                string seriesName = !string.IsNullOrWhiteSpace(embeddedInfo?.Series)
                    ? embeddedInfo.Series.Trim()
                    : (string.IsNullOrWhiteSpace(nameInfo.Series) ? "Unknown" : nameInfo.Series.Trim());

                if (!seriesByName.TryGetValue(seriesName, out var series))
                {
                    series = new Series { Name = seriesName };
                    context.Series.Add(series);
                    seriesByName[seriesName] = series;
                }

                var issue = new Issue
                {
                    Series = series,
                    FilePath = file,
                    AddedTime = DateTime.UtcNow,
                };

                if (embeddedInfo is not null)
                {
                    CeLibraryMigrator.MapStoryFields(embeddedInfo, issue);
                }

                // Filename parsing fills in only what embedded metadata left blank - embedded wins
                // per-field, not all-or-nothing.
                issue.Number ??= string.IsNullOrWhiteSpace(nameInfo.Number) ? null : nameInfo.Number;
                issue.Volume ??= nameInfo.Volume > 0 ? nameInfo.Volume : null;
                issue.Year ??= nameInfo.Year > 0 ? nameInfo.Year : null;

                series.Issues.Add(issue);
                context.Issues.Add(issue);

                issuesAdded++;
                seriesTouched.Add(seriesName);
            }
            catch
            {
                // One bad file doesn't stop the batch - same contract as CoverThumbnailService.GenerateAllAsync.
            }

            progress.Report((++done, total));
        }

        context.SaveChanges();
        return new LibraryFolderScanResult(issuesAdded, seriesTouched.Count);
    }

    /// <summary>
    /// "Sync Metadata" (docs/superpowers/specs/2026-08-09-embedded-metadata-and-migration-relocation-design.md
    /// follow-up) - unlike <see cref="ScanAllAsync"/>, which only ever touches newly-discovered
    /// files, this re-reads embedded ComicInfo.xml for every already-linked issue and fills in
    /// currently-blank fields (<see cref="CeLibraryMigrator.MapStoryFields"/> with
    /// <c>onlyIfBlank: true</c> - never overwrites a value that's already there, from migration or
    /// a manual edit). Presence-based and safe to re-run, same "fills gaps, one bad file doesn't
    /// stop the batch" contract as <see cref="CoverThumbnailService.GenerateAllAsync"/>.
    /// </summary>
    public async Task<LibraryMetadataSyncResult> SyncMetadataAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        return await Task.Run(() => SyncMetadata(progress, ct), ct);
    }

    private LibraryMetadataSyncResult SyncMetadata(IProgress<(int Done, int Total)> progress, CancellationToken ct)
    {
        using var context = _contextFactory();

        var issues = context.Issues.Where(i => i.FilePath != null).ToList();
        int total = issues.Count;
        int done = 0;
        progress.Report((0, total));

        foreach (var issue in issues)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(issue.FilePath))
                {
                    var embeddedInfo = TryReadEmbeddedInfo(issue.FilePath!);
                    if (embeddedInfo is not null)
                    {
                        CeLibraryMigrator.MapStoryFields(embeddedInfo, issue, onlyIfBlank: true);
                    }
                }
            }
            catch
            {
                // One bad file doesn't stop the batch - same contract as ScanAllAsync.
            }

            progress.Report((++done, total));
        }

        int issuesUpdated = context.ChangeTracker.Entries<Issue>().Count(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Modified);
        context.SaveChanges();
        return new LibraryMetadataSyncResult(issuesUpdated);
    }

    /// <summary>
    /// Reads embedded ComicInfo.xml via the archive reader's own <see cref="IInfoStorage"/>
    /// implementation (the same one <c>ComicBook.RefreshInfoFromFile</c> uses internally) - the
    /// same provider <see cref="PageImageDecoder"/> opens for page decoding, just a separate
    /// short-lived open here since only metadata is needed. Returns null - never throws - for
    /// anything that doesn't pan out: unsupported/dynamic formats, no embedded ComicInfo.xml,
    /// or a malformed one. Callers fall back to filename parsing in every case.
    /// </summary>
    private static ComicInfo? TryReadEmbeddedInfo(string file)
    {
        try
        {
            using var provider = Providers.Readers.CreateSourceProvider(file);
            if (provider is not IInfoStorage infoStorage)
            {
                return null;
            }

            provider.Open(async: false);
            return infoStorage.LoadInfo(InfoLoadingMethod.Complete);
        }
        catch
        {
            return null;
        }
    }
}
