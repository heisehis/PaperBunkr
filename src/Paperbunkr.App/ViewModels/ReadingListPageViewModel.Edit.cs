using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Edit mode (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §8, decision Q14): an explicit mode, not remembered, that
/// swaps the hero buttons for an editing bar and turns the path into editable rows - whole-list drag reorder (and Ctrl+↑/↓), checkboxes
/// and the bulk bar, role pickers, notes, remove, and chapter editing. Chapters are expanded and "Up next" is a plain row while editing.
/// Writes go through <see cref="ReadingListManager.MoveItemsTo"/> and <see cref="ReadingListManager.SetGroupLabel"/>.
/// </summary>
public partial class ReadingListPageViewModel
{
    public const string NewChapterLabel = "New chapter";

    [ObservableProperty]
    private bool _isEditing;

    partial void OnIsEditingChanged(bool value)
    {
        if (!value)
        {
            MemberSelection.Clear(_rows);
            RaiseSelectionState();
            foreach (var row in _rows.Where(r => r.NoteEditing))
            {
                row.NoteEditing = false;
            }
        }

        RebuildPath();
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (IsListOpen)
        {
            IsEditing = true;
        }
    }

    [RelayCommand]
    private void EndEdit() => IsEditing = false;

    /// <summary>The existing chapter labels in order, for "Move to chapter ▾".</summary>
    public IReadOnlyList<string> ChapterLabels =>
        ReadingListGrouping.Runs(_rows, r => r.Item.GroupLabel).Select(r => r.Label).Where(l => l.Length > 0).Distinct().ToList();

    /// <summary>"Move to chapter ▾" picks: every chapter, then "No chapter" (an empty string).</summary>
    public IReadOnlyList<string> MoveToChapterChoices => ChapterLabels.Append(NoChapterChoice).ToList();

    public const string NoChapterChoice = "No chapter";

    /// <summary>Bulk: put the selected rows in a chapter (or none).</summary>
    [RelayCommand]
    private void MoveSelectionToChapter(string? label)
    {
        if (_activeReadingListId is not int listId || MemberSelection.Count == 0)
        {
            return;
        }

        SetLabel(listId, MemberSelection.SelectedIds.ToList(), label == NoChapterChoice ? null : label);
    }

    /// <summary>"＋ Chapter": the selected rows become a new chapter, which starts in rename.</summary>
    [RelayCommand]
    private void AddChapter()
    {
        if (_activeReadingListId is not int listId || MemberSelection.Count == 0)
        {
            StatusMessage = "Select the issues that make up the new chapter first.";
            return;
        }

        string label = NewChapterLabel;
        var existing = ChapterLabels.ToHashSet(StringComparer.Ordinal);
        for (int n = 2; existing.Contains(label); n++)
        {
            label = $"{NewChapterLabel} {n}";
        }

        var ids = MemberSelection.SelectedIds.ToList();
        SetLabel(listId, ids, label, then: () =>
        {
            if (PathItems.OfType<ChapterHeaderItem>().FirstOrDefault(h => h.Label == label) is { } header)
            {
                BeginChapterRename(header);
            }
        });
    }

    [RelayCommand]
    private void BeginChapterRename(ChapterHeaderItem? header)
    {
        if (header is null || !IsEditing)
        {
            return;
        }

        foreach (var other in PathItems.OfType<ChapterHeaderItem>().Where(h => h.IsRenaming))
        {
            other.IsRenaming = false;
        }

        header.RenameText = header.Label;
        header.IsRenaming = true;
    }

    [RelayCommand]
    private void CommitChapterRename(ChapterHeaderItem? header)
    {
        if (header is not { IsRenaming: true } || _activeReadingListId is not int listId)
        {
            return;
        }

        header.IsRenaming = false;
        string label = header.RenameText.Trim();
        if (label == header.Label)
        {
            return;
        }

        // A blank name removes the chapter (its issues become unlabelled).
        SetLabel(listId, header.ItemIds, label.Length == 0 ? null : label);
    }

