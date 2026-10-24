using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Controls;

/// <summary>
/// <c>controls:PopupFit.ToWindow="True"</c> on a dropdown <see cref="Popup"/> caps its content at the room left in the window below (or above)
/// its anchor, so a long menu scrolls instead of running past the window's edge (the Library Workspaces menu ran over the taskbar with a fixed
/// MaxHeight of 620; 2026-10-20 UI polish). The content needs a ScrollViewer inside to scroll.
/// </summary>
public static class PopupFit
{
    /// <summary>Space kept clear between the popup and the window edge.</summary>
    public const double EdgeMargin = 12;

    /// <summary>A popup never shrinks below this, so a cramped window still shows a few rows.</summary>
    public const double MinHeight = 160;

    public static readonly AttachedProperty<bool> ToWindowProperty =
        AvaloniaProperty.RegisterAttached<Popup, bool>("ToWindow", typeof(PopupFit));

    static PopupFit()
    {
        ToWindowProperty.Changed.AddClassHandler<Popup>((popup, e) =>
        {
            popup.Opened -= OnOpened;
            if (e.NewValue is true)
            {
                popup.Opened += OnOpened;
            }
        });
    }

    public static bool GetToWindow(Popup popup) => popup.GetValue(ToWindowProperty);

    public static void SetToWindow(Popup popup, bool value) => popup.SetValue(ToWindowProperty, value);

    /// <summary>The height left for a popup anchored at <paramref name="anchorTop"/>..<paramref name="anchorBottom"/> in a window
    /// <paramref name="windowHeight"/> tall: the larger of the space below and above, since the popup flips when there is more room above.</summary>
    public static double AvailableHeight(double windowHeight, double anchorTop, double anchorBottom, double offset)
    {
        double below = windowHeight - anchorBottom - offset - EdgeMargin;
        double above = anchorTop - offset - EdgeMargin;
        return Math.Max(MinHeight, Math.Max(below, above));
    }

    private static void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Popup { Child: Layoutable child } popup
            || (popup.PlacementTarget ?? popup.Parent as Visual) is not Visual anchor
            || TopLevel.GetTopLevel(anchor) is not { } topLevel
            || anchor.TransformToVisual(topLevel) is not { } transform)
        {
            return;
        }

        Rect bounds = new Rect(anchor.Bounds.Size).TransformToAABB(transform);
        child.MaxHeight = AvailableHeight(topLevel.ClientSize.Height, bounds.Top, bounds.Bottom, popup.VerticalOffset);
    }
}
