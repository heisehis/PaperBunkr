using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>Result of a completed <see cref="BookFolderScanService"/> run.</summary>
public record BookFolderScanResult(int BooksAdded, int SeriesTouched, IReadOnlyList<int> AddedBookIds);

/// <summary>
/// On-demand Novel folder scan-and-import (docs/superpowers/specs/
/// 2026-08-09-novels-epub-pdf-support-design.md §3), structurally parallel to
/// <see cref="LibraryFolderScanner"/> for the comic library, but against the independent
/// Book/BookSeries schema - no shared code or tables with the comic scan path.
/// </summary>
public class BookFolderScanService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public BookFolderScanService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal BookFolderScanService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<BookFolderScanResult> ScanAllAsync(IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        return await Task.Run(() => ScanAll(progress, ct), ct);
    }

    private BookFolderScanResult ScanAll(IProgress<(int Done, int Total)> progress, CancellationToken ct)
    {
        using var context = _contextFactory();

        var existingPaths = new HashSet<string>(
            context.Books.Select(b => b.FilePath),
            StringComparer.OrdinalIgnoreCase);

        var candidateFiles = new List<(string Path, BookFormat Format)>();
        foreach (string folder in context.BookFolders.Select(f => f.Path).ToList())
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            candidateFiles.AddRange(
                Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(f => !existingPaths.Contains(f))
                    .Select(f => (Path: f, Format: ClassifyFormat(f)))
                    .Where(c => c.Format is not null)
                    .Select(c => (c.Path, c.Format!.Value)));
        }

        return ImportFiles(context, candidateFiles, progress, ct);
    }

    /// <summary>
    /// Live open-on-launch entry point (docs/superpowers/specs/2026-09-16-book-file-associations-
    /// design.md) - imports a specific, already-known file (the shape Windows' own file-association
    /// launch command line produces), mirroring <see cref="LibraryFolderScanner.ImportNewFilesAsync"/>'s
    /// role for the comic library. <c>ConfigureAwait(false)</c> for the exact same reason that method
    /// now carries it: a synchronous <c>.GetAwaiter().GetResult()</c> caller on the UI thread would
    /// otherwise deadlock waiting for this method's own continuation, which needs that same blocked
    /// thread to resume (docs/paperbunkr-todo.md, 2026-09-16 open-on-launch deadlock note).
    /// </summary>
    public async Task<BookFolderScanResult> ImportNewFilesAsync(IReadOnlyCollection<string> files, IProgress<(int Done, int Total)> progress, CancellationToken ct = default)
    {
        return await Task.Run(
            () =>
            {
                using var context = _contextFactory();

                var existingPaths = new HashSet<string>(
                    context.Books.Select(b => b.FilePath),
                    StringComparer.OrdinalIgnoreCase);

                var candidateFiles = files
                    .Where(f => !existingPaths.Contains(f))
                    .Select(f => (Path: f, Format: ClassifyFormat(f)))
                    .Where(c => c.Format is not null)
                    .Select(c => (c.Path, c.Format!.Value))
                    .ToList();

                return ImportFiles(context, candidateFiles, progress, ct);
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Extension-based format classification, shared by <see cref="ScanAll"/> and
    /// <see cref="ImportNewFilesAsync"/>. Returns <see langword="null"/> for anything unrecognized.
    /// ".fb2.zip" (a common FB2 distribution convention) has a ".zip" extension per
    /// <see cref="Path.GetExtension(string)"/>, so it's checked against the full lowercased path, not
    /// just the last extension segment, before falling through to the single-extension cases.
    /// </summary>
    private static BookFormat? ClassifyFormat(string filePath)
    {
        if (filePath.EndsWith(".fb2.zip", StringComparison.OrdinalIgnoreCase))
        {
            return BookFormat.Fb2;
        }

        string extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".epub", StringComparison.OrdinalIgnoreCase))
        {
            return BookFormat.Epub;
        }

        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BookFormat.Pdf;
        }

        if (string.Equals(extension, ".fb2", StringComparison.OrdinalIgnoreCase))
        {
            return BookFormat.Fb2;
        }

        if (string.Equals(extension, ".mobi", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".azw3", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".azw", StringComparison.OrdinalIgnoreCase))
        {
            return BookFormat.Mobi;
        }

        return null;
    }

    /// <summary>
    /// Shared per-file import body for both <see cref="ScanAll"/> (an entire watched folder's new
    /// files) and <see cref="ImportNewFilesAsync"/> (a single open-on-launch file) - same
    /// text-source parse, series find-or-create, and id tracking regardless of which caller found
    /// the files.
    /// </summary>
    private static BookFolderScanResult ImportFiles(PaperbunkrDbContext context, List<(string Path, BookFormat Format)> candidateFiles, IProgress<(int Done, int Total)> progress, CancellationToken ct)
    {
        int total = candidateFiles.Count;
        int done = 0;
        progress.Report((0, total));

        // Loaded once and updated in-memory as new series are created within this run, same
        // rationale as LibraryFolderScanner's seriesByName dictionary.
        var seriesByName = context.BookSeries.ToList().ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);
        var seriesTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedBooks = new List<Book>();

        foreach (var (filePath, format) in candidateFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using IBookTextSource source = BookTextSourceFactory.Create(format, filePath);

                var book = new Book
                {
                    Title = source.Metadata.Title,
                    Author = source.Metadata.Author,
                    Format = format,
                    FilePath = filePath,
                    AddedTime = DateTime.UtcNow,
                };

                string? seriesName = source.Metadata.SeriesName;
                if (!string.IsNullOrWhiteSpace(seriesName))
                {
                    if (!seriesByName.TryGetValue(seriesName, out var series))
                    {
                        series = new BookSeries { Name = seriesName };
                        context.BookSeries.Add(series);
                        seriesByName[seriesName] = series;
                    }

                    book.BookSeries = series;
                    series.Books.Add(book);
                    seriesTouched.Add(seriesName);
                }

                context.Books.Add(book);
                addedBooks.Add(book);
            }
            catch
            {
                // One bad file doesn't stop the batch - same contract as LibraryFolderScanner.ScanAll.
            }

            progress.Report((++done, total));
        }

        context.SaveChanges();
        return new BookFolderScanResult(addedBooks.Count, seriesTouched.Count, addedBooks.Select(b => b.Id).ToList());
    }
}
