using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Controls;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views.Preferences;

/// <summary>
/// Preferences &gt; Keyboard Shortcuts (docs/superpowers/specs/2026-10-04-keyboard-shortcuts-master-detail-design.md): a grouped action list beside an editor for the selected action.
/// The code-behind only does what XAML cannot: stacks the two panes in a narrow window, selects a row as it takes focus (so arrowing down the list updates the editor), and moves focus
/// between the list and the editor with Right and Left.
/// </summary>
public partial class KeyboardShortcutsSection : UserControl
{
    /// <summary>Below this width the editor drops under the list instead of sitting beside it.</summary>
    private const double NarrowWidth = 760;

    /// <summary>Height of the list when it is stacked above the editor in a narrow window.</summary>
    private const double StackedListHeight = 320;

    private PreferencesScreenViewModel? _vm;
    private bool? _narrow;

    public KeyboardShortcutsSection()
    {
        InitializeComponent();
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width < NarrowWidth);
        DataContextChanged += OnDataContextChanged;

        // Taking focus selects the row, so Up and Down through the list drive the editor. The select can rebuild the list, but the editor posts that rebuild to run after this event.
        AddHandler(GotFocusEvent, OnRowGotFocus);

        // Avalonia's ItemsControl runs its own arrow-key navigation over the list's containers ahead of the screen's bubbling handler; the list takes the directional move first instead
        // (the same reason WantedScreen tunnels it). Right on a row goes into the editor.
        GroupList.AddHandler(KeyDownEvent, OnListKeyDownTunnel, RoutingStrategies.Tunnel);

        // Left in the editor goes back to the selected row. It bubbles from the focused control, so a capture box (which handles every key itself) never reaches it.
        PaneScroll.AddHandler(KeyDownEvent, OnPaneKeyDown);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Shortcuts.PropertyChanged -= OnEditorPropertyChanged;
        }

        _vm = DataContext as PreferencesScreenViewModel;
        if (_vm is not null)
        {
            _vm.Shortcuts.PropertyChanged += OnEditorPropertyChanged;
        }
    }

    private void ApplyLayout(bool narrow)
    {
        if (_narrow == narrow)
        {
            return;
        }

        _narrow = narrow;
        if (narrow)
        {
            Body.ColumnDefinitions = new ColumnDefinitions("*");
            Body.RowDefinitions = new RowDefinitions("Auto,16,Auto");
            SetCell(ListPanel, row: 0, column: 0);
            SetCell(PanePanel, row: 2, column: 0);
            ListScroll.Height = StackedListHeight;
            PageScroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        }
        else
        {
            Body.RowDefinitions = new RowDefinitions("*");
            Body.ColumnDefinitions = new ColumnDefinitions("*,16,1.3*");
            SetCell(ListPanel, row: 0, column: 0);
            SetCell(PanePanel, row: 0, column: 2);
            ListScroll.Height = double.NaN;
            PageScroll.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        }
    }

    private static void SetCell(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
    }

    private void OnRowGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (_vm is not null && e.Source is Button { DataContext: ShortcutRowViewModel row } button && button.Classes.Contains("ksRow"))
        {
            _vm.Shortcuts.SelectRow(row);
        }
    }

    private void OnListKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Source is not Visual source || source.FindAncestorOfType<Button>(includeSelf: true) is not { } button
            || !button.Classes.Contains("ksRow"))
        {
            return;
        }

        if (e.Key == Key.Right)
        {
            e.Handled = FocusReclaimer.FocusFirstButton(PaneScroll);
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            // The screen-wide directional search treats the Preferences sidebar's items as rows at the same height, so it would hop out of the list; step through the list's own rows instead.
            var rows = GroupList.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ksRow") && b.IsEffectivelyVisible).ToList();
            int step = e.Key == Key.Down ? 1 : -1;
            int target = rows.IndexOf(button) + step;
            if (target >= 0 && target < rows.Count)
            {
                rows[target].Focus(NavigationMethod.Directional);
                rows[target].BringIntoView();
                e.Handled = true;
            }
            else if (target < 0)
            {
                // Up from the first row goes back to the search box, which only browses until Enter, F2 or a click (see TextEntryMode).
                SearchBox.Focus(NavigationMethod.Directional);
                e.Handled = true;
            }
        }
    }

    private void OnPaneKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Left || e.KeyModifiers != KeyModifiers.None || e.Source is not Visual source
            || source.GetSelfAndVisualAncestors().Any(a => a is BindingCaptureBox or TextBox))
        {
            return;
        }

        e.Handled = FocusSelectedRow();
    }

    private void OnEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShortcutsEditorViewModel.SelectedRow))
        {
            return;
        }

        // Once the pane has swapped to the new action (and the list has rebuilt, if it needed to), keep that action's row in view. When the selection came from the "Show that action" link,
        // the link has just left the tree and focus is nowhere, so land it on the row instead of dropping the keyboard user at the top of the page.
        Dispatcher.UIThread.Post(() =>
        {
            if (FindSelectedRowButton() is not { } button)
            {
                return;
            }

            button.BringIntoView();
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null)
            {
                button.Focus(NavigationMethod.Directional);
            }
        }, DispatcherPriority.Background);
    }

    private bool FocusSelectedRow()
    {
        if (FindSelectedRowButton() is not { } button)
        {
            return false;
        }

        button.Focus(NavigationMethod.Directional);
        return true;
    }

    private Button? FindSelectedRowButton() =>
        GroupList.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("ksRow") && ReferenceEquals(b.DataContext, _vm?.Shortcuts.SelectedRow));
}
