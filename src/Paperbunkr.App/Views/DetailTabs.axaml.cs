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
}
