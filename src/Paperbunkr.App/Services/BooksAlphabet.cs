using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// The Books A-Z jump rail's logic (docs/superpowers/specs/2026-10-27-books-slice2a-browse-shelves-design.md §7), sharing the Library rail's
/// letter rule (<see cref="AlphabetIndexEntry.LetterFor"/>: skips a leading article, "#" for digits and symbols). Unlike the Library, the Books grid is a plain
/// wrapping panel whose containers all exist, so a letter jumps to the real item instead of an estimated offset.
/// </summary>
public static class BooksAlphabet
{
    /// <summary>The text a book is filed under for the current sort: its author under an Author sort, otherwise its title.</summary>
    public static string? KeyFor(BookCardSample card, BooksSortField sort) =>
        sort == BooksSortField.Author ? card.Author : card.Title;

    /// <summary>The rail applies to a flat list sorted by Title or Author; grouped, it indexes the group headers.</summary>
    public static bool Applies(bool grouped, BooksGroupField group, BooksSortField sort) =>
        grouped ? group != BooksGroupField.Status : sort is BooksSortField.Title or BooksSortField.Author;

    public static IReadOnlyList<AlphabetIndexEntry> Build(IEnumerable<BookCardSample> flat, IEnumerable<BookCardGroup> groups, bool grouped, BooksSortField sort) =>
        AlphabetIndexEntry.Build(grouped ? groups.Select(g => g.Header) : flat.Select(c => KeyFor(c, sort)));

    /// <summary>Index of the first book filed under <paramref name="letter"/>, or -1.</summary>
    public static int FirstBookIndex(IReadOnlyList<BookCardSample> flat, BooksSortField sort, string letter)
    {
        for (int i = 0; i < flat.Count; i++)
        {
            if (AlphabetIndexEntry.LetterFor(KeyFor(flat[i], sort)) == letter)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Index of the first group whose header falls under <paramref name="letter"/>, or -1.</summary>
    public static int FirstGroupIndex(IReadOnlyList<BookCardGroup> groups, string letter)
    {
        for (int i = 0; i < groups.Count; i++)
        {
            if (AlphabetIndexEntry.LetterFor(groups[i].Header) == letter)
            {
                return i;
            }
        }

        return -1;
    }
}
