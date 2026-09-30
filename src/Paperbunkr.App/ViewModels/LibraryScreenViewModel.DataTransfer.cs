using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels.LibraryActions;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Copy Data / Paste Data… / Clear Data… (CE's <c>CopyComicData</c>, <c>PasteComicData</c>, <c>ClearComicData</c>,
/// <c>ComicBrowserControl.cs:2808-2866</c>; docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §2/§3). Unlike CE, Paste is one
/// undo step too.
/// </summary>
public partial class LibraryScreenViewModel
{
    private MetadataClipboardService _metadataClipboard = MetadataClipboardService.Shared;
    private Action<IReadOnlyList<int>, MetadataClipboardContent> _openPasteData = (_, _) => { };
    private IDialogService? _dialogs;

    /// <summary>The Copy Data clipboard - shared with the single-book editor. A test can swap it.</summary>
    internal MetadataClipboardService MetadataClipboard
    {
        get => _metadataClipboard;
        set => _metadataClipboard = value;
    }

    /// <summary>Opens the Paste Data dialog - set by the shell.</summary>
    internal Action<IReadOnlyList<int>, MetadataClipboardContent> OpenPasteData
    {
        get => _openPasteData;
        set => _openPasteData = value;
    }

    /// <summary>Confirmations for Clear Data and Re-read - set by the shell; without it those actions don't run.</summary>
    internal IDialogService? Dialogs
    {
        get => _dialogs;
        set => _dialogs = value;
    }

    public bool HasMetadataClipboard => _metadataClipboard.HasContent;

    /// <summary>"Copy Data" (Ctrl+C): the first targeted book's data (CE copies the first selected book).</summary>
    [RelayCommand]
    private void CopyData(LibraryTarget target)
    {
        var ids = IssueIdsOf(target);
        if (ids.Count == 0)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext(includeRemote: true);
        var issue = context.Issues.Include(i => i.Series).Include(i => i.Tags).FirstOrDefault(i => i.Id == ids[0]);
        if (issue is null)
        {
            return;
        }

        string caption = $"{issue.Series?.Name ?? "Unknown Series"} #{issue.EffectiveNumber()}";
        var fields = BulkFieldRegistry.IssueOwned.ToDictionary(f => f.Label, f => f.Get(issue));
        _metadataClipboard.Set(new MetadataClipboardContent(caption, fields));
        OnPropertyChanged(nameof(HasMetadataClipboard));
        _showToast("Copied data", $"Copied data from {caption}.");
    }

    /// <summary>"Paste Data…" (Ctrl+V): opens the field picker for the targeted local books.</summary>
    [RelayCommand]
    private void PasteData(LibraryTarget target)
    {
        if (_metadataClipboard.Content is not { } content)
        {
            return;
        }

        var ids = LocalIssuesOnly(IssueIdsOf(target));
        if (ids.Count > 0)
        {
            _openPasteData(ids, content);
        }
    }

    /// <summary>The Paste Data dialog's result: every ticked field onto every book, a ticked empty value clearing it, list fields replaced
    /// whole (CE's <c>CopyDataFrom</c>; Genre/Tags go through <c>MergeFrom</c>, which diffs against the new set - a replace that keeps
    /// Category/Weight for values that survive) - as one undo step.</summary>
    internal void ApplyPastedData(IReadOnlyList<int> issueIds, IReadOnlyDictionary<string, string?> fields)
    {
        var descriptors = BulkFieldRegistry.IssueOwned.Where(f => fields.ContainsKey(f.Label)).ToList();
        if (descriptors.Count == 0)
        {
            return;
        }

        int edited = EditIssuesWithHistory(
            $"Paste data onto {Books(issueIds.Count)}",
            issueIds,
            issue =>
            {
                foreach (var field in descriptors)
                {
                    field.Set(issue, fields[field.Label]);
                }
            });

        if (edited > 0)
        {
            string what = descriptors.Count == 1 ? "1 field" : $"{descriptors.Count} fields";
            _showToast("Pasted data", $"Pasted {what} onto {Books(edited)} · Ctrl+Z to undo");
        }
    }

    /// <summary>"Clear Data…": after CE's confirmation, every book-owned field back to empty - read progress, files, pages, bookmarks, the
    /// cover and the series stay (CE keeps read state and file info too; it also zeroes PageCount, which Paperbunkr derives from the file).</summary>
    [RelayCommand]
    private async Task ClearData(LibraryTarget target)
    {
        var ids = LocalIssuesOnly(IssueIdsOf(target));
        if (ids.Count == 0 || _dialogs is null)
        {
            return;
        }

        string message = $"Remove all entered data from {Books(ids.Count)}? Read progress, files and covers are kept. This can be reverted with Undo.";
        if (!await _dialogs.ConfirmAsync(message, title: "Clear data", confirmLabel: "Clear", isDestructive: true))
        {
            return;
        }

        var fields = BulkFieldRegistry.IssueOwned.ToList();
        int edited = EditIssuesWithHistory($"Clear data on {Books(ids.Count)}", ids, issue =>
        {
            foreach (var field in fields)
            {
                field.Set(issue, null);
            }
        });

        if (edited > 0)
        {
            _showToast("Cleared data", $"Cleared the data of {Books(edited)} · Ctrl+Z to undo");
        }
    }
}
