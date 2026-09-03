using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// Code-behind for the Ctrl+P command palette (docs/superpowers/specs/2026-09-03-quick-open-command-
/// palette-design.md). Relays search-box arrow / enter keys into the ViewModel - the same pattern
/// <c>LibraryToolbar</c>'s search-suggestions box uses - and focuses the box each time the palette
/// opens (via the ViewModel's <see cref="QuickOpenViewModel.Opened"/> event, since the overlay
/// itself never leaves the visual tree - only its parent's <c>IsVisible</c> toggles).
/// </summary>
public partial class QuickOpenOverlay : UserControl
{
    private QuickOpenViewModel? _subscribed;

    public QuickOpenOverlay()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_subscribed is not null)
            {
                _subscribed.Opened -= FocusSearchBox;
            }

            _subscribed = DataContext as QuickOpenViewModel;
            if (_subscribed is not null)
            {
                _subscribed.Opened += FocusSearchBox;
            }
        };
    }

    private void FocusSearchBox() => Dispatcher.UIThread.Post(() => SearchBox.Focus());

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not QuickOpenViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                vm.ActivateSelected();
                e.Handled = true;
                break;
        }
    }

    private void OnResultTapped(object? sender, TappedEventArgs e)
    {
        (DataContext as QuickOpenViewModel)?.ActivateSelected();
    }
}
