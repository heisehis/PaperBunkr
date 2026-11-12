using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views.Preferences;

/// <summary>Appearance > Personalize (docs/superpowers/specs/2026-11-06-personalization-design.md). DataContext is a <see cref="PersonalizeViewModel"/>.</summary>
public partial class PersonalizeGroup : UserControl
{
    public PersonalizeGroup()
    {
        InitializeComponent();
        // The flyout's hex field commits on Enter and closes on Escape (text-box commit/cancel stays the control's own key handling).
        AccentHexBox.AddHandler(KeyDownEvent, OnHexKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnHexKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not PersonalizeViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.ApplyCustomAccentCommand.Execute(null);
            if (vm.CustomAccentError is null)
            {
                // Still routing through a control inside the popup: close it after the event has finished.
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
