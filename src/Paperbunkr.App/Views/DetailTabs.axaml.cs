using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class DetailTabs : UserControl
{
    private readonly FocusReclaimer _focus;
    private DetailTabsViewModel? _viewModel;

    public DetailTabs()
    {
        InitializeComponent();
        _focus = new FocusReclaimer(this, () => _viewModel is { } vm && (vm.IsIssuesTab || vm.IsSpecialsTab), FocusFirstIssue);
        DataContextChanged += (_, _) => Subscribe();
        AttachedToVisualTree += (_, _) => _focus.Reclaim();
    }

    private void Subscribe()
    {
        if (_viewModel is { } old)
        {
            old.PropertyChanged -= OnViewModelPropertyChanged;
            old.IssueGroups.CollectionChanged -= OnIssuesChanged;
            old.Specials.CollectionChanged -= OnIssuesChanged;
        }

        _viewModel = DataContext as DetailTabsViewModel;
        if (_viewModel is { } vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.IssueGroups.CollectionChanged += OnIssuesChanged;
            vm.Specials.CollectionChanged += OnIssuesChanged;
        }
    }

    private void OnIssuesChanged(object? sender, NotifyCollectionChangedEventArgs e) => _focus.ReclaimIfFocusWithinOrNowhere();

    // A tab or view-mode switch hides the focused tile without detaching it; the switch control itself stays focusable, so only a real loss reclaims.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DetailTabsViewModel.ActiveTab) or nameof(DetailTabsViewModel.IssueViewMode))
        {
            _focus.ReclaimIfFocusLost();
        }
    }

    private void FocusFirstIssue()
    {
        var tiles = this.GetVisualDescendants().OfType<Control>()
            .Where(c => c.Focusable && c.DataContext is IssueCardSample && c.IsEffectivelyVisible && c.IsEffectivelyEnabled)
            .ToList();
        var target = tiles.FirstOrDefault(c => c.DataContext is IssueCardSample { IsSelected: true }) ?? tiles.FirstOrDefault();
        target?.Focus(NavigationMethod.Directional);
        target?.BringIntoView();
    }

    /// <summary>
    /// Issue tile click-to-select (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md
    /// §1) - direct pointer-event handling, same as <see cref="PageCanvas"/>, since Shift-range
    /// selection needs <see cref="PointerEventArgs.KeyModifiers"/> that a plain Button/ICommand
    /// binding can't carry. Only the left button toggles selection; right-clicks fall through
    /// untouched so the screen's <see cref="Paperbunkr.App.Controls.ContextMenuHost"/> (bubbling
    /// from the <c>DetailTabs</c> root) still picks them up. Attached in all three
    /// Issues-tab view-mode templates (Poster/List/Card) - <c>sender</c> is whichever tile control
    /// carries the <see cref="IssueCardSample"/> DataContext.
    /// </summary>
    private void OnIssueTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (sender is not Control { DataContext: IssueCardSample issue } || DataContext is not DetailTabsViewModel viewModel)
        {
            return;
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // Plain click focuses one tile (drives the hero + "Read this issue" button); Ctrl/Shift
        // build the bulk-edit multi-selection (real user direction 2026-09-04).
        if (shift || ctrl)
        {
            viewModel.ToggleIssueSelection(issue, shift);
        }
        else
        {
            viewModel.FocusIssue(issue);
        }
    }

    /// <summary>Double-click / double-tap opens the tile's issue in the reader - attached on all
    /// three view-mode templates (Poster/List/Card), same as <see cref="OnIssueTilePointerPressed"/>.</summary>
    private void OnIssueTileDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: IssueCardSample issue } && DataContext is DetailTabsViewModel viewModel)
        {
            viewModel.OpenIssue(issue);
        }
    }

    /// <summary>
    /// Keyboard equivalent of <see cref="OnIssueTilePointerPressed"/> and <see cref="OnIssueTileDoubleTapped"/> (P5, docs/Paperbunkr-Roadmap.md):
    /// Enter opens the issue in the reader, plain Space focuses it (drives the hero), Ctrl/Shift+Space toggles it in the bulk-edit selection.
    /// Shift+arrow extends the range. Other arrow/Home/End keys delegate to <see cref="GridKeyboardNavigation"/> for spatial 2D movement,
    /// resolving the active view mode's own <c>ItemsControl</c> from the focused tile rather than a fixed name.
    /// </summary>
    private void OnIssueTileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || !ReferenceEquals(e.Source, sender) || sender is not Control { DataContext: IssueCardSample issue } control || DataContext is not DetailTabsViewModel viewModel)
        {
            return;
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (e.Key == Key.Enter && !shift && !ctrl)
        {
            viewModel.OpenIssue(issue);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            if (shift || ctrl)
            {
                viewModel.ToggleIssueSelection(issue, shift);
            }
            else
            {
                viewModel.FocusIssue(issue);
            }

            e.Handled = true;
            return;
        }

        Action<object>? extendSelection = null;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            extendSelection = target =>
            {
                if (target is IssueCardSample targetIssue)
                {
                    viewModel.ToggleIssueSelection(targetIssue, isShiftHeld: true);
                }
            };
        }

        if (control.FindAncestorOfType<ItemsControl>() is { } list && GridKeyboardNavigation.TryHandleArrowKey(list, control, e.Key, extendSelection))
        {
            e.Handled = true;
        }
    }
}
