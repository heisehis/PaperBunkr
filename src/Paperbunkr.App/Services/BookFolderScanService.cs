using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>Result of a completed <see cref="BookFolderScanService"/> run.</summary>
public record BookFolderScanResult(int BooksAdded, int SeriesTouched);

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

            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                if (existingPaths.Contains(file))
                {
                    continue;
                }

                string extension = Path.GetExtension(file);
                if (string.Equals(extension, ".epub", StringComparison.OrdinalIgnoreCase))
                {
                    candidateFiles.Add((file, BookFormat.Epub));
                }
                else if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    candidateFiles.Add((file, BookFormat.Pdf));
                }
            }
        }

        int total = candidateFiles.Count;
        int done = 0;
        progress.Report((0, total));

        // Loaded once and updated in-memory as new series are created within this run, same
        // rationale as LibraryFolderScanner's seriesByName dictionary.
        var seriesByName = context.BookSeries.ToList().ToDictionary(s => s.Name, s => s, StringComparer.OrdinalIgnoreCase);
        var seriesTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int booksAdded = 0;

        foreach (var (filePath, format) in candidateFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using IBookTextSource source = format == BookFormat.Epub
                    ? new EpubBookSource(filePath)
                    : new PdfBookSource(filePath);

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
                booksAdded++;
            }
            catch
            {
                // One bad file doesn't stop the batch - same contract as LibraryFolderScanner.ScanAll.
            }

            progress.Report((++done, total));
        }

        context.SaveChanges();
        return new BookFolderScanResult(booksAdded, seriesTouched.Count);
    }
}
