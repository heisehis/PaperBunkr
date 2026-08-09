using Avalonia.Controls;
using Avalonia.Input;
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
    /// untouched so the tile's own <c>ContextMenu</c> still opens normally.
    /// </summary>
    private void OnIssueTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (sender is Border { DataContext: IssueCardSample issue } && DataContext is DetailTabsViewModel viewModel)
        {
            viewModel.ToggleIssueSelection(issue, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
    }

    /// <summary>
    /// Keyboard equivalent of <see cref="OnIssueTilePointerPressed"/> (P5, docs/alpha-roadmap.md) -
    /// Enter/Space toggles the focused tile, Shift held extends the range exactly like a
    /// shift-click does, since <see cref="DetailTabsViewModel.ToggleIssueSelection"/> already
    /// takes that as a plain bool and doesn't care where it came from. Other arrow/Home/End keys
    /// delegate to <see cref="GridKeyboardNavigation"/> for spatial 2D movement through the
    /// <c>WrapPanel</c>-backed issue grid (docs/superpowers/specs/
    /// 2026-08-09-reader-gestures-and-grid-navigation-design.md).
    /// </summary>
    private void OnIssueTileKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Border { DataContext: IssueCardSample issue } || DataContext is not DetailTabsViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter || e.Key == Key.Space)
        {
            viewModel.ToggleIssueSelection(issue, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
            return;
        }

        if (GridKeyboardNavigation.TryHandleArrowKey(IssuesList, issue, e.Key))
        {
            e.Handled = true;
        }
    }
}
