using System.Collections.Generic;
using Avalonia.Input;

namespace Paperbunkr.App.Models;

/// <summary>Picker option for a remappable key binding (Preferences &gt; Keyboard Shortcuts, docs/alpha-roadmap.md P5 follow-up).</summary>
public readonly record struct KeyOption(KeyGesture Gesture, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Curated candidate gestures offered to every remappable command in <see cref="KeyboardCommandRegistry"/>
/// - deliberately a fixed list, not a live "press any key" capture widget like CE's own
/// <c>KeyboardShortcutEditor</c>; a capture-any-key editor is more machinery than the small,
/// still-growing set of real remappable commands justifies today. Every option here is either an
/// unmodified key or a Shift+key combo, so there's no conflict with app-wide shortcuts
/// (Tab/Enter/Space/Esc) or text entry elsewhere in the app.
/// </summary>
public static class KeyOptions
{
    public static readonly IReadOnlyList<KeyOption> All =
    [
        new KeyOption(new KeyGesture(Key.Left), "Left Arrow"),
        new KeyOption(new KeyGesture(Key.Right), "Right Arrow"),
        new KeyOption(new KeyGesture(Key.Up), "Up Arrow"),
        new KeyOption(new KeyGesture(Key.Down), "Down Arrow"),
        new KeyOption(new KeyGesture(Key.A), "A"),
        new KeyOption(new KeyGesture(Key.D), "D"),
        new KeyOption(new KeyGesture(Key.J), "J"),
        new KeyOption(new KeyGesture(Key.K), "K"),
        new KeyOption(new KeyGesture(Key.OemComma), "Comma ( , )"),
        new KeyOption(new KeyGesture(Key.OemPeriod), "Period ( . )"),
        new KeyOption(new KeyGesture(Key.PageUp), "Page Up"),
        new KeyOption(new KeyGesture(Key.PageDown), "Page Down"),
        new KeyOption(new KeyGesture(Key.Home), "Home"),
        new KeyOption(new KeyGesture(Key.End), "End"),
        new KeyOption(new KeyGesture(Key.F), "F"),
        new KeyOption(new KeyGesture(Key.R), "R"),
        new KeyOption(new KeyGesture(Key.R, KeyModifiers.Shift), "Shift+R"),
        new KeyOption(new KeyGesture(Key.Z), "Z"),
        new KeyOption(new KeyGesture(Key.Z, KeyModifiers.Shift), "Shift+Z"),
        new KeyOption(new KeyGesture(Key.D1), "1"),
        new KeyOption(new KeyGesture(Key.D2), "2"),
        new KeyOption(new KeyGesture(Key.D3), "3"),
        new KeyOption(new KeyGesture(Key.D4), "4"),
        new KeyOption(new KeyGesture(Key.D5), "5"),
        new KeyOption(new KeyGesture(Key.S), "S"),
    ];
}
