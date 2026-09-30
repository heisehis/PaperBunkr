using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The journey path and the cover wall (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §4-§5, decisions L1/D2/Q7-Q9):
/// one flat, virtualized <see cref="PathItems"/> list built by <see cref="ReadingPathBuilder"/>, chapter collapse kept per list for the
/// session, and the Path | Covers choice remembered app-wide in <c>AppSettings.ReadingListViewMode</c>.
/// </summary>
public partial class ReadingListPageViewModel
{
    public const string PathMode = "Path";
    public const string CoversMode = "Covers";

    /// <summary>Collapsed chapter labels per list id - session only (not saved).</summary>
    private readonly Dictionary<int, HashSet<string>> _collapsedByList = new();

    public ObservableCollection<PathItem> PathItems { get; } = new();

    public ObservableCollection<CoverTileItem> CoverTiles { get; } = new();

    private string? _viewMode;

    /// <summary>"Path" or "Covers" - read lazily from AppSettings the first time.</summary>
    public string ViewMode
    {
        get
        {
            if (_viewMode is null)
            {
                using var context = PaperbunkrDb.CreateContext();
                _viewMode = context.GetOrCreateAppSettings().ReadingListViewMode == CoversMode ? CoversMode : PathMode;
            }

            return _viewMode;
        }
    }

    public bool IsPathMode => ViewMode == PathMode;

    public bool IsCoversMode => ViewMode == CoversMode;

    [RelayCommand]
    private void SetViewMode(string? mode)
    {
        string next = mode == CoversMode ? CoversMode : PathMode;
        if (next == ViewMode)
        {
            return;
        }

        _viewMode = next;
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.GetOrCreateAppSettings().ReadingListViewMode = next;
            context.SaveChanges();
        }

        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(IsPathMode));
        OnPropertyChanged(nameof(IsCoversMode));
        RebuildPath();
    }

    /// <summary>Rebuilds <see cref="PathItems"/> and <see cref="CoverTiles"/> from the current rows, collapse state and mode.</summary>
    private void RebuildPath()
    {
        var collapsed = _activeReadingListId is int id && _collapsedByList.TryGetValue(id, out var set)
            ? set
            : new HashSet<string>();

        PathItems.Clear();
        foreach (var item in ReadingPathBuilder.Build(_rows, collapsed, IsEditing))
        {
            PathItems.Add(item);
        }

        CoverTiles.Clear();
        foreach (var tile in ReadingPathBuilder.BuildCovers(_rows))
        {
            CoverTiles.Add(tile);
        }
    }

    [RelayCommand]
    private void ToggleChapter(ChapterHeaderItem? header)
    {
        if (header is null || IsEditing || _activeReadingListId is not int id)
        {
            return;
        }

        if (!_collapsedByList.TryGetValue(id, out var set))
        {
            _collapsedByList[id] = set = new HashSet<string>(StringComparer.Ordinal);
        }

        if (!set.Remove(header.Label))
        {
            if (header.IsCurrent)
            {
                return;         // the chapter holding Up next never collapses
            }

            set.Add(header.Label);
        }

        // Deferred: the header raising this is rebuilt (CLAUDE.md runtime gotcha).
        Avalonia.Threading.Dispatcher.UIThread.Post(RebuildPath);
    }

    /// <summary>The item the view should bring into view on open: Up next, else the first item.</summary>
    public PathItem? ScrollTarget => PathItems.OfType<UpNextItem>().FirstOrDefault() ?? PathItems.FirstOrDefault();

    /// <summary>The tile index the cover wall should bring into view on open.</summary>
    public int CoverScrollIndex => Math.Max(0, CoverTiles.ToList().FindIndex(t => t.IsNextUp));
}
