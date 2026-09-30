using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels.LibraryActions;
using Paperbunkr.Data;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// "My Rating ▸" (CE's <c>RatingEditor.SetRating</c>, <c>MainForm.cs:133-143</c>) and the shared undoable-edit helper that Paste Data, Clear
/// Data and Re-read also use (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §2).
/// </summary>
public partial class LibraryScreenViewModel
{
    private MetadataEditHistoryService _history = MetadataEditHistoryService.Shared;

    /// <summary>Undo/redo stack the bulk metadata actions record into - <see cref="MetadataEditHistoryService.Shared"/> unless a test swaps it.</summary>
    internal MetadataEditHistoryService History
    {
        get => _history;
        set => _history = value;
    }

    /// <summary>
    /// Sets the same whole-star rating on every targeted local book, whatever each one had (CE parity), as one undo step. Per book, like
    /// Quick Rate: the file write-back is queued when the rating actually changes what goes into the file, and a <c>RatingChanged</c>
    /// activity entry is logged. <c>Stars</c> null clears the rating.
    /// </summary>
    [RelayCommand]
    private void SetRating((LibraryTarget Target, int? Stars) args)
    {
        float? value = args.Stars is int s ? Math.Clamp(s, 0, 5) : null;
        var changed = new List<(int IssueId, int SeriesId)>();
        int edited = EditIssuesWithHistory(
            args.Stars is int n ? $"Rate {Books(args.Target.Count)} {n}★" : $"Clear rating on {Books(args.Target.Count)}",
            IssueIdsOf(args.Target),
            issue =>
            {
                if (issue.Rating != value)
                {
                    issue.Rating = value;
                    changed.Add((issue.Id, issue.SeriesId));
                }
            });

        if (changed.Count > 0)
        {
            using var context = PaperbunkrDb.CreateContext();
            string detail = value is float r ? $"Rating set to {(int)r}" : "Rating cleared";
            foreach (var (issueId, seriesId) in changed)
            {
                SeriesActivityLog.Record(context, seriesId, SeriesActivityEventKind.RatingChanged, detail, issueId);
            }

            context.SaveChanges();
        }

        if (edited > 1)
        {
            _showToast("Rating set", args.Stars is int stars
                ? $"Rated {edited} books {new string('★', stars)} · Ctrl+Z to undo"
                : $"Cleared the rating on {edited} books · Ctrl+Z to undo");
        }
    }

    /// <summary>The rating every row in <paramref name="issueIds"/> shares (0 = unrated), or null when they differ - drives the ✓.</summary>
    internal int? CommonRating(IReadOnlyList<int> issueIds)
    {
        var rows = VisibleIssueRows.Where(r => issueIds.Contains(r.Id)).ToList();
        if (rows.Count == 0)
        {
            return null;
        }

        var values = rows.Select(r => r.Rating is float f ? (int)f : 0).Distinct().ToList();
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>
    /// Applies <paramref name="apply"/> to every local issue in <paramref name="issueIds"/> as one undo step: history snapshot before, save,
    /// snapshot after, one <see cref="MetadataEditHistoryService.Record"/>; then queues the file write-back for each book whose file-bound
    /// fields changed, and refreshes the Library. Returns the number of books edited. Remote books are skipped (with the usual toast).
    /// </summary>
    internal int EditIssuesWithHistory(string description, IReadOnlyList<int> issueIds, Action<Issue> apply)
    {
        var local = LocalIssuesOnly(issueIds);
        if (local.Count == 0)
        {
            return 0;
        }

        var before = new Dictionary<int, Dictionary<string, string?>>();
        var after = new Dictionary<int, Dictionary<string, string?>>();
        var writeBack = new List<int>();
        using (var context = PaperbunkrDb.CreateContext())
        {
            var issues = context.Issues.Include(i => i.Series).Include(i => i.Tags).Where(i => local.Contains(i.Id)).ToList();
            var fileBefore = new Dictionary<int, MetadataFileFieldSnapshot>();
            foreach (var issue in issues)
            {
                before[issue.Id] = MetadataEditHistoryService.CaptureSnapshot(issue);
                fileBefore[issue.Id] = MetadataFileFieldSnapshot.Capture(issue);
                apply(issue);
            }

            context.SaveChanges();

            foreach (var issue in issues)
            {
                after[issue.Id] = MetadataEditHistoryService.CaptureSnapshot(issue);
                if (MetadataFileFieldSnapshot.Differ(fileBefore[issue.Id], MetadataFileFieldSnapshot.Capture(issue)))
                {
                    writeBack.Add(issue.Id);
                }
            }
        }

        if (before.Count == 0)
        {
            return 0;
        }

        _history.Record(description, before, after);
        foreach (int id in writeBack)
        {
            _enqueueMetadataWriteBack(id, false);
        }

        // Deferred: these commands run from a menu item or a bar flyout still routing its click (CLAUDE.md's detach-while-routing rule).
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadFromDatabase());
        return before.Count;
    }

    private static string Books(int count) => count == 1 ? "1 book" : $"{count} books";
}
