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
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Target-based Library actions (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §2): each command takes the
/// <see cref="LibraryTarget"/> the catalog resolved when it built the menu or bar, so a series selection works as well as an issue one and the
/// label's count is always the count acted on.
/// </summary>
public partial class LibraryScreenViewModel
{
    private Func<string, Task> _setClipboardText = ClipboardHelper.CopyTextAsync;

    /// <summary>Test seam for "Copy file paths" - the system clipboard needs a real window.</summary>
    internal Func<string, Task> SetClipboardText
    {
        get => _setClipboardText;
        set => _setClipboardText = value;
    }

    /// <summary>Issue ids of <paramref name="target"/>: the ids themselves, or every issue of the targeted series.</summary>
    internal IReadOnlyList<int> IssueIdsOf(LibraryTarget target) =>
        target.IsSeries ? ExpandSeriesToIssueIds(target.Ids) : target.Ids;

    /// <summary>The visible issue rows in on-screen order, for the catalog (checks, file presence, series of a selection).</summary>
    internal IList<IssueListRow> VisibleIssueRows => GetOrderedVisibleIssueRows();

    /// <summary>The visible series cards in on-screen order, for the catalog.</summary>
    internal IList<SeriesCardSample> VisibleSeriesCards => GetOrderedVisibleSeriesCards();

    // ----- Selection bar (docs/superpowers/specs/2026-09-29-library-bulk-actions-design.md §1, option C: icon-only) -----

    /// <summary>The bar's left-hand slots for the current selection, from <see cref="LibraryActionCatalog.BuildBar"/>.</summary>
    public IReadOnlyList<LibraryBarItem> BarLeadingItems { get; private set; } = Array.Empty<LibraryBarItem>();

    /// <summary>The bar's right-aligned slots (Delete, Clear).</summary>
    public IReadOnlyList<LibraryBarItem> BarTrailingItems { get; private set; } = Array.Empty<LibraryBarItem>();

    public string SelectionCountLabel => HasSelection
        ? $"{SelectionCount} selected"
        : SeriesSelectionCount == 1 ? "1 series selected" : $"{SeriesSelectionCount} series selected";

    private bool _clipboardHooked;

