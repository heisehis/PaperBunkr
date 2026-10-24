using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Matching;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The list page's ⋯ Manage actions (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §7, decision Q16): imports and
/// exports, role detection, arc refresh / follow / request missing, and dropped-file import. Ported unchanged from the previous screen.
/// </summary>
public partial class ReadingListPageViewModel
{
    /// <summary>The "Detect roles" action: runs role detection over every item of the open list. Roles it is sure of fill items that have
    /// no role yet (marked automatic); a role you set is never changed - a differing guess is offered as a suggestion instead. Reported
    /// through the Activity Center.</summary>
    [RelayCommand]
    private void DetectRoles()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var job = _activity.StartJob(ActivityJobKind.Other, "Detecting roles in this list");
        string message;
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            var summary = MemberRoleDetection.DetectForList(context, listId);
            message = summary.ToString();
            job.Succeed(message, itemsProcessed: summary.Applied + summary.Suggested);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Role detection failed: {ex.Message}";
            job.Fail(StatusMessage, ex: ex);
            return;
        }

        // Reload after the click has finished routing (the rows being rebuilt include the control that raised it); the reload clears the
        // status line, so the result is shown once it is done.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoadReadingList(listId);
            StatusMessage = message;
        });
    }

    [RelayCommand]
    private async Task RefreshArcList()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        try
        {
            var result = await ArcReadingListBuilder.RefreshAsync(context, listId, CancellationToken.None);
            LoadReadingList(listId);
            StatusMessage = WithDetails(
                $"Added {result.AddedCount}, replaced {result.ReplacedPlaceholderCount} placeholder(s), {result.StillMissingCount} still missing.", result.Report);
        }
        catch (Exception ex) when (ex is ReadingListSourceException or InvalidOperationException)
        {
            StatusMessage = ex.Message;
        }
    }

    /// <summary>
    /// "Recheck library" (docs/superpowers/specs/2026-10-13-cbl-smart-match-core-design.md §5.9, decision Q8): looks again for the comics
    /// this list's "missing" entries are waiting for. Works offline and on any list - a .cbl import has no source to refresh from.
    /// </summary>
    [RelayCommand]
    private void RecheckLibrary()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        string message;
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            var healed = ReadingListHealer.RecheckList(context, listId);
            message = healed.Relinked + healed.NeedChoice + healed.StillMissing + healed.AlreadyInList.Count == 0
                ? "Nothing in this list is waiting for a comic."
                : healed.Summary;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            StatusMessage = $"Recheck failed: {ex.Message}";
            return;
        }

        // Reload after the click has finished routing (the rows being rebuilt include the control that raised it).
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LoadReadingList(listId);
            StatusMessage = message;
        });
    }

    /// <summary>The counts line plus, when the source could not deliver everything, the names behind "duplicate" and "skipped".</summary>
    private static string WithDetails(string summary, ImportReport? report)
    {
        if (report is null)
        {
            return summary;
        }

        string text = summary;
        if (report.NeedsChoice > 0)
        {
            text += report.NeedsChoice == 1 ? " 1 entry needs a choice." : $" {report.NeedsChoice} entries need a choice.";
        }

        if (report.Incomplete)
        {
            text += $" Incomplete: the source lists {report.Reported} entries and {report.Parsed + report.Skipped.Count} were received.";
        }

        return report.Details() is { } details ? text + Environment.NewLine + details : text;
    }

    private bool _loadingFollowArc;

    /// <summary>
    /// "Follow this arc": a scheduled task keeps the list in step with its source and requests newly listed issues you don't have
    /// (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md 8). Only meaningful on arc-linked lists.
    /// </summary>
    [ObservableProperty]
    private bool _followArc;

    partial void OnFollowArcChanged(bool value)
    {
        if (_loadingFollowArc || _activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.FirstOrDefault(l => l.Id == listId);
        if (list is null || string.IsNullOrEmpty(list.Source))
        {
            return;
        }

        list.FollowArc = value;
        context.SaveChanges();
        StatusMessage = value
            ? "Following this arc: new issues are checked for daily (turn on \"Follow story arcs\" under Preferences → Automation)."
            : "No longer following this arc.";
    }

    /// <summary>Test seam: the ComicVine client used for "Request missing". Production uses the shared, rate-limited one at foreground priority.</summary>
    internal Func<string, IComicVineClient> CreateComicVine { get; set; } = key => new ComicVineClient(key, ComicVineRequestPriority.High);

    /// <summary>Test seam: the Metron client, for a series that was tracked on Metron. Production builds it from the login saved under Connections.</summary>
    internal Func<IComicVineClient?> CreateMetron { get; set; } = () =>
    {
        using var context = PaperbunkrDb.CreateContext();
        return ComicProviderFactory.Create(context, ComicProvider.Metron, ComicVineRequestPriority.High);
    };

    /// <summary>
    /// "Request missing issues" (docs/superpowers/specs/2026-09-19-comic-acquisition-daemon-design.md 8): turns this arc-linked list's
    /// placeholders into wanted issues. Runs as an Activity Center job because matching each series to ComicVine takes a few rate-limited requests.
    /// </summary>
    [RelayCommand]
    private async Task RequestMissingFromArc()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        if (!IsArcLinked)
        {
            StatusMessage = "Bulk requests are only available on lists built from a story arc. Use Request on individual missing issues instead.";
            return;
        }

        await RequestAsync(listId, "Requesting missing issues", (context, client, clientFor, ct) => ArcRequestService.RequestMissingAsync(context, listId, client, ct, clientFor));
    }

    /// <summary>Per-item Request on a missing (placeholder) row of any list.</summary>
    private void RequestItem(ReadingListItemRowViewModel row)
    {
        if (_activeReadingListId is not int listId || row.Item.Issue is not { IsPlaceholder: true } issue)
        {
            return;
        }

        _ = RequestAsync(listId, $"Requesting {issue.Series?.Name} #{issue.Number}", (context, client, clientFor, ct) =>
            ArcRequestService.RequestPlaceholdersAsync(context, new[] { issue }, client, ct, clientFor));
    }

    private async Task RequestAsync(int listId, string jobTitle, Func<PaperbunkrDbContext, IComicVineClient?, Func<ComicProvider, IComicVineClient?>, CancellationToken, Task<ArcRequestResult>> request)
    {
        string? key;
        using (var context = PaperbunkrDb.CreateContext())
        {
            key = CredentialStore.Get(context, "ComicVine", CredentialKind.ApiKey);
        }

        var metron = CreateMetron();
        if (string.IsNullOrEmpty(key) && metron is null)
        {
            StatusMessage = "Requesting needs your ComicVine API key - add it under Preferences → Connections.";
            return;
        }

        var comicVine = string.IsNullOrEmpty(key) ? null : CreateComicVine(key);
        IComicVineClient? ClientFor(ComicProvider provider) => provider == ComicProvider.Metron ? metron : comicVine;

        using var job = _activity.StartJob(ActivityJobKind.Acquisition, jobTitle);
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            var result = await request(context, comicVine, ClientFor, job.CancellationToken);

            var summary = DescribeRequestResult(result);
            StatusMessage = summary;
            job.Succeed(summary, new ActivityLink(ActivityLinkKind.WantedScreen), itemsProcessed: result.Requested, itemsFailed: result.Unresolved.Count);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
        catch (ComicVineException ex)
        {
            StatusMessage = ex.Message;
            job.Fail(ex.Message);
        }
    }

    /// <summary>One line of totals, then up to three of the entries that couldn't be matched (never silently dropped).</summary>
    internal static string DescribeRequestResult(ArcRequestResult result)
    {
        var parts = new List<string> { result.Requested == 1 ? "Requested 1 issue" : $"Requested {result.Requested} issues" };
        if (result.AlreadyTracked > 0)
        {
            parts.Add($"{result.AlreadyTracked} already requested");
        }

        if (result.Unresolved.Count > 0)
        {
            parts.Add($"{result.Unresolved.Count} couldn't be matched");
        }

        var text = string.Join(", ", parts) + ".";
        if (result.Unresolved.Count > 0)
        {
            text += string.Concat(result.Unresolved.Take(3).Select(u => $"{Environment.NewLine}{u.Series} #{u.Number}: {u.Reason}"));
            if (result.Unresolved.Count > 3)
            {
                text += $"{Environment.NewLine}…and {result.Unresolved.Count - 3} more.";
            }
        }

        return text;
    }


    [RelayCommand]
    private async Task ImportCbl()
    {
        string? path = await _filePicker.PickOpenFileAsync("Import CBL Reading List", "cbl", "CBL Reading List");
        if (path is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        CblImportResult imported = CblImport.ImportAny(context, path);
        if (imported.Kind == CblFileKind.SmartList)
        {
            // A ComicRack smart list: it becomes a Smart List, not a reading list, so there is nothing to place or open here.
            var notes = imported.Warnings.Select(w => w.ToString()).ToList();
            if (imported.Refused)
            {
                notes.Add(imported.RefusedReason!);
            }

            CeImportAlerts.RaiseIfAny(_activity, notes, "smart list");
            StatusMessage = imported.Refused
                ? imported.RefusedReason
                : $"That file is a ComicRack smart list. Imported it as the smart list '{imported.SmartList!.Name}' - find it on the Smart screen.";
            return;
        }

        var list = imported.ReadingList!;
        PlaceInThisFolder(list.Id);
        _openList(list.Id);
        StatusMessage = imported.Report is { } report
            ? $"Imported '{list.Name}': {report.Summary}" + (report.Details() is { } details ? Environment.NewLine + details : string.Empty)
            : $"Imported '{list.Name}' with {list.Items.Count} issues.";
    }

    [RelayCommand]
    private async Task ImportCsv()
    {
        string? path = await _filePicker.PickOpenFileAsync("Import CSV Reading List", "csv", "CSV Reading List");
        if (path is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var result = CsvReadingListIO.Import(context, path);
        PlaceInThisFolder(result.List.Id);
        _openList(result.List.Id);
        StatusMessage = result.Report is { } report
            ? $"Imported '{result.List.Name}': {report.Summary}" + (report.Details() is { } details ? Environment.NewLine + details : string.Empty)
            : $"Imported '{result.List.Name}': {result.OwnedCount} owned, {result.PlaceholderCount} missing.";
    }

    [RelayCommand]
    private async Task ExportCbl()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        string? path = await _filePicker.PickSaveFileAsync("Export CBL Reading List", $"{ListName}.cbl", "cbl", "CBL Reading List");
        if (path is null)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        CblReadingListIO.Export(context, listId, path);
        StatusMessage = $"Exported to {path}.";
    }

    [RelayCommand]
    private async Task ExportAsText()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        string text = ReadingListTextExporter.Export(context, listId);

        string? path = await _filePicker.PickSaveFileAsync("Export Reading List as Text", $"{ListName}.txt", "txt", "Text File");
        if (path is not null)
        {
            await System.IO.File.WriteAllTextAsync(path, text);
            StatusMessage = $"Exported to {path}.";
        }
    }

    [RelayCommand]
    private async Task CopyAsText()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        string text = ReadingListTextExporter.Export(context, listId);
        await _filePicker.SetClipboardTextAsync(text);
        StatusMessage = "Copied to clipboard.";
    }

    /// <summary>Export by format name - the gallery tile's Export ▸ runs this after opening the list.</summary>
    public Task ExportAs(string format) => format switch
    {
        "cbl" => ExportCbl(),
        "csv" => ExportCsv(),
        "text" => ExportAsText(),
        "pdf" => ExportChecklist(),
        _ => Task.CompletedTask,
    };

    /// <summary>A list created from this page (import, story arc) lands in the open list's folder (decision Q8).</summary>
    private void PlaceInThisFolder(int listId)
    {
        using var context = PaperbunkrDb.CreateContext();
        if (context.ReadingLists.Find(listId) is { } list)
        {
            ReadingListFolders.PlaceNewList(context, list, FolderId);
            context.SaveChanges();
        }
    }
}
