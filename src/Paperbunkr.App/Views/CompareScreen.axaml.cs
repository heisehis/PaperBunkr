using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// The Compare screen (docs/superpowers/specs/2026-09-26-comic-reader-compare-design.md #11). Its keys are local to the screen and caught on the way down (Tunnel) so the page canvases cannot swallow them:
/// Left/Right and PageUp/PageDown turn A's page, Space or F flips the flicker, M switches the mode, 1 and 2 keep A or B. Escape is the shell's.
/// </summary>
public partial class CompareScreen : UserControl
{
    public CompareScreen()
    {
        InitializeComponent();
        Focusable = true;
        FocusAdorner = null;
        AddHandler(KeyDownEvent, OnCompareKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Focus();
    }

    private void OnCompareKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CompareScreenViewModel vm || e.KeyModifiers != KeyModifiers.None || e.Source is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Right or Key.PageDown:
                vm.NextPageCommand.Execute(null);
                break;
            case Key.Left or Key.PageUp:
                vm.PreviousPageCommand.Execute(null);
                break;
            case Key.Home:
                vm.GoToFirstPageCommand.Execute(null);
                break;
            case Key.Space or Key.F:
                vm.FlipCommand.Execute(null);
                break;
            case Key.M:
                vm.ToggleModeCommand.Execute(null);
                break;
            case Key.D0:
                vm.ResetViewCommand.Execute(null);
                break;
            case Key.D1:
                vm.KeepACommand.Execute(null);
                break;
            case Key.D2:
                vm.KeepBCommand.Execute(null);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
