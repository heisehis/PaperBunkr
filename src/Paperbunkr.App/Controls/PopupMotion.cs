using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Paperbunkr.App.Controls;

/// <summary>
/// Entrance for every popup in the app - dropdowns, SuggestBox lists, context menus, flyouts (docs/superpowers/specs/2026-10-20-motion-
/// system-v2-foundation-overlays-design.md §5.4, Q11). When a <see cref="Popup"/> opens, its child fades in and travels 4 px away from the
/// anchor on the Reveal tokens: down when the popup sits below its anchor, up when it sits above (class <c>popupEnter</c>, then
/// <c>popupEntered</c> a tick later so the transition sees the change; styles at the end of Styles/Controls.axaml). Closing stays Avalonia's
/// immediate removal, as ComicRack CE's menus follow the Windows menu animation: a short show, no lingering close.
/// Tooltips keep the Fluent default. <c>controls:PopupMotion.Enter="False"</c> on a Popup opts it out.
/// </summary>
public static class PopupMotion
{
    public static readonly AttachedProperty<bool> EnterProperty =
        AvaloniaProperty.RegisterAttached<Popup, bool>("Enter", typeof(PopupMotion), defaultValue: true);

    private static bool s_installed;

    public static bool GetEnter(Popup popup) => popup.GetValue(EnterProperty);

    public static void SetEnter(Popup popup, bool value) => popup.SetValue(EnterProperty, value);

    /// <summary>Hooks every Popup in the process. Idempotent.</summary>
    public static void Install()
    {
        if (s_installed)
        {
            return;
        }

        s_installed = true;
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((popup, e) =>
        {
            if (e.NewValue is true && GetEnter(popup) && popup.Child is Control child and not ToolTip)
            {
                Play(child, OpensUpward(popup.Placement));
            }
        });
    }

    /// <summary>Whether a popup with this placement sits above its anchor (so it rises into place rather than dropping).</summary>
    internal static bool OpensUpward(PlacementMode placement) =>
        placement is PlacementMode.Top or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight;

    internal static void Play(Control child, bool upward)
    {
        child.Classes.Remove("popupEntered");
        child.Classes.Set("popupEnter", true);
        child.Classes.Set("popupUp", upward);

        // A tick later, so the transitions observe the hidden start state before it changes (as ToastPresenter does).
        Dispatcher.UIThread.Post(() => child.Classes.Add("popupEntered"));
    }
}
