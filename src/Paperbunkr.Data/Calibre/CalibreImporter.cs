using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Books;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Calibre;

/// <param name="ReadStatusColumn">The label of a Calibre bool/text custom column that says a book has been read, or null.</param>
/// <param name="RefreshExisting">Also update books already imported (still never a field marked Manual).</param>
public sealed record CalibreImportOptions(string? ReadStatusColumn = null, bool RefreshExisting = false);

/// <summary>A Calibre book that looks like one already in the library (same ISBN, or same title and author) and needs a human decision.</summary>
public sealed record CalibreReviewItem(int CalibreBookId, string Title, string? Author, int ExistingBookId, string ExistingTitle, string Reason);

public sealed record CalibreImportResult(
    int Added,
    int Updated,
    int Skipped,
    int FileNotFound,
    int NoLongerInCalibre,
    IReadOnlyList<CalibreReviewItem> Review)
{
    public int NeedsReview => Review.Count;
}

/// <summary>
/// Imports a Calibre library into the book library (docs/superpowers/specs/2026-10-27-books-slice4a-calibre-import-design.md §3-§4). Read-only on
/// Calibre's side; books reference their Calibre files in place. Matching: same Calibre library + book id, then same file path, then ISBN or
/// an unambiguous title + author (those two are offered for review, never merged automatically).
/// </summary>
public sealed class CalibreImporter
{
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<int, string>? _setCover;

    /// <param name="setCover">Sets a new book's custom cover from Calibre's <c>cover.jpg</c> (book id, image path); null skips covers.</param>
    public CalibreImporter(Func<PaperbunkrDbContext> contextFactory, Action<int, string>? setCover = null)
    {
        _contextFactory = contextFactory;
        _setCover = setCover;
    }

    private static readonly string[] ImportedFields =
    [
        BookFields.Publisher, BookFields.Language, BookFields.Isbn, BookFields.Series, BookFields.SeriesIndex,
        BookFields.Summary, BookFields.Rating, BookFields.Tags,
    ];

    public CalibreImportResult Import(string libraryRoot, CalibreImportOptions options, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        using var reader = CalibreLibraryReader.Open(libraryRoot);
        var calibreBooks = reader.ReadBooks(options.ReadStatusColumn);
        string libraryId = reader.LibraryId;

        using var context = _contextFactory();
        var books = context.Books.Include(b => b.Tags).Include(b => b.FieldSources).Include(b => b.BookSeries).ToList();

        var byCalibre = books.Where(b => b.CalibreLibraryId == libraryId && b.CalibreBookId is not null)
            .ToDictionary(b => b.CalibreBookId!.Value);
        var byPath = new Dictionary<string, Book>(StringComparer.OrdinalIgnoreCase);
        foreach (var book in books)
        {
            byPath.TryAdd(book.FilePath, book);
        }

        var unlinked = books.Where(b => b.CalibreBookId is null).ToList();
        var byIsbn = unlinked.Where(b => !string.IsNullOrWhiteSpace(b.ISBN))
            .GroupBy(b => NormalizeIsbn(b.ISBN!)).ToDictionary(g => g.Key, g => g.ToList());
        var byTitleAuthor = unlinked
            .GroupBy(b => CalibreMapper.NormalizeForMatch(b.Title) + "|" + CalibreMapper.NormalizeForMatch(b.Author))
            .ToDictionary(g => g.Key, g => g.ToList());

        var seriesByName = new Dictionary<string, BookSeries>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in context.BookSeries.OrderBy(s => s.Id).ToList())
        {
            seriesByName.TryAdd(existing.Name, existing);
        }

        BookSeries ResolveSeries(string name)
        {
            if (!seriesByName.TryGetValue(name, out var series))
            {
                series = new BookSeries { Name = name };
                context.BookSeries.Add(series);
                seriesByName[name] = series;
            }

            return series;
        }

        int added = 0, updated = 0, skipped = 0, missing = 0, done = 0;
        var review = new List<CalibreReviewItem>();
        var newCovers = new List<(Book Book, string Cover)>();
        DateTime now = DateTime.UtcNow;
        progress?.Report((0, calibreBooks.Count));

        foreach (var calibre in calibreBooks)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((++done, calibreBooks.Count));

            var choice = CalibreMapper.ChooseFormat(calibre.Formats);
            if (choice is null)
            {
                skipped++;
                continue;
            }

            string path = CalibreMapper.ResolveFilePath(libraryRoot, calibre, choice.Value.Format);
            if (!File.Exists(path))
            {
                missing++;
                continue;
            }

