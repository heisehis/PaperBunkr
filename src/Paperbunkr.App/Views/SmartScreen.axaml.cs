using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class SmartScreen : UserControl
{
    private readonly TypeAheadSearch.Buffer _typeAheadBuffer = new();

    public SmartScreen()
    {
        InitializeComponent();
        // Type-ahead (docs/superpowers/specs/2026-09-12-grid-typeahead-rangeselect-quit-design.md) -
        // no multi-select here to clear (this screen is read-only browse, unlike Library/Books), so
        // the clear-selection callback is a no-op.
        AddHandler(TextInputEvent, OnScreenTextInput, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnScreenKeyDownForBackspace, RoutingStrategies.Tunnel);
    }

    private bool HandleTypeAhead(char typedChar, object? source)
    {
        if (source is not Control control || control.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
        {
            return false;
        }

        return TypeAheadSearch.TryHandleTextInput<object>(
            _typeAheadBuffer, typedChar, itemsControl,
            item => item switch { IssueCardSample issue => issue.Title, SeriesCardSample card => card.Name, BookCardSample book => book.Title, _ => string.Empty },
            clearSelection: () => { });
    }

    private void OnScreenTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source is TextBox || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        bool handled = false;
        foreach (char c in e.Text)
        {
            handled = HandleTypeAhead(c, e.Source);
        }

        if (handled)
        {
            e.Handled = true;
        }
    }

    private void OnScreenKeyDownForBackspace(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None && e.Source is not TextBox && HandleTypeAhead('\b', e.Source))
        {
            e.Handled = true;
        }
    }

    /// <summary>Backdrop click-to-close for the Grouped Review overlay (docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-scan-alerts-design.md §3) - same pattern as LibraryScreen's Add-issue overlay backdrop.</summary>
    private void OnGroupedReviewBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SmartScreenViewModel vm)
        {
            vm.CloseGroupedReviewCommand.Execute(null);
        }
    }

    /// <summary>
    /// Spatial arrow-key navigation across the virtualized results grid (docs/superpowers/specs/
    /// 2026-08-31-keyboard-operability-design.md). <see cref="GridKeyboardNavigation.TryHandleArrowKey"/>
    /// now handles virtualized panels itself (generalized from this handler's own original
    /// implementation, once Library's main card grid turned out to need the exact same fix - see
    /// that method's own doc comment), so this is just the standard wiring every other grid uses.
    /// </summary>
    private void OnResultCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control fromControl ||
            fromControl.FindAncestorOfType<ItemsControl>() is not { } itemsControl)
        {
            return;
        }

        if (GridKeyboardNavigation.TryHandleArrowKey(itemsControl, fromControl, e.Key))
        {
            e.Handled = true;
        }
    }
}
