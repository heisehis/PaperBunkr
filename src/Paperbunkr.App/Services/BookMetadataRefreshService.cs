using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine.IO.Provider.Books;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <param name="Scanned">Books whose file was opened.</param>
/// <param name="Updated">Books that gained at least one field.</param>
/// <param name="Failed">Files that were missing or could not be parsed.</param>
/// <param name="FieldsFilled">Total fields filled across all books.</param>
public sealed record BookMetadataRefreshResult(int Scanned, int Updated, int Failed, int FieldsFilled)
{
    public string Describe() => Updated == 0
        ? (Failed == 0 ? "No new book metadata found" : $"No new book metadata found ({Failed} file{(Failed == 1 ? string.Empty : "s")} could not be read)")
        : $"Filled {FieldsFilled} field{(FieldsFilled == 1 ? string.Empty : "s")} on {Updated} book{(Updated == 1 ? string.Empty : "s")}"
          + (Failed == 0 ? string.Empty : $" ({Failed} unreadable)");
}

/// <summary>
/// "Refresh metadata from files" (docs/superpowers/specs/2026-10-26-books-slice1-data-foundation-design.md §4.1): re-reads each book's
/// file and fills only <b>empty</b> fields through <see cref="BookMetadataApplier"/>, so it never overwrites a value (and never re-adds
/// a field the user cleared by hand). Runs as a scheduled task (first run soon after the migration, then weekly) and from the Books menu.
/// </summary>
public sealed class BookMetadataRefreshService
{
    private const int BatchSize = 20;

    private readonly Func<PaperbunkrDbContext> _contextFactory;

    public BookMetadataRefreshService()
        : this(PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal BookMetadataRefreshService(Func<PaperbunkrDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public Task<BookMetadataRefreshResult> RunAsync(IProgress<(int Done, int Total)>? progress, CancellationToken ct = default) =>
        Task.Run(() => Run(progress, ct), ct);

    private BookMetadataRefreshResult Run(IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        using var context = _contextFactory();

        List<int> ids = context.Books.AsNoTracking().OrderBy(b => b.Id).Select(b => b.Id).ToList();
        int scanned = 0, updated = 0, failed = 0, filled = 0, done = 0;
        progress?.Report((0, ids.Count));

        foreach (int[] batch in ids.Chunk(BatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var books = context.Books
                .Include(b => b.BookSeries)
                .Include(b => b.Tags)
                .Include(b => b.FieldSources)
                .Where(b => batch.Contains(b.Id))
                .ToList();

            var seriesByName = new Dictionary<string, BookSeries>(StringComparer.OrdinalIgnoreCase);
            foreach (var series in context.BookSeries.OrderBy(s => s.Id).ToList())
            {
                seriesByName.TryAdd(series.Name, series);
            }

            foreach (var book in books)
            {
                ct.ThrowIfCancellationRequested();

                if (!File.Exists(book.FilePath))
                {
                    failed++;
                    progress?.Report((++done, ids.Count));
                    continue;
                }

                try
                {
                    using IBookTextSource source = BookTextSourceFactory.Create(book.Format, book.FilePath);
                    scanned++;

                    var changed = BookMetadataApplier.Apply(
                        book,
                        BookFieldValues.FromFile(source.Metadata),
                        BookFieldSourceKind.File,
                        sourceRef: null,
                        DateTime.UtcNow,
                        name =>
                        {
                            if (!seriesByName.TryGetValue(name, out var series))
                            {
                                series = new BookSeries { Name = name };
                                context.BookSeries.Add(series);
                                seriesByName[name] = series;
                            }

                            return series;
                        });

                    if (changed.Count > 0)
                    {
                        updated++;
                        filled += changed.Count;
                    }
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    failed++;
                }

                progress?.Report((++done, ids.Count));
            }

            context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        var settings = context.GetOrCreateAppSettings();
        settings.BookMetadataBackfillDone = true;
        context.SaveChanges();

        return new BookMetadataRefreshResult(scanned, updated, failed, filled);
    }
}
