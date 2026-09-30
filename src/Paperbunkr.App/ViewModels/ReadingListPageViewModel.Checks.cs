using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The list page's forecast, Checks strip (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §6, decision K1), exports, and
/// the continuity Rebuild and canonical check. Ported from the previous screen's tracking partial: the completion forecast, overlap banner with compare/merge, checklist and CSV export
/// (docs/superpowers/specs/2026-09-28-reading-lists-organize-and-track-design.md §5-§7), and a continuity list's Rebuild plus the check
/// against ComicVine/Metron (docs/superpowers/specs/2026-09-28-reading-lists-build-from-events-design.md §2-§3).
/// </summary>
public partial class ReadingListPageViewModel
{
    // --- Forecast (§5) ---

    private IReadOnlyList<DateTime>? _finishedComicTimestamps;

    /// <summary>Test seam for the forecast's clock.</summary>
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>" · at your pace, done by ~Nov 2026 · 2 still missing", or empty.</summary>
    [ObservableProperty]
    private string _forecastSuffix = string.Empty;

    partial void OnForecastSuffixChanged(string value)
    {
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(MetaLine));
    }

    /// <summary>Drops the cached reading pace, so the next list load re-reads it (called on every visit to the screen).</summary>
    public void InvalidateForecast() => _finishedComicTimestamps = null;

    private void UpdateForecast(int unread, int missingUnread)
    {
        var now = UtcNow();
        if (_finishedComicTimestamps is null)
        {
            using var context = PaperbunkrDb.CreateContext();
            _finishedComicTimestamps = ReadingListForecast.LoadFinishedComicTimestamps(context, now);
        }

        var forecast = ReadingListForecast.Compute(_finishedComicTimestamps, unread, missingUnread, now);
        ForecastSuffix = forecast is null
            ? string.Empty
            : $" · at your pace, done by {ReadingListForecast.FormatFinishBy(forecast.FinishByUtc, now)}"
              + (forecast.MissingUnread > 0 ? $" · {forecast.MissingUnread} still missing" : string.Empty);
    }

    // --- Overlap (§7) ---

    private IReadOnlyList<ReadingListOverlapPair> _overlaps = Array.Empty<ReadingListOverlapPair>();

    /// <summary>Partners of the open list, highest overlap first.</summary>
    public ObservableCollection<OverlapPartner> OverlapPartners { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlap))]
    [NotifyPropertyChangedFor(nameof(OverlapText))]
    private OverlapPartner? _currentOverlap;

    public bool HasOverlap => CurrentOverlap is not null && IsListOpen;

    /// <summary>The Checks strip shows when it has at least one line (K1).</summary>
    public bool HasChecks => HasOverlap || HasCanonicalPanel || HasRebuildNote;

    private void RaiseChecks() => OnPropertyChanged(nameof(HasChecks));

    public string OverlapText => CurrentOverlap is { } o ? $"Shares {o.Shared} of {o.OwnCount} issues with {o.Name}" : string.Empty;

    public bool HasMoreOverlaps => OverlapPartners.Count > 1;

    public string MoreOverlapsLabel => OverlapPartners.Count > 1 ? $"+{OverlapPartners.Count - 1} more" : string.Empty;

    /// <summary>Overlap pairs across every list, then the open list's partners (the Checks strip's first line).</summary>
    private void RefreshOverlaps(PaperbunkrDbContext context)
    {
        var lists = context.ReadingListItems.AsNoTracking()
            .Select(i => new { i.ReadingListId, i.IssueId })
            .AsEnumerable()
            .GroupBy(i => i.ReadingListId)
            .Select(g => (ListId: g.Key, IssueIds: (IReadOnlySet<int>)g.Select(i => i.IssueId).ToHashSet()))
            .ToList();
        var dismissed = context.ReadingListOverlapDismissals.Select(d => new { d.ListAId, d.ListBId }).AsEnumerable()
            .Select(d => (d.ListAId, d.ListBId)).ToHashSet();
        _overlaps = ReadingListOverlap.Find(lists, dismissed);
        var sizes = lists.ToDictionary(l => l.ListId, l => l.IssueIds.Count);
        var names = context.ReadingLists.Select(l => new { l.Id, l.Name }).ToDictionary(l => l.Id, l => l.Name);

        int? keep = CurrentOverlap?.ListId;
        OverlapPartners.Clear();
        if (_activeReadingListId is int active)
        {
            foreach (var pair in _overlaps.Where(p => p.ListA == active || p.ListB == active))
            {
                int other = pair.Other(active);
                OverlapPartners.Add(new OverlapPartner(other, names.GetValueOrDefault(other, "another list"), pair.Shared, sizes.GetValueOrDefault(active)));
            }
        }

        CurrentOverlap = OverlapPartners.FirstOrDefault(p => p.ListId == keep) ?? OverlapPartners.FirstOrDefault();
        OnPropertyChanged(nameof(HasMoreOverlaps));
        OnPropertyChanged(nameof(MoreOverlapsLabel));
        OnPropertyChanged(nameof(HasOverlap));
        RaiseChecks();
    }

    [RelayCommand]
    private void SelectOverlapPartner(OverlapPartner? partner)
    {
        if (partner is not null)
        {
            CurrentOverlap = partner;
        }
    }

    /// <summary>"Not a duplicate": permanent for the pair (decision Q26).</summary>
    [RelayCommand]
    private void DismissOverlap()
    {
        if (_activeReadingListId is not int active || CurrentOverlap is not { } partner)
        {
            return;
        }

        var (a, b) = ReadingListOverlap.Normalize(active, partner.ListId);
        using (var context = PaperbunkrDb.CreateContext())
        {
            if (!context.ReadingListOverlapDismissals.Any(d => d.ListAId == a && d.ListBId == b))
            {
                context.ReadingListOverlapDismissals.Add(new ReadingListOverlapDismissal { ListAId = a, ListBId = b, CreatedAt = DateTime.UtcNow });
                context.SaveChanges();
            }
        }

        Dispatcher.UIThread.Post(Reload);
    }

    /// <summary>The Compare overlay (Q25), or null when closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComparing))]
    private ReadingListCompareViewModel? _compare;

    public bool IsComparing => Compare is not null;

    [RelayCommand]
    private void CloseCompare() => Compare = null;

    [RelayCommand]
    private void CompareOverlap()
    {
        if (_activeReadingListId is not int active || CurrentOverlap is not { } partner)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        Compare = ReadingListCompareViewModel.Build(context, active, partner.ListId, () => Compare = null, () => _ = MergeOverlap());
    }

    /// <summary>Merge the partner list into the open one (Q16), after a confirm that states the numbers.</summary>
    [RelayCommand]
    private async Task MergeOverlap()
    {
        if (_activeReadingListId is not int active || CurrentOverlap is not { } partner)
        {
            return;
        }

        int toAdd;
        using (var context = PaperbunkrDb.CreateContext())
        {
            toAdd = ReadingListMerger.CountToAdd(context, active, partner.ListId);
        }

        string issues = toAdd == 1 ? "1 issue" : $"{toAdd} issues";
        int choice = _dialogs is null
            ? 0
            : await _dialogs.ShowAsync(new ConfirmDialogRequest(
                $"Add {issues} from \"{partner.Name}\" to \"{ListName}\". Notes and roles from \"{partner.Name}\" fill in only where this list has none.",
                Title: "Merge reading lists",
                PrimaryLabel: $"Merge and delete \"{partner.Name}\"",
                SecondaryLabel: "Merge, keep both"));
        if (choice < 0)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            var result = ReadingListMerger.Merge(context, active, partner.ListId, deleteOther: choice == 0);
            context.SaveChanges();
            Compare = null;
            Dispatcher.UIThread.Post(() =>
            {
                LoadReadingList(active);
                StatusMessage = $"Merged: {result.AddedCount} added" + (result.DeletedOther ? $", \"{partner.Name}\" deleted." : ".");
            });
        }
    }

    // --- Exports (§6) ---

    [RelayCommand]
    private async Task ExportChecklist()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        string? path = await _filePicker.PickSaveFileAsync("Save printable checklist", $"{ListName} checklist.pdf", "pdf", "PDF document");
        if (path is null)
        {
            return;
        }

        using var job = _activity.StartJob(ActivityJobKind.Other, "Saving checklist");
        try
        {
            ChecklistModel model;
            using (var context = PaperbunkrDb.CreateContext())
            {
                model = ReadingListChecklist.Build(context, listId, DateTime.Now);
            }

            // Written next to the target first, then moved into place: a failure never leaves a half-written PDF behind.
            string temp = path + ".partial";
            await Task.Run(() =>
            {
                using (var stream = File.Create(temp))
                {
                    ReadingListChecklistPdf.Write(stream, model, ReadingListChecklistPdf.DefaultPaperSize());
                }

                File.Move(temp, path, overwrite: true);
            });
            StatusMessage = $"Checklist saved to {path}.";
            job.Succeed("Checklist saved", new ActivityLink(ActivityLinkKind.ExternalUrl, new Uri(path).AbsoluteUri));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Couldn't save the checklist: {ex.Message}";
            job.Fail(StatusMessage, ex: ex);
        }
    }

    [RelayCommand]
    private async Task ExportCsv()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        string? path = await _filePicker.PickSaveFileAsync("Export Reading List as CSV", $"{ListName}.csv", "csv", "CSV file");
        if (path is null)
        {
            return;
        }

        string csv;
        using (var context = PaperbunkrDb.CreateContext())
        {
            csv = CsvReadingListIO.Write(context, listId);
        }

        await File.WriteAllTextAsync(path, csv);
        StatusMessage = $"Exported to {path}.";
    }

    // --- Continuity link and Rebuild (spec B §2) ---

    private int? _linkedContinuityId;
    private ContinuityOrderKind? _linkedContinuityOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContinuityLinked))]
    [NotifyPropertyChangedFor(nameof(ContinuityLinkLabel))]
    private string? _continuityLinkName;

    public bool IsContinuityLinked => ContinuityLinkName is not null;

    public string ContinuityLinkLabel => ContinuityLinkName is null
        ? string.Empty
        : $"from {ContinuityLinkName} · {(_linkedContinuityOrder == ContinuityOrderKind.PublicationOrder ? "publication order" : "story order")}";

    /// <summary>"Rebuilt · 12 added · 3 removed · 40 moved" - dismissible note under the header.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRebuildNote))]
    private string? _rebuildNote;

    public bool HasRebuildNote => RebuildNote is not null;

    partial void OnRebuildNoteChanged(string? value) => RaiseChecks();

    [RelayCommand]
    private void DismissRebuildNote() => RebuildNote = null;

    /// <summary>Navigation to a continuity's page from the "from X" link; set by MainViewModel.</summary>
    public Action<int>? OpenContinuity { get; set; }

    [RelayCommand]
    private void OpenLinkedContinuity()
    {
        if (_linkedContinuityId is int id)
        {
            OpenContinuity?.Invoke(id);
        }
    }

    [RelayCommand]
    private async Task RebuildFromContinuity()
    {
        if (_activeReadingListId is not int listId || _linkedContinuityId is not int continuityId)
        {
            return;
        }

        var kind = _linkedContinuityOrder ?? ContinuityOrderKind.PublicationOrder;
        using var job = _activity.StartJob(ActivityJobKind.Other, $"Rebuilding {ListName}");
        try
        {
            var result = await Task.Run(() =>
            {
                using var context = PaperbunkrDb.CreateContext();
                var order = ContinuityListOrders.Compute(context, continuityId, kind);
                return ContinuityReadingListBuilder.RebuildFromOrder(context, listId, order);
            });
            string note = $"Rebuilt · {result.Added} added · {result.Removed} removed · {result.Moved} moved";
            job.Succeed(note);
            LoadReadingList(listId);
            RebuildNote = note;
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            job.Fail(ex.Message, ex: ex);
        }
    }

    // --- Check against ComicVine/Metron (spec B §3) ---

    private StoryEvent? _linkedStoryEvent;
    private CanonicalDiff? _canonicalDiff;
    private bool _preferMetron;

    /// <summary>Test seam: resolves a reading-list source by key (production: the registry, which needs credentials).</summary>
    internal Func<string, IReadingListSource?> GetSource { get; set; } = key =>
    {
        using var context = PaperbunkrDb.CreateContext();
        return ReadingListSourceRegistry.Get(context, key);
    };

    /// <summary>"Check against ComicVine" (or Metron) in the Manage menu; empty when the list can't be checked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheckCanonical))]
    private string _checkCanonicalLabel = string.Empty;

    public bool CanCheckCanonical => CheckCanonicalLabel.Length > 0;

    private void RefreshCanonicalAvailability()
    {
        var keys = _linkedStoryEvent is null ? Array.Empty<string>() : ReadingListCanonicalDiff.AvailableSourceKeys(_linkedStoryEvent, GetSource);
        string? preferred = _preferMetron && keys.Contains("Metron") ? "Metron" : keys.FirstOrDefault();
        CheckCanonicalLabel = preferred is null ? string.Empty : $"Check against {ReadingListSourceRegistry.GetDisplayName(preferred)}";
        CanSwitchCanonicalSource = keys.Count > 1;
    }

    [ObservableProperty]
    private bool _canSwitchCanonicalSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCanonicalPanel))]
    private string? _canonicalSummary;

    public bool HasCanonicalPanel => CanonicalSummary is not null;

    partial void OnCanonicalSummaryChanged(string? value) => RaiseChecks();

    [ObservableProperty]
    private bool _canonicalHasDifferences;

    [ObservableProperty]
    private string _insertMissingLabel = string.Empty;

    [ObservableProperty]
    private bool _canReorderToMatch;

    [ObservableProperty]
    private bool _canonicalDetailsOpen;

    public ObservableCollection<string> CanonicalMissingLines { get; } = new();

    public bool HasCanonicalMissing => CanonicalMissingLines.Count > 0;

    public string SwitchCanonicalSourceLabel => _canonicalDiff?.SourceKey == "Metron" ? "Use ComicVine instead" : "Use Metron instead";

    private void ClearCanonicalPanel()
    {
        _canonicalDiff = null;
        CanonicalSummary = null;
        CanonicalHasDifferences = false;
        CanonicalDetailsOpen = false;
        CanonicalMissingLines.Clear();
    }

    [RelayCommand]
    private async Task CheckCanonical()
    {
        if (_activeReadingListId is not int listId || _linkedStoryEvent is null
            || ReadingListCanonicalDiff.ChooseSource(_linkedStoryEvent, GetSource, _preferMetron) is not { } choice)
        {
            return;
        }

        using var job = _activity.StartJob(ActivityJobKind.Other, $"Checking {ListName} against {choice.Source.DisplayName}");
        try
        {
            var providerOrder = await ReadingListCanonicalDiff.FetchAsync(choice.Source, choice.ArcId, job.CancellationToken);
            using var context = PaperbunkrDb.CreateContext();
            ShowCanonicalDiff(ReadingListCanonicalDiff.Compute(context, listId, choice.Source.SourceKey, providerOrder));
            job.Succeed(CanonicalSummary ?? "Checked.");
        }
        catch (ReadingListSourceException ex)
        {
            StatusMessage = ex.Message;
            job.Fail(ex.Message, ex: ex);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelled.";
        }
    }

    private void ShowCanonicalDiff(CanonicalDiff diff)
    {
        _canonicalDiff = diff;
        CanonicalMissingLines.Clear();
        foreach (var entry in diff.Missing)
        {
            string year = entry.Arc.Year > 0 ? $" ({entry.Arc.Year})" : string.Empty;
            CanonicalMissingLines.Add($"#{entry.Position + 1}  {entry.Arc.Series} #{entry.Arc.Number}{year} · {(entry.LocalIssueId is null ? "not in library" : "owned")}");
        }

        OnPropertyChanged(nameof(HasCanonicalMissing));
        CanonicalHasDifferences = !diff.Matches;
        InsertMissingLabel = $"Insert missing ({diff.Missing.Count})";
        CanReorderToMatch = diff.OutOfOrderCount > 0;
        OnPropertyChanged(nameof(SwitchCanonicalSourceLabel));
        if (diff.Matches)
        {
            CanonicalSummary = $"Matches {diff.SourceName}'s order.";
            var shown = diff;
            DispatcherTimer.RunOnce(() =>
            {
                if (ReferenceEquals(_canonicalDiff, shown))
                {
                    ClearCanonicalPanel();
                }
            }, TimeSpan.FromSeconds(4));
            return;
        }

        var parts = new List<string>();
        if (diff.Missing.Count > 0)
        {
            parts.Add($"lists {diff.Missing.Count} issue{(diff.Missing.Count == 1 ? "" : "s")} this list doesn't have");
        }

        if (diff.OutOfOrderCount > 0)
        {
            parts.Add($"{diff.OutOfOrderCount} {(diff.OutOfOrderCount == 1 ? "is" : "are")} out of order");
        }

        CanonicalSummary = $"{diff.SourceName} {string.Join(" · ", parts)}";
    }

    [RelayCommand]
    private void InsertCanonicalMissing() => ApplyCanonical(ReadingListCanonicalDiff.InsertMissing);

    [RelayCommand]
    private void ReorderCanonical() => ApplyCanonical(ReadingListCanonicalDiff.ReorderToMatch);

    private void ApplyCanonical(Func<PaperbunkrDbContext, CanonicalDiff, Paperbunkr.Data.Events.LibraryEvents?, int> apply)
    {
        if (_canonicalDiff is not { } diff || _activeReadingListId != diff.ListId)
        {
            return;
        }

        var providerOrder = diff.Entries.Select(e => e.Arc).ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            apply(context, diff, null);
            context.SaveChanges();
        }

        // Re-compute from the provider order already fetched - no second request.
        Dispatcher.UIThread.Post(() =>
        {
            LoadReadingList(diff.ListId);
            using var context = PaperbunkrDb.CreateContext();
            ShowCanonicalDiff(ReadingListCanonicalDiff.Compute(context, diff.ListId, diff.SourceKey, providerOrder));
        });
    }

    [RelayCommand]
    private void ToggleCanonicalDetails() => CanonicalDetailsOpen = !CanonicalDetailsOpen;

    [RelayCommand]
    private void DismissCanonical() => ClearCanonicalPanel();

    [RelayCommand]
    private async Task SwitchCanonicalSource()
    {
        _preferMetron = _canonicalDiff?.SourceKey != "Metron";
        RefreshCanonicalAvailability();
        await CheckCanonical();
    }
}
