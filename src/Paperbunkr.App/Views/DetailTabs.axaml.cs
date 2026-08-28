using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class DetailTabs : UserControl
{
    public DetailTabs()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Issue tile click-to-select (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md
    /// §1) - direct pointer-event handling, same as <see cref="PageCanvas"/>, since Shift-range
    /// selection needs <see cref="PointerEventArgs.KeyModifiers"/> that a plain Button/ICommand
    /// binding can't carry. Only the left button toggles selection; right-clicks fall through
    /// untouched so the tile's own <c>ContextMenu</c> still opens normally. Attached in all three
    /// Issues-tab view-mode templates (Poster/List/Card) - <c>sender</c> is whichever tile control
    /// carries the <see cref="IssueCardSample"/> DataContext.
    /// </summary>
    private void OnIssueTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (sender is Control { DataContext: IssueCardSample issue } && DataContext is DetailTabsViewModel viewModel)
        {
            viewModel.ToggleIssueSelection(issue, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
    }

    /// <summary>
    /// Keyboard equivalent of <see cref="OnIssueTilePointerPressed"/> (P5, docs/alpha-roadmap.md) -
    /// Enter/Space toggles the focused tile, Shift held extends the range. Other arrow/Home/End
    /// keys delegate to <see cref="GridKeyboardNavigation"/> for spatial 2D movement, resolving the
    /// active view mode's own <c>ItemsControl</c> from the focused tile rather than a fixed name.
    /// </summary>
    private void OnIssueTileKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: IssueCardSample issue } control || DataContext is not DetailTabsViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            viewModel.ToggleIssueSelection(issue, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
            return;
        }

        if (control.FindAncestorOfType<ItemsControl>() is { } list && GridKeyboardNavigation.TryHandleArrowKey(list, issue, e.Key))
        {
            e.Handled = true;
        }
    }
}
