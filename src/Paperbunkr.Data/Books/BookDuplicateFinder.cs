using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Books;

public sealed record BookDuplicateEntry(int BookId, string Title, string? Author, BookFormat Format, string FilePath);

/// <param name="Reason">"Same ISBN" or "Same title and author".</param>
/// <param name="MixedFormats">True when the copies are different formats (an EPUB and a PDF of the same book); false for two files of one format.</param>
public sealed record BookDuplicateGroup(string Label, string Reason, bool MixedFormats, IReadOnlyList<BookDuplicateEntry> Entries);

/// <summary>
/// Finds the same book held more than once (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §8): equal ISBN first, then equal
/// normalised title and author (<see cref="TitleNormalizer.StripDown"/>: case, punctuation and "the"/"and" ignored). Report-only; a book belongs to at
/// most one group, and an ISBN match wins over a title match.
/// </summary>
public static class BookDuplicateFinder
{
    public static IReadOnlyList<BookDuplicateGroup> Find(IEnumerable<Book> books)
    {
        var list = books.ToList();
        var used = new HashSet<int>();
        var groups = new List<BookDuplicateGroup>();

        foreach (var byIsbn in list.Where(b => !string.IsNullOrWhiteSpace(b.ISBN)).GroupBy(b => Isbn(b.ISBN!)).Where(g => g.Count() > 1))
        {
            groups.Add(ToGroup(byIsbn.ToList(), "Same ISBN"));
            used.UnionWith(byIsbn.Select(b => b.Id));
        }

        var rest = list.Where(b => !used.Contains(b.Id) && TitleKey(b.Title).Length > 0);
        foreach (var byName in rest.GroupBy(b => TitleKey(b.Title) + "\u001f" + TitleKey(b.Author ?? string.Empty)).Where(g => g.Count() > 1))
        {
            groups.Add(ToGroup(byName.ToList(), "Same title and author"));
        }

        return groups.OrderBy(g => g.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Isbn(string isbn) => new string(isbn.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string TitleKey(string text) => TitleNormalizer.StripDown(text).Trim().ToLowerInvariant();

    private static BookDuplicateGroup ToGroup(List<Book> books, string reason)
    {
        var entries = books
            .OrderBy(b => b.Format)
            .ThenBy(b => b.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(b => new BookDuplicateEntry(b.Id, b.Title, b.Author, b.Format, b.FilePath))
            .ToList();
        string label = string.IsNullOrWhiteSpace(books[0].Author) ? books[0].Title : $"{books[0].Title} — {books[0].Author}";
        return new BookDuplicateGroup(label, reason, entries.Select(e => e.Format).Distinct().Count() > 1, entries);
    }
}
