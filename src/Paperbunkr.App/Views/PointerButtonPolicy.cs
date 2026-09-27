using Avalonia;
using Avalonia.Input;

namespace Paperbunkr.App.Views;

/// <summary>What a pointer press means to <see cref="PageCanvas"/> (docs/superpowers/specs/2026-09-25-comic-reader-reach-design.md §1).</summary>
public enum PointerRole
{
    /// <summary>Left mouse button, touch contact or pen tip: may drag-pan, double-click zoom and hit tap zones.</summary>
    Primary,

    /// <summary>Right button: only ever opens the context menu (handled by <c>ContextMenuHost</c>), never an action here.</summary>
    Secondary,

    /// <summary>Middle button: does nothing.</summary>
    Middle,

    /// <summary>Mouse "back" side button (XButton1): previous page.</summary>
    Back,

    /// <summary>Mouse "forward" side button (XButton2): next page.</summary>
    Forward,

    Other,
}

/// <summary>
/// Pure decisions about pointer presses. <c>PageCanvas.OnPointerPressed</c> used to ignore which button was pressed, so a right or middle press started a drag, a
/// page-turn zone or a double-click zoom; this classifies the press first so only <see cref="PointerRole.Primary"/> presses do those things.
/// </summary>
internal static class PointerButtonPolicy
{
    /// <summary>Distance a press may move and still count as a tap (continuous mode has no other way to tell a click from a drag).</summary>
    public const double TapMaxDistance = 6;

    /// <summary>Longest press that still counts as a tap.</summary>
    public const double TapMaxDurationMs = 400;

    public static PointerRole Classify(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => PointerRole.Primary,
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => PointerRole.Secondary,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => PointerRole.Middle,
        PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton1Released => PointerRole.Back,
        PointerUpdateKind.XButton2Pressed or PointerUpdateKind.XButton2Released => PointerRole.Forward,
        _ => PointerRole.Other,
    };

    /// <summary>Whether the press may start a drag, a zone tap, a double-click zoom or a flick.</summary>
    public static bool MayAct(PointerRole role) => role == PointerRole.Primary;

    /// <summary>A press-release pair is a tap when it stayed within <see cref="TapMaxDistance"/> and lasted at most <see cref="TapMaxDurationMs"/>.</summary>
    public static bool IsTap(Vector movement, double elapsedMs) =>
        elapsedMs <= TapMaxDurationMs && (movement.X * movement.X) + (movement.Y * movement.Y) <= TapMaxDistance * TapMaxDistance;
}