    [RelayCommand]
    private void CancelChapterRename(ChapterHeaderItem? header)
    {
        if (header is not null)
        {
            header.IsRenaming = false;
        }
    }

    private void SetLabel(int listId, IReadOnlyCollection<int> itemIds, string? label, Action? then = null)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            if (ReadingListManager.SetGroupLabel(context, listId, itemIds, label) == 0)
            {
                return;
            }

            context.SaveChanges();
        }

        Dispatcher.UIThread.Post(() =>
        {
            Reload();
            then?.Invoke();
        });
    }

    /// <summary>
    /// Moves <paramref name="moving"/> so it sits before <paramref name="before"/> (null = the end) - the drop of a drag in Edit mode. Dragging
    /// a selected row moves the whole selection. The moved rows adopt the chapter they land in: the label of the row they now follow, or of
    /// the row they precede when they land first.
    /// </summary>
    public bool MoveRows(IReadOnlyList<ReadingListItemRowViewModel> moving, ReadingListItemRowViewModel? before)
    {
        if (_activeReadingListId is not int listId || moving.Count == 0 || (before is not null && moving.Contains(before)))
        {
            return false;
        }

        var ids = moving.Select(r => r.Id).ToHashSet();
        var rest = _rows.Where(r => !ids.Contains(r.Id)).ToList();
        int target = before is null ? rest.Count : rest.IndexOf(before);
        if (target < 0)
        {
            return false;
        }

        string? adopt = target > 0 ? rest[target - 1].Item.GroupLabel : rest.Count > 0 ? rest[0].Item.GroupLabel : null;
        bool moved;
        using (var context = PaperbunkrDb.CreateContext())
        {
            moved = ReadingListManager.MoveItemsTo(context, listId, ids, target);
            int relabelled = ReadingListManager.SetGroupLabel(context, listId, ids, adopt);
            if (!moved && relabelled == 0)
            {
                return false;
            }

            context.SaveChanges();
        }

        Dispatcher.UIThread.Post(Reload);
        return true;
    }

    /// <summary>The rows a drag of <paramref name="row"/> carries: the selection when the row is selected, else just the row.</summary>
    public IReadOnlyList<ReadingListItemRowViewModel> DragSet(ReadingListItemRowViewModel row) =>
        row.IsSelected ? _rows.Where(r => r.IsSelected).ToList() : new[] { row };

    /// <summary>Ctrl+↑ / Ctrl+↓: moves the selection (or <paramref name="focused"/>) one place, keeping its chapter.</summary>
    public bool MoveSelectionBy(ReadingListItemRowViewModel? focused, int delta)
    {
        if (_activeReadingListId is not int listId || delta == 0)
        {
            return false;
        }

        var set = focused is not null ? DragSet(focused) : _rows.Where(r => r.IsSelected).ToList();
        if (set.Count == 0)
        {
            return false;
        }

        var ids = set.Select(r => r.Id).ToHashSet();
        var rest = _rows.Where(r => !ids.Contains(r.Id)).ToList();
        int firstIndex = _rows.IndexOf(set[0]);
        int currentTarget = _rows.Take(firstIndex).Count(r => !ids.Contains(r.Id));
        int target = Math.Clamp(currentTarget + delta, 0, rest.Count);
        if (target == currentTarget)
        {
            return false;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            if (!ReadingListManager.MoveItemsTo(context, listId, ids, target))
            {
                return false;
            }

            context.SaveChanges();
        }

        var keepSelected = ids;
        Dispatcher.UIThread.Post(() =>
        {
            Reload();
            foreach (var row in _rows.Where(r => keepSelected.Contains(r.Id)))
            {
                MemberSelection.Toggle(_rows, row, isShiftHeld: false);
            }

            RaiseSelectionState();
        });
        return true;
    }
}
