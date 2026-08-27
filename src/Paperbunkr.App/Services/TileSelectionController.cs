using System;
using System.Collections.Generic;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Services;

/// <summary>
/// Shared multi-selection logic for a tile/card grid, extracted from
/// <c>DetailTabsViewModel</c>'s original <c>ToggleIssueSelection</c> (docs/superpowers/specs/
/// 2026-08-07-bulk-issue-editing-design.md §1) and generalized so Library's issue grids
/// (docs/superpowers/specs/2026-08-24-library-multiselect-slice1-design.md) can reuse the exact
/// same behavior instead of a near-duplicate implementation.
///
/// <para>
/// <see cref="Toggle"/> covers both the "plain click" and "ctrl+click"/checkbox-click gestures with
/// one code path (<paramref name="isShiftHeld"/> false): both additively toggle just the clicked
/// item, leaving the rest of the selection untouched. This matches the behavior this codebase's
/// first selection implementation already shipped with - Paperbunkr's plain click has never done a
/// CE-style "exclusive select, clear everything else"; only shift-click narrows/extends a range.
/// Preserved deliberately rather than silently changed during this extraction.
/// </para>
/// </summary>
public sealed class TileSelectionController<TCard> where TCard : class, ISelectableCard
{
    private readonly HashSet<int> _selectedIds = new();
    private int? _lastToggledIndex;

    public IReadOnlySet<int> SelectedIds => _selectedIds;

    public int Count => _selectedIds.Count;

    public bool IsSelected(int id) => _selectedIds.Contains(id);

    /// <summary>
    /// Toggles or range-extends selection against <paramref name="orderedItems"/> - the currently
    /// displayed order (respects active sort; when grouped, pass the flattened group order). Plain
    /// click and ctrl-click/checkbox-click both call this with <paramref name="isShiftHeld"/> false.
    /// </summary>
    public void Toggle(IList<TCard> orderedItems, TCard item, bool isShiftHeld)
    {
        int index = orderedItems.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        if (isShiftHeld && _lastToggledIndex is int lastIndex)
        {
            int start = Math.Min(lastIndex, index);
            int end = Math.Max(lastIndex, index);
            for (int i = start; i <= end; i++)
            {
                orderedItems[i].IsSelected = true;
                _selectedIds.Add(orderedItems[i].Id);
            }
        }
        else
        {
            item.IsSelected = !item.IsSelected;
            if (item.IsSelected)
            {
                _selectedIds.Add(item.Id);
            }
            else
            {
                _selectedIds.Remove(item.Id);
            }
        }

        _lastToggledIndex = index;
    }

    /// <summary>
    /// Clears the selection. <paramref name="visibleItems"/> (optional) lets the caller also reset
    /// the visible <see cref="ISelectableCard.IsSelected"/> flags of currently-displayed items still
    /// referenced by the UI - pass <see langword="null"/> when the caller is about to discard/rebuild
    /// those items anyway (e.g. loading a different series), since there's nothing to visually reset.
    /// </summary>
    public void Clear(IEnumerable<TCard>? visibleItems = null)
    {
        if (visibleItems is not null)
        {
            foreach (var item in visibleItems)
            {
                if (_selectedIds.Contains(item.Id))
                {
                    item.IsSelected = false;
                }
            }
        }

        _selectedIds.Clear();
        _lastToggledIndex = null;
    }

    /// <summary>
    /// The ids that a right-click action on <paramref name="rightClickedId"/> should operate on:
    /// the current selection plus the right-clicked item, deduplicated - so right-clicking a lone
    /// unselected tile with nothing else selected still acts on just that one, but right-clicking
    /// while other tiles are selected extends the acted-on set to include this one too, without
    /// changing the persisted selection itself (this is a snapshot for one action, not a selection
    /// change). Matches <c>DetailTabsViewModel.EditIssueProperties</c>/<c>RevealIssue</c>'s existing
    /// precedent exactly.
    /// </summary>
    public IReadOnlyList<int> UnionForAction(int rightClickedId)
    {
        if (_selectedIds.Count == 0)
        {
            return new[] { rightClickedId };
        }

        var union = new HashSet<int>(_selectedIds) { rightClickedId };
        return new List<int>(union);
    }
}
