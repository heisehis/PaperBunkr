using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.Services;

/// <summary>Outcome of a completed <see cref="DragImportService.ImportAsync"/> call - the counts
/// drive the Library/Reading-List screens' completion toast, and <see cref="IssueIds"/> lets the
/// Reading List screen attach membership for every comic in the drop (freshly imported ones plus
/// ones that already matched an existing <see cref="Issue"/> by path).</summary>
public record DragImportResult(
    int Imported,
    int AlreadyInLibrary,
    int SkippedUnsupported,
    int ReadingListsImported,
    IReadOnlyList<int> IssueIds);

/// <summary>
/// Shared entry point for the drag-and-drop file/folder import both the Library and Reading List
/// screens expose (docs/superpowers/specs/2026-08-31-drag-and-drop-import-design.md). Takes the raw
/// list of dropped filesystem paths (files and folders mixed) and:
/// <list type="number">
/// <item>Expands dropped folders to their contained comic files (recursive, same extension filter
/// <see cref="LibraryFolderScanner"/> uses) and registers each dropped folder as a
/// <see cref="WatchedFolder"/> if not already registered (<c>Watch = false</c>, matching the manual
/// "Add Folder" flow) - a dropped folder is an explicit "this belongs in my library" gesture.</item>
/// <item>Buckets the flattened file list by extension: <c>.cbl</c>/<c>.csv</c> import as new reading
/// lists, supported comic extensions import into the library, everything else is counted as
/// skipped.</item>
/// <item>Imports comics via <see cref="LibraryFolderScanner.ImportNewFilesAsync"/> (already dedupes
/// against existing <see cref="Issue.FilePath"/>) and reading-list files via the existing
/// <see cref="CblReadingListIO"/>/<see cref="CsvReadingListIO"/> import paths, each wrapped so one
/// malformed file doesn't abort the batch.</item>
/// <item>Re-queries every dropped comic path back to an <see cref="Issue.Id"/> for the result.</item>
/// </list>
/// Constructed fresh at each call site (<c>new DragImportService()</c>), matching this app's
/// "no DI container, construct stateless providers fresh" precedent.
/// </summary>
public class DragImportService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly LibraryFolderScanner _scanner;

    public DragImportService()
        : this(PaperbunkrDb.CreateContext, new LibraryFolderScanner())
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal DragImportService(Func<PaperbunkrDbContext> contextFactory, LibraryFolderScanner scanner)
    {
        _contextFactory = contextFactory;
        _scanner = scanner;
    }

    public Task<DragImportResult> ImportAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        return Task.Run(() => Import(paths, ct), ct);
    }

    private DragImportResult Import(IReadOnlyList<string> paths, CancellationToken ct)
    {
        var supportedExtensions = new HashSet<string>(Providers.Readers.GetFileExtensions(), StringComparer.OrdinalIgnoreCase);

        var comicFiles = new List<string>();
        var readingListFiles = new List<string>();
        int skippedUnsupported = 0;

        // Step 1: expand folders to their contained comic files, and register each dropped folder as
        // a WatchedFolder (exact-path dedup, same check PreferencesScreenViewModel.AddFolder does).
        using (var context = _contextFactory())
        {
            var registered = new HashSet<string>(context.WatchedFolders.Select(w => w.Path), StringComparer.OrdinalIgnoreCase);
            bool addedFolder = false;

            foreach (string path in paths)
            {
                ct.ThrowIfCancellationRequested();

                if (Directory.Exists(path))
                {
                    if (registered.Add(path))
                    {
                        context.WatchedFolders.Add(new WatchedFolder { Path = path });
                        addedFolder = true;
                    }

                    comicFiles.AddRange(EnumerateComicFiles(path, supportedExtensions));
                }
                else if (File.Exists(path))
                {
                    string ext = Path.GetExtension(path);
                    if (ext.Equals(".cbl", StringComparison.OrdinalIgnoreCase) || ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        readingListFiles.Add(path);
                    }
                    else if (supportedExtensions.Contains(ext))
                    {
                        comicFiles.Add(path);
                    }
                    else
                    {
                        skippedUnsupported++;
                    }
                }

                // A path that's neither an existing file nor folder (e.g. a browser-sourced drag with
                // no real file behind it) is silently dropped - it was never a real thing to import.
            }

            if (addedFolder)
            {
                context.SaveChanges();
            }
        }

        // Step 2: import the comic files. LibraryFolderScanner.ImportNewFilesAsync already filters to
        // supported extensions and dedupes against existing Issue.FilePath, so no new dedup here.
        int imported = 0;
        if (comicFiles.Count > 0)
        {
            imported = _scanner.ImportNewFilesAsync(comicFiles, new Progress<(int Done, int Total)>(), ct)
                .GetAwaiter().GetResult().IssuesAdded;
        }

        // Step 3: import reading-list files, one bad file skipped and counted rather than aborting.
        int readingListsImported = 0;
        foreach (string file in readingListFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var context = _contextFactory();
                if (Path.GetExtension(file).Equals(".cbl", StringComparison.OrdinalIgnoreCase))
                {
                    CblReadingListIO.Import(context, file);
                }
                else
                {
                    CsvReadingListIO.Import(context, file);
                }

                readingListsImported++;
            }
            catch
            {
                // Malformed reading-list file - skipped, not counted as imported, doesn't stop the batch.
            }
        }

        // Step 4: resolve an IssueId for every dropped comic path (freshly imported + already present).
        IReadOnlyList<int> issueIds = Array.Empty<int>();
        if (comicFiles.Count > 0)
        {
            var wanted = new HashSet<string>(comicFiles, StringComparer.OrdinalIgnoreCase);
            using var context = _contextFactory();
            issueIds = context.Issues
                .Where(i => i.FilePath != null)
                .Select(i => new { i.Id, i.FilePath })
                .AsEnumerable()
                .Where(x => wanted.Contains(x.FilePath!))
                .Select(x => x.Id)
                .ToList();
        }

        // AlreadyInLibrary = every dropped comic that resolved to an Issue minus the ones just added.
        // A comic file that failed to import (corrupt archive) resolves to nothing and is counted
        // nowhere, matching LibraryFolderScanner's own silent per-file skip.
        int alreadyInLibrary = Math.Max(0, issueIds.Count - imported);

        return new DragImportResult(imported, alreadyInLibrary, skippedUnsupported, readingListsImported, issueIds);
    }

    private static IEnumerable<string> EnumerateComicFiles(string folder, HashSet<string> supportedExtensions)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
