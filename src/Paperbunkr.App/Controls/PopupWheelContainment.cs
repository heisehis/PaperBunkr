using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Keeps a mouse-wheel turn inside the popup it happened in. A popup's host (<see cref="OverlayPopupHost"/> with the app's
/// <c>Win32PlatformOptions.OverlayPopups</c>, <see cref="PopupRoot"/> otherwise) routes events on to its logical parent, the
/// <see cref="Popup"/> control, which sits in the page under the dropdown's anchor. A scroll viewer only marks the wheel handled while it
/// can still move, so once a dropdown list reached its end the next notch bubbled out of the popup into the page's own scroll viewer and
/// the whole page scrolled behind the open dropdown (reported 2026-11-12). Marking the wheel handled at the popup's root - after every
/// scroll viewer inside the popup has had it - stops it there for every dropdown, menu and flyout at once.
/// </summary>
public static class PopupWheelContainment
{
    private static bool _installed;

    /// <summary>Registers the class handlers. Idempotent; called once at startup (and by tests).</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        InputElement.PointerWheelChangedEvent.AddClassHandler<OverlayPopupHost>(Contain, RoutingStrategies.Bubble);
        InputElement.PointerWheelChangedEvent.AddClassHandler<PopupRoot>(Contain, RoutingStrategies.Bubble);
    }

    private static void Contain(object? sender, PointerWheelEventArgs e) => e.Handled = true;
}
