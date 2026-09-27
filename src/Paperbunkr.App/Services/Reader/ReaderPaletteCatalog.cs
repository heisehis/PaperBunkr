using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Paperbunkr.App.Services.Reader;

/// <summary>One thing the reader command palette can do (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md section 5).</summary>
/// <param name="Title">What the row says ("Toggle fullscreen").</param>
/// <param name="Group">Faint category label ("View", "Reading", "Page", "Navigate").</param>
/// <param name="Shortcut">The command's current shortcut for display, or <see langword="null"/>.</param>
/// <param name="Run">What choosing it does. The palette closes first and runs it one dispatcher tick later.</param>
public sealed record ReaderPaletteEntry(string Title, string Group, string? Shortcut, Action Run);

/// <summary>Pure ranking and page-number parsing for the reader command palette. Matching reuses <see cref="QuickOpenMatcher.Score"/>.</summary>
public static class ReaderPaletteCatalog
{
    /// <summary>Most rows the palette shows for a typed query.</summary>
    public const int MaxResults = 12;

    /// <summary>Most rows shown before anything is typed (the catalog order is the curated one).</summary>
    public const int MaxUntyped = 30;

    private static readonly Regex PageNumber = new(@"^\s*(?:go\s*to\s*)?(?:page|pg|p|#)?\s*(\d{1,6})\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// A bare number, or one written as "page 40", "p40", "pg 40", "#40" or "go to 40", is a request for that (1-based) page. Anything else, including a number
    /// with other words after it, is not.
    /// </summary>
    public static bool TryParsePageNumber(string? query, out int page)
    {
        page = 0;
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var match = PageNumber.Match(query);
        return match.Success && int.TryParse(match.Groups[1].Value, out page) && page >= 0;
    }

    /// <summary>
    /// Entries matching <paramref name="query"/>, best first. An empty query lists the catalog in its own order. Each entry is scored on its title and on
    /// "group title" (so "view fullscreen" finds it); ties fall back to the shorter title, then the catalog order.
    /// </summary>
    public static IReadOnlyList<ReaderPaletteEntry> Rank(IReadOnlyList<ReaderPaletteEntry> entries, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return entries.Take(MaxUntyped).ToList();
        }

        string trimmed = query.Trim();
        var scored = new List<(ReaderPaletteEntry Entry, int Score, int Order)>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            int? title = QuickOpenMatcher.Score(trimmed, entry.Title);
            int? grouped = QuickOpenMatcher.Score(trimmed, $"{entry.Group} {entry.Title}");
            int? best = title is null ? grouped : grouped is null ? title : Math.Max(title.Value, grouped.Value);
            if (best is { } score)
            {
                scored.Add((entry, score, i));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Entry.Title.Length)
            .ThenBy(s => s.Order)
            .Take(MaxResults)
            .Select(s => s.Entry)
            .ToList();
    }
}