    /// <summary>Rebuilds the bar - on every selection change, and when something an entry's state depends on changes (the Copy Data
    /// clipboard, the write-to-files setting). Submenus are rebuilt again at click time (<see cref="BuildBarMenu"/>), so lists and
    /// collections added since are always current.</summary>
    internal void RaiseBarItems()
    {
        if (!_clipboardHooked)
        {
            _clipboardHooked = true;
            _metadataClipboard.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(HasMetadataClipboard));
                RaiseBarItems();
            });
        }

        var catalog = new LibraryActionCatalog(this);
        var items = catalog.ForSelection() is { } context ? catalog.BuildBar(context) : Array.Empty<LibraryBarItem>();
        BarLeadingItems = items.Where(i => !i.IsTrailing).ToList();
        BarTrailingItems = items.Where(i => i.IsTrailing && !i.IsSeparator).ToList();
        OnPropertyChanged(nameof(BarLeadingItems));
        OnPropertyChanged(nameof(BarTrailingItems));
        OnPropertyChanged(nameof(SelectionCountLabel));
    }

    partial void OnCanWriteMetadataToFilesChanged(bool value) => RaiseBarItems();

    /// <summary>A bar ▾ button's menu, built fresh for the current selection when it is clicked.</summary>
    internal IReadOnlyList<Paperbunkr.App.ContextMenus.ContextMenuEntry> BuildBarMenu(string actionId)
    {
        var catalog = new LibraryActionCatalog(this);
        if (catalog.ForSelection() is not { } context)
        {
            return Array.Empty<Paperbunkr.App.ContextMenus.ContextMenuEntry>();
        }

        var entry = catalog.BuildBar(context).FirstOrDefault(i => i.ActionId == actionId)?.Entry;
        return entry?.Children ?? (IReadOnlyList<Paperbunkr.App.ContextMenus.ContextMenuEntry>)Array.Empty<Paperbunkr.App.ContextMenus.ContextMenuEntry>();
    }

    [RelayCommand]
    private void MarkTargetRead(LibraryTarget target) => MarkIssuesRead(IssueIdsOf(target));

    [RelayCommand]
    private void MarkTargetUnread(LibraryTarget target) => MarkIssuesUnread(IssueIdsOf(target));

    /// <summary>
    /// "Mark as ▸ Read up to here" (a Paperbunkr addition, no CE equivalent): every non-special issue of the clicked issue's series that
    /// comes before it in run order (<see cref="IssueOrdering.OrderByRun"/>, the Detail screen's own order), plus the issue itself.
    /// </summary>
    [RelayCommand]
    private void MarkReadUpToHere(int issueId)
    {
        List<int> ids;
        using (var context = PaperbunkrDb.CreateContext(includeRemote: true))
        {
            var issue = context.Issues.Find(issueId);
            if (issue is null)
            {
                return;
            }

            var ordered = context.Issues.Where(i => i.SeriesId == issue.SeriesId).ToList()
                .Where(i => !i.IsSpecial())
                .OrderByRun()
                .ToList();
            int index = ordered.FindIndex(i => i.Id == issueId);
            ids = index < 0 ? new List<int> { issueId } : ordered.Take(index + 1).Select(i => i.Id).ToList();
        }

        MarkIssuesRead(ids);
    }

    /// <summary>Whether "Read up to here" can work out an order for this issue - it needs a numeric issue number.</summary>
    internal static bool CanMarkReadUpTo(IssueListRow row) => row.NumberSortKey is not null;

    // ----- Series ▸ setters over every series of the target (the older per-value commands still act on one series) -----

    [RelayCommand]
    private void SetSeriesContentTypeFor((IReadOnlyList<int> SeriesIds, ContentType Value) args)
    {
        if (UpdateSeries(args.SeriesIds, s => s.ContentType = args.Value))
        {
            LoadFromDatabase();
        }
    }

    [RelayCommand]
    private void SetSeriesStatusFor((IReadOnlyList<int> SeriesIds, SeriesStatus Value) args)
    {
        if (UpdateSeries(args.SeriesIds, s => s.Status = args.Value))
        {
            foreach (int id in args.SeriesIds)
            {
                InvalidateSeriesProjection(id, s => s.Status = args.Value);
            }
        }
    }

    [RelayCommand]
    private void SetSeriesReadingStatusFor((IReadOnlyList<int> SeriesIds, ReadingStatus Value) args)
    {
        if (UpdateSeries(args.SeriesIds, s => s.ReadingStatus = args.Value))
        {
            foreach (int id in args.SeriesIds)
            {
                InvalidateSeriesProjection(id, s => s.ReadingStatus = args.Value);
            }
        }
    }

    [RelayCommand]
    private void SetSeriesReadingModeFor((IReadOnlyList<int> SeriesIds, ReadingMode Value) args)
    {
        if (UpdateSeries(args.SeriesIds, s => s.ReadingMode = args.Value))
        {
            foreach (int id in args.SeriesIds)
            {
                InvalidateSeriesProjection(id, s => s.ReadingMode = args.Value);
            }
        }
    }

    private bool UpdateSeries(IReadOnlyList<int> seriesIds, Action<Series> apply)
    {
        var local = LocalSeriesOnly(seriesIds);
        if (local.Count == 0)
        {
            return false;
        }

        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Where(s => local.Contains(s.Id)).ToList();
        foreach (var s in series)
        {
            apply(s);
        }

        context.SaveChanges();
        return series.Count > 0;
    }

    // ----- Files -----

    /// <summary>"Show in Explorer" for any number of books (CE's Ctrl+G): one book reveals its file, several reveal every folder they are in.</summary>
    [RelayCommand]
    private void RevealTarget(LibraryTarget target)
    {
        var ids = IssueIdsOf(target);
        if (ids.Count == 0)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var issues = context.Issues.Where(i => ids.Contains(i.Id) && i.FilePath != null).ToList();
        if (issues.Count == 1)
        {
            RevealInExplorerHelper.RevealIssue(issues[0], _activity);
        }
        else if (issues.Count > 1)
        {
            RevealInExplorerHelper.RevealIssues(issues, _activity);
        }
    }

    /// <summary>"Copy file paths": full paths, one per line, in on-screen order; fileless and remote books are skipped.</summary>
    [RelayCommand]
    private async Task CopyFilePaths(LibraryTarget target)
    {
        var ids = IssueIdsOf(target).Where(id => !_remoteIssueIds.Contains(id)).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        Dictionary<int, string> paths;
        using (var context = PaperbunkrDb.CreateContext())
        {
            paths = context.Issues.Where(i => ids.Contains(i.Id) && i.FilePath != null)
                .Select(i => new { i.Id, i.FilePath })
                .ToList()
                .ToDictionary(i => i.Id, i => i.FilePath!);
        }

        // Issue targets follow the on-screen row order; a series target is already in card-then-number order.
        IEnumerable<int> order = target.IsSeries
            ? ids
            : VisibleIssueRows.Select(r => r.Id).Where(ids.Contains).Concat(ids).Distinct();
        var lines = order.Where(paths.ContainsKey).Select(id => paths[id]).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        await _setClipboardText(string.Join(Environment.NewLine, lines));
        _showToast("Copied", lines.Count == 1 ? "Copied the file path." : $"Copied {lines.Count} file paths.");
    }

    // ----- Selection -----

    /// <summary>"Invert Selection" (CE's <c>itemView.InvertSelection</c>) within the grid currently shown.</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        if (IsSeriesGranularity)
        {
            SeriesSelection.InvertWithin(GetOrderedVisibleSeriesCards());
            SeriesSelectionCount = SeriesSelection.Count;
        }
        else
        {
            Selection.InvertWithin(GetOrderedVisibleIssueRows());
            SelectionCount = Selection.Count;
        }
    }

    /// <summary>Clears whichever selection is active - the bar's ✕ and Esc.</summary>
    [RelayCommand]
    private void ClearAnySelection()
    {
        if (HasSelection)
        {
            ClearSelection();
        }

        if (HasSeriesSelection)
        {
            ClearSeriesSelection();
        }
    }

    // ----- Series-target counterparts of issue-only commands -----

    /// <summary>Bulk Edit for a target: the issue editor (single or bulk) for issues, the bulk series editor for series.</summary>
    [RelayCommand]
    private void BulkEditTarget(LibraryTarget target)
    {
        if (target.IsSeries)
        {
            var local = LocalSeriesOnly(target.Ids);
            if (local.Count > 0)
            {
                _goBulkSeriesProperties(local);
            }
        }
        else
        {
            OpenIssueEditor(target.Ids);
        }
    }

    [RelayCommand]
    private void AddTargetToReadingList((LibraryTarget Target, int ReadingListId) args)
    {
        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists.Find(args.ReadingListId);
        if (list is null)
        {
            return;
        }

        AddIssuesToReadingList(context, list, IssueIdsOf(args.Target));
        context.SaveChanges();
    }

    [RelayCommand]
    private void CreateReadingListAndAddTarget(LibraryTarget target)
    {
        using var context = PaperbunkrDb.CreateContext();
        var now = DateTime.UtcNow;
        var list = new ReadingList
        {
            Name = "New Reading List",
            SortOrder = context.ReadingLists.Count(),
            Type = ReadingListType.User,
            CreatedAt = now,
            UpdatedAt = now,
        };
        context.ReadingLists.Add(list);
        context.SaveChanges();

        AddIssuesToReadingList(context, list, IssueIdsOf(target));
        context.SaveChanges();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadFromDatabase());
    }

    [RelayCommand]
    private async Task RunLibraryPluginOnTarget((LibraryTarget Target, Command Command) args) =>
        await RunLibraryPluginOn(IssueIdsOf(args.Target), args.Command);
}
