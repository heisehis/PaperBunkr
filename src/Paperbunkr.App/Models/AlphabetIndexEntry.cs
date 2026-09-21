using System;
using System.Collections.Generic;
using System.Linq;

namespace Paperbunkr.App.Models;

/// <summary>
/// One letter of the Library's A-Z jump rail (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #26).
/// <see cref="HasItems"/> is false when nothing in the current view starts with that letter, so the rail can dim it. "#" covers
/// every name that does not start with an ASCII letter - the same rule the click handler's letter match uses.
/// </summary>
public sealed record AlphabetIndexEntry(string Letter, bool HasItems)
{
    private static readonly string[] AllLetters =
        Enumerable.Range('A', 26).Select(c => ((char)c).ToString()).Append("#").ToArray();

    /// <summary>The letter bucket a name falls in: "A".."Z", or "#" for digits, symbols and blanks.</summary>
    public static string LetterFor(string? name)
    {
        string trimmed = (name ?? string.Empty).TrimStart();
        char first = trimmed.Length > 0 ? char.ToUpperInvariant(trimmed[0]) : '\0';
        return char.IsAsciiLetter(first) ? first.ToString() : "#";
    }

    /// <summary>The rail letter an arbitrary Library list item belongs to: a group/section header is its own letter, a row or card is the
    /// letter of its series name. Null for anything else. Drives the rail's "current letter" highlight.</summary>
    public static string? LetterForItem(object? item) => item switch
    {
        GridSectionHeader h => h.Header,
        SeriesCardGroup g => g.Header,
        IssueListRowGroup g => g.Header,
        IssueListRow r => LetterFor(r.SeriesName),
        SeriesCardSample c => LetterFor(c.Name),
        string s => LetterFor(s),
        _ => null,
    };

    /// <summary>All 27 entries in rail order, marking which letters occur among <paramref name="names"/>.</summary>
    public static IReadOnlyList<AlphabetIndexEntry> Build(IEnumerable<string?> names)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? name in names)
        {
            present.Add(LetterFor(name));
        }

        return AllLetters.Select(l => new AlphabetIndexEntry(l, present.Contains(l))).ToList();
    }
}
