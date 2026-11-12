using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class CollectionPropertiesOverlay : UserControl
{
    public CollectionPropertiesOverlay()
    {
        InitializeComponent();
        // The hex field of the custom-colour flyout commits on Enter and closes on Escape (text-box commit/cancel stays the control's own key handling).
        CustomHexBox.AddHandler(KeyDownEvent, OnHexKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnHexKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CollectionPropertiesScreenViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.ApplyCustomAccentCommand.Execute(null);
            if (vm.CustomAccentError is null)
            {
                // The key is still routing through a control inside the popup: close it after the event has finished.
                Dispatcher.UIThread.Post(() => vm.IsColorFlyoutOpen = false);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Dispatcher.UIThread.Post(() => vm.IsColorFlyoutOpen = false);
            e.Handled = true;
        }
    }
}
