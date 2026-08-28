using System.Collections.Generic;
using System.IO;
using System.Linq;
using cYo.Common.Win32;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Services;

/// <summary>
/// Wraps CE's already-ported <see cref="FileExplorer"/> shell helper (previously zero call sites
/// anywhere in the app) with the folder-resolution rules for each of Paperbunkr's three reveal
/// surfaces (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md
/// §1) - kept out of the ViewModels so the resolution logic isn't duplicated three times.
///
/// Folder-resolution (pure, unit-testable) is split from the actual P/Invoke shell calls
/// (<see cref="FileExplorer"/>, untestable without touching the real shell) so the dedup/ordering
/// logic itself has real coverage.
/// </summary>
public static class RevealInExplorerHelper
{
    /// <summary>Selects the exact file in Explorer - CE's own per-ComicBook behavior.</summary>
    public static bool RevealIssue(Issue issue)
    {
        string? path = ResolveIssueFilePath(issue);
        return path is not null && FileExplorer.OpenFolderAndSelect(path);
    }

    /// <summary>
    /// More than one file may be involved, so there's no single file to select - dedupes to unique
    /// containing folders and opens each once.
    /// </summary>
    public static void RevealIssues(IEnumerable<Issue> issues)
    {
        foreach (var folder in ResolveUniqueFolders(issues))
        {
            FileExplorer.OpenFolder(folder);
        }
    }

    /// <summary>
    /// A series has no single file, so this opens (doesn't select) the containing folder of its
    /// first issue by number - the closest analog to "reveal this series" at series granularity.
    /// </summary>
    public static bool RevealSeries(Series series)
    {
        string? folder = ResolveSeriesFolder(series);
        return folder is not null && FileExplorer.OpenFolder(folder);
    }

    /// <summary>Selects the book's file in Explorer (docs/superpowers/specs/2026-08-27-book-details-
    /// screen-design.md) - a <see cref="Book"/> is a single file like an <see cref="Issue"/>, so this
    /// mirrors <see cref="RevealIssue"/> exactly.</summary>
    public static bool RevealBook(Book book)
    {
        string? path = ResolveBookFilePath(book);
        return path is not null && FileExplorer.OpenFolderAndSelect(path);
    }

    /// <summary>Pure - returns null if the issue has no file, otherwise its own <see cref="Issue.FilePath"/> unchanged (no folder extraction; OpenFolderAndSelect wants the file itself).</summary>
    internal static string? ResolveIssueFilePath(Issue issue) =>
        string.IsNullOrEmpty(issue.FilePath) ? null : issue.FilePath;

    /// <summary>Pure - the book's own <see cref="Book.FilePath"/>, or null when it's empty. Same shape as <see cref="ResolveIssueFilePath"/>.</summary>
    internal static string? ResolveBookFilePath(Book book) =>
        string.IsNullOrEmpty(book.FilePath) ? null : book.FilePath;

    /// <summary>Pure - unique, non-empty containing folders across every issue with a file, in encounter order.</summary>
    internal static IReadOnlyList<string> ResolveUniqueFolders(IEnumerable<Issue> issues) =>
        issues
            .Where(i => !string.IsNullOrEmpty(i.FilePath))
            .Select(i => Path.GetDirectoryName(i.FilePath))
            .Where(f => !string.IsNullOrEmpty(f))
            .Distinct()
            .Select(f => f!)
            .ToList();

    /// <summary>Pure - the containing folder of the series' first issue (by number) that has a file, or null if none do.</summary>
    internal static string? ResolveSeriesFolder(Series series)
    {
        var firstWithFile = series.Issues
            .OrderByNumber()
            .FirstOrDefault(i => !string.IsNullOrEmpty(i.FilePath));

        if (firstWithFile is null)
        {
            return null;
        }

        string? folder = Path.GetDirectoryName(firstWithFile.FilePath);
        return string.IsNullOrEmpty(folder) ? null : folder;
    }
}