            string? author = CalibreMapper.JoinAuthors(calibre.Authors);
            bool isNew = false;
            if (!byCalibre.TryGetValue(calibre.Id, out var target))
            {
                if (byPath.TryGetValue(path, out target))
                {
                    target.CalibreBookId = calibre.Id;
                    target.CalibreLibraryId = libraryId;
                    byCalibre[calibre.Id] = target;
                }
                else if (FindReviewCandidate(calibre, author, byIsbn, byTitleAuthor) is { } candidate)
                {
                    review.Add(new CalibreReviewItem(calibre.Id, calibre.Title, author, candidate.Book.Id, candidate.Book.Title, candidate.Reason));
                    continue;
                }
                else
                {
                    target = new Book
                    {
                        Title = calibre.Title,
                        Author = author,
                        Format = choice.Value.BookFormat,
                        FilePath = path,
                        AddedTime = now,
                        CalibreBookId = calibre.Id,
                        CalibreLibraryId = libraryId,
                    };
                    context.Books.Add(target);
                    byCalibre[calibre.Id] = target;
                    byPath[path] = target;
                    isNew = true;
                    added++;
                }
            }
            else if (!options.RefreshExisting)
            {
                continue;
            }

            bool changed = ApplyMetadata(target, calibre, now, ResolveSeries, isNew ? BookApplyMode.FillEmpty : options.RefreshExisting ? BookApplyMode.Override : BookApplyMode.FillEmpty);
            if (isNew)
            {
                if (CalibreMapper.CoverPath(libraryRoot, calibre) is { } cover && File.Exists(cover))
                {
                    newCovers.Add((target, cover));
                }
            }
            else if (changed)
            {
                updated++;
            }
        }

        context.SaveChanges();

        if (_setCover is not null)
        {
            foreach (var (book, cover) in newCovers)
            {
                try
                {
                    _setCover(book.Id, cover);
                }
                catch (Exception)
                {
                    // A cover that cannot be read only costs that book its Calibre cover; the file's own cover still shows.
                }
            }
        }

        var calibreIds = calibreBooks.Select(b => b.Id).ToHashSet();
        int gone = books.Count(b => b.CalibreLibraryId == libraryId && b.CalibreBookId is int id && !calibreIds.Contains(id));
        return new CalibreImportResult(added, updated, skipped, missing, gone, review);
    }

    /// <summary>The user chose "Link" on a review row: the existing book becomes that Calibre book and takes its values into empty fields.</summary>
    public void Link(string libraryRoot, CalibreReviewItem item, CalibreImportOptions options)
    {
        using var reader = CalibreLibraryReader.Open(libraryRoot);
        var calibre = reader.ReadBooks(options.ReadStatusColumn).FirstOrDefault(b => b.Id == item.CalibreBookId);
        if (calibre is null)
        {
            return;
        }

        using var context = _contextFactory();
        var book = context.Books.Include(b => b.Tags).Include(b => b.FieldSources).Include(b => b.BookSeries)
            .FirstOrDefault(b => b.Id == item.ExistingBookId);
        if (book is null)
        {
            return;
        }

        book.CalibreBookId = calibre.Id;
        book.CalibreLibraryId = reader.LibraryId;
        var seriesByName = new Dictionary<string, BookSeries>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in context.BookSeries.OrderBy(s => s.Id).ToList())
        {
            seriesByName.TryAdd(existing.Name, existing);
        }

        ApplyMetadata(book, calibre, DateTime.UtcNow, name =>
        {
            if (!seriesByName.TryGetValue(name, out var series))
            {
                series = new BookSeries { Name = name };
                context.BookSeries.Add(series);
            }

            return series;
        }, BookApplyMode.FillEmpty);
        context.SaveChanges();
    }

    private static bool ApplyMetadata(Book book, CalibreBook calibre, DateTime now, Func<string, BookSeries> resolveSeries, BookApplyMode mode)
    {
        // Override ignores Manual protection, so a refresh passes only the fields the user has not edited.
        ISet<string>? only = mode == BookApplyMode.Override
            ? ImportedFields.Where(f => !BookMetadataApplier.IsManual(book, f)).ToHashSet()
            : null;

        var changed = BookMetadataApplier.Apply(
            book, CalibreMapper.ToFieldValues(calibre), BookFieldSourceKind.Calibre, calibre.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            now, resolveSeries, mode, only);

        bool statusChanged = false;
        if (calibre.IsRead == true && book.Status == BookReadingStatus.Unread && !BookMetadataApplier.IsManual(book, BookFields.Status))
        {
            book.Status = BookReadingStatus.Finished;
            BookMetadataApplier.Record(book, BookFields.Status, BookFieldSourceKind.Calibre, calibre.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), now);
            statusChanged = true;
        }

        return changed.Count > 0 || statusChanged;
    }

    private static (Book Book, string Reason)? FindReviewCandidate(
        CalibreBook calibre, string? author, Dictionary<string, List<Book>> byIsbn, Dictionary<string, List<Book>> byTitleAuthor)
    {
        if (!string.IsNullOrWhiteSpace(calibre.Isbn) && byIsbn.TryGetValue(NormalizeIsbn(calibre.Isbn), out var sameIsbn) && sameIsbn.Count == 1)
        {
            return (sameIsbn[0], "Same ISBN");
        }

        string key = CalibreMapper.NormalizeForMatch(calibre.Title) + "|" + CalibreMapper.NormalizeForMatch(author);
        if (byTitleAuthor.TryGetValue(key, out var sameTitle) && sameTitle.Count == 1)
        {
            return (sameTitle[0], "Same title and author");
        }

        return null;
    }

    private static string NormalizeIsbn(string isbn) => new(isbn.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
