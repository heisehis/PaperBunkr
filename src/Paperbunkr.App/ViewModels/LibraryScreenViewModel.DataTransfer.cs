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

    /// <summary>
    /// "Commit proposed values" (ComicRack's command of the same name; docs/superpowers/specs/2026-10-11-proposed-values-design.md): for the selected books, writes every
    /// accepted, filename-inferred value that the book's own field does not hold yet into that field. Only fills empty fields, so it asks nothing; it is one undo step and
    /// queues the usual file write-back.
    /// </summary>
    [RelayCommand]
    private void CommitProposedValues(LibraryTarget target)
    {
        var ids = LocalIssuesOnly(IssueIdsOf(target));
        if (ids.Count == 0)
        {
            return;
        }

        // Read what is proposed in its own query: the editing context below must load the books without their proposals, or Effective*() would already read
        // the value and the undo step's before/after snapshots would show no change.
        Dictionary<int, IReadOnlyList<(Paperbunkr.Data.Entities.MetadataProposalField Field, string Value)>> pending;
        using (var context = PaperbunkrDb.CreateContext())
        {
            pending = context.Issues.Include(i => i.MetadataProposals)
                .Where(i => ids.Contains(i.Id))
                .AsEnumerable()
                .Select(i => (i.Id, Pending: ProposedValues.Pending(i, ProposedValues.CommittableFields)))
                .Where(x => x.Pending.Count > 0)
                .ToDictionary(x => x.Id, x => x.Pending);
        }

        if (pending.Count == 0)
        {
            _showToast("Nothing to commit", ids.Count == 1 ? "This book has no proposed value waiting to be saved" : "None of these books has a proposed value waiting to be saved");
            return;
        }

        int values = 0;
        int edited = EditIssuesWithHistory(
            $"Commit proposed values on {Books(pending.Count)}",
            pending.Keys.ToList(),
            issue => values += ProposedValues.Apply(issue, pending[issue.Id]));

        if (edited > 0)
        {
            _showToast("Committed proposed values", $"Saved {values} value{(values == 1 ? "" : "s")} on {Books(edited)} · Ctrl+Z to undo");
        }
    }

    /// <summary>
    /// "Complete count" (ComicRack's Complete Count script; docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §2): the selected books of a <em>completed</em> series get
    /// the series' last issue number as their Count. One undo step; only books whose Count would change are touched.
    /// </summary>
    [RelayCommand]
    private void CompleteSeriesCount(LibraryTarget target)
    {
        var ids = LocalIssuesOnly(IssueIdsOf(target));
        if (ids.Count == 0)
        {
            return;
        }

        // Worked out from a read that includes the proposals (a number may exist only as one); the edit below loads the books without them, so its undo snapshot is right.
        Dictionary<int, int> toChange;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var seriesIds = context.Issues.Where(i => ids.Contains(i.Id)).Select(i => i.SeriesId).Distinct().ToList();
            var library = context.Issues.Include(i => i.Series).Include(i => i.MetadataProposals).Where(i => seriesIds.Contains(i.SeriesId)).ToList();
            var targets = library.Where(i => ids.Contains(i.Id)).ToList();
            toChange = CompleteCount.Compute(library, targets)
                .Where(kv => targets.First(t => t.Id == kv.Key).Count != kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        if (toChange.Count == 0)
        {
            _showToast("Nothing to change", "No selected book is in a completed series that needs its count set");
            return;
        }

        int edited = EditIssuesWithHistory(
            $"Complete the count of {Books(toChange.Count)}",
            toChange.Keys.ToList(),
            issue => issue.Count = toChange[issue.Id]);

        if (edited > 0)
        {
            _showToast("Completed the count", $"Set the count of {Books(edited)} from their series' last issue · Ctrl+Z to undo");
        }
    }

    /// <summary>
    /// "First page is an ad" (ComicRack's Remove Scanner Credits / Move First Page to End; docs/superpowers/specs/2026-10-11-series-checks-and-bulk-fixes-design.md §3): page 1 is tagged an
    /// advertisement and page 2 the cover. Page tags are not part of the metadata undo history, hence the confirmation.
    /// </summary>
    [RelayCommand]
    private async Task MarkFirstPageAsAd(LibraryTarget target)
    {
        var ids = LocalIssuesOnly(IssueIdsOf(target));
        if (ids.Count == 0 || _dialogs is null)
        {
            return;
        }

        string message = $"Tag the first page of {Books(ids.Count)} as an ad and the second as the cover? The reader will skip the first page. Books with fewer than two known pages are skipped. This is not part of Undo; page tags can be changed again in the reader.";
        if (!await _dialogs.ConfirmAsync(message, title: "First page is an ad", confirmLabel: "Tag pages"))
        {
            return;
        }

        int tagged = 0;
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (int id in ids)
            {
                if (FirstPageAd.Apply(context, id))
                {
                    tagged++;
                }
            }
        }

        _showToast(tagged == 0 ? "Nothing tagged" : "Tagged first pages", tagged == 0 ? "None of these books has two known pages" : $"Tagged the first two pages of {Books(tagged)}");
    }

    /// <summary>
    /// "Find scanner from file name" (ComicRack's Scan Information From Filename script; docs/superpowers/specs/2026-10-11-scan-credits-design.md): the scan or release group named
    /// in each selected book's file name is added to its Scan Information. One undo step; a book whose name has no credit, or that already lists it, is left alone.
    /// </summary>
    [RelayCommand]
    private void FindScannerFromFileName(LibraryTarget target)
    {
        var ids = LocalIssuesOnly(IssueIdsOf(target));
        if (ids.Count == 0)
        {
            return;
        }

        var catalog = Paperbunkr.Data.Metadata.ScanCredits.ScanCreditCatalog.Create(Paperbunkr.Data.Metadata.ScanCredits.ScanCreditSettings.Load());
        var updates = new Dictionary<int, string>();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var issue in context.Issues.Include(i => i.Series).Include(i => i.Tags).Include(i => i.MetadataProposals).Where(i => ids.Contains(i.Id)).ToList())
            {
                if (Paperbunkr.Data.Metadata.ScanCredits.ScanCreditDetector.Detect(issue, catalog) is { } credit
                    && Paperbunkr.Data.Metadata.ScanCredits.ScanCreditDetector.Merge(issue.ScanInformation, credit) is { } merged)
                {
                    updates[issue.Id] = merged;
                }
            }
        }

        if (updates.Count == 0)
        {
            _showToast("No scanner found", ids.Count == 1 ? "This book's file name names no scanner it doesn't already list" : "None of these file names names a scanner the book doesn't already list");
            return;
        }

        int edited = EditIssuesWithHistory(
            $"Find scanner from file name on {Books(updates.Count)}",
            updates.Keys.ToList(),
            issue => issue.ScanInformation = updates[issue.Id]);

        if (edited > 0)
        {
            _showToast("Scanner found", $"Added the scanner to the Scan Information of {Books(edited)} · Ctrl+Z to undo");
        }
    }

    /// <summary>
    /// "Guess format from name" (docs/superpowers/specs/2026-10-11-format-presets-design.md): a selected book with no format gets one when its series name, title or file name
    /// says it (Annual, Director's Cut, TPB, One-Shot, Black &amp; White). One undo step; books that already have a format are left alone.
    /// </summary>
    [RelayCommand]
    private void GuessFormatFromName(LibraryTarget target)
    {
        var ids = LocalIssuesOnly(IssueIdsOf(target));
        if (ids.Count == 0)
        {
            return;
        }

        // Read with the proposals (a proposed format counts as "already has one"); the edit below loads the books without them so its undo snapshot is right.
        var updates = new Dictionary<int, string>();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var issue in context.Issues.Include(i => i.Series).Include(i => i.MetadataProposals).Where(i => ids.Contains(i.Id)).ToList())
            {
                if (FormatGuesser.Guess(issue) is { } format)
                {
                    updates[issue.Id] = format;
                }
            }
        }

        if (updates.Count == 0)
        {
            _showToast("No format found", "None of these books is missing a format that its name gives away");
            return;
        }

        int edited = EditIssuesWithHistory(
            $"Guess the format of {Books(updates.Count)}",
            updates.Keys.ToList(),
            issue => issue.Format = updates[issue.Id]);

        if (edited > 0)
        {
            _showToast("Format set", $"Set the format of {Books(edited)} from their names · Ctrl+Z to undo");
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
