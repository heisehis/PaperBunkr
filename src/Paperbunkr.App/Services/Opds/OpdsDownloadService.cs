using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Calibre;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services.Opds;

/// <summary>
/// Downloads a catalog entry into the Downloads folder, imports that exact file, and applies the catalog's descriptive values
/// (docs/superpowers/specs/2026-10-27-books-slice4c-opds-design.md §5). Values are only filled into empty fields and are recorded as "OPDS".
/// </summary>
public sealed class OpdsDownloadService
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly OpdsClient _client;
    private readonly Func<string, Task<int?>> _importFile;

    /// <param name="importFile">Imports one file into the book library and returns the new book's id (null when it was not added); the default uses the folder scanner's single-file import.</param>
    public OpdsDownloadService(Func<PaperbunkrDbContext>? contextFactory = null, OpdsClient? client = null, Func<string, Task<int?>>? importFile = null)
    {
        _contextFactory = contextFactory ?? PaperbunkrDb.CreateContext;
        _client = client ?? new OpdsClient();
        _importFile = importFile ?? ImportWithScanner;
    }

    private static async Task<int?> ImportWithScanner(string path)
    {
        var result = await new BookFolderScanService().ImportNewFilesAsync([path], new Progress<(int Done, int Total)>()).ConfigureAwait(false);
        return result.AddedBookIds.Count > 0 ? result.AddedBookIds[0] : null;
    }

    public async Task<(string Path, int? BookId)> DownloadAsync(OpdsCatalog catalog, OpdsEntry entry, string folder, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var link = OpdsClient.ChooseAcquisition(entry.Acquisitions)
            ?? throw new InvalidDataException("This entry offers no EPUB, FB2, MOBI or PDF.");
        return await DownloadAsync(catalog, entry, link, folder, progress, ct).ConfigureAwait(false);
    }

    public async Task<(string Path, int? BookId)> DownloadAsync(OpdsCatalog catalog, OpdsEntry entry, OpdsLink link, string folder, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var credentials = OpdsCatalogStore.CredentialsOf(catalog);
        string name = string.IsNullOrWhiteSpace(entry.Author) ? entry.Title : $"{entry.Title} - {entry.Author}";
        string path = await _client.DownloadAsync(link, folder, name, credentials, progress, ct).ConfigureAwait(false);
        int? bookId = await _importFile(path).ConfigureAwait(false);

        using var context = _contextFactory();
        context.OpdsDownloads.Add(new OpdsDownload
        {
            CatalogId = catalog.Id,
            EntryId = entry.Id,
            Title = entry.Title,
            Author = entry.Author,
            FilePath = path,
            DownloadedUtc = DateTime.UtcNow,
            BookId = bookId,
        });

        if (bookId is int id)
        {
            ApplyMetadata(context, id, entry);
        }

        context.SaveChanges();
        return (path, bookId);
    }

    private static void ApplyMetadata(PaperbunkrDbContext context, int bookId, OpdsEntry entry)
    {
        var book = context.Books.Include(b => b.Tags).Include(b => b.FieldSources).Include(b => b.BookSeries).FirstOrDefault(b => b.Id == bookId);
        if (book is null)
        {
            return;
        }

        var seriesByName = context.BookSeries.OrderBy(s => s.Id).ToList().GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        BookMetadataApplier.Apply(
            book,
            new BookFieldValues
            {
                Summary = entry.Summary,
                SeriesName = entry.Series,
                SeriesIndex = entry.SeriesIndex,
                LanguageISO = CalibreMapper.MapLanguage(entry.Language),
                Tags = entry.Categories,
            },
            BookFieldSourceKind.Opds,
            entry.Id,
            DateTime.UtcNow,
            seriesName =>
            {
                if (!seriesByName.TryGetValue(seriesName, out var series))
                {
                    series = new BookSeries { Name = seriesName };
                    context.BookSeries.Add(series);
                    seriesByName[seriesName] = series;
                }

                return series;
            });
    }

    /// <summary>
    /// Which of <paramref name="entries"/> are already in the library: downloaded from this catalog before (and the book still exists), or a book
    /// with the same title and author.
    /// </summary>
    public HashSet<string> InLibrary(int catalogId, IEnumerable<OpdsEntry> entries)
    {
        using var context = _contextFactory();
        var existingBookIds = context.Books.Select(b => b.Id).ToHashSet();
        var downloaded = context.OpdsDownloads.Where(d => d.CatalogId == catalogId && d.BookId != null).ToList()
            .Where(d => existingBookIds.Contains(d.BookId!.Value)).Select(d => d.EntryId).ToHashSet();
        var keys = context.Books.Select(b => new { b.Title, b.Author }).ToList()
            .Select(b => Key(b.Title, b.Author)).ToHashSet();

        return entries.Where(e => downloaded.Contains(e.Id) || keys.Contains(Key(e.Title, e.Author))).Select(e => e.Id).ToHashSet();
    }

    private static string Key(string? title, string? author) =>
        CalibreMapper.NormalizeForMatch(title) + "|" + CalibreMapper.NormalizeForMatch(author);
}
