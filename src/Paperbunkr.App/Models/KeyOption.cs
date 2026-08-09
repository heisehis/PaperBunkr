using System.Collections.Generic;
using Avalonia.Input;

namespace Paperbunkr.App.Models;

/// <summary>Picker option for a remappable single-key binding (Preferences &gt; Keyboard Shortcuts, docs/alpha-roadmap.md P5 follow-up).</summary>
public readonly record struct KeyOption(Key Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Curated candidate keys offered to every remappable command in <see cref="KeyboardCommandRegistry"/>
/// - deliberately a fixed list, not a live "press any key" capture widget like CE's own
/// <c>KeyboardShortcutEditor</c>; a capture-any-key editor is more machinery than the small,
/// still-growing set of real remappable commands justifies today. Every option here is a plain,
/// unmodified key so there's no conflict with app-wide shortcuts (Tab/Enter/Space/Esc) or text
/// entry elsewhere in the app.
/// </summary>
public static class KeyOptions
{
    public static readonly IReadOnlyList<KeyOption> All =
    [
        new KeyOption(Key.Left, "Left Arrow"),
        new KeyOption(Key.Right, "Right Arrow"),
        new KeyOption(Key.Up, "Up Arrow"),
        new KeyOption(Key.Down, "Down Arrow"),
        new KeyOption(Key.A, "A"),
        new KeyOption(Key.D, "D"),
        new KeyOption(Key.J, "J"),
        new KeyOption(Key.K, "K"),
        new KeyOption(Key.OemComma, "Comma ( , )"),
        new KeyOption(Key.OemPeriod, "Period ( . )"),
        new KeyOption(Key.PageUp, "Page Up"),
        new KeyOption(Key.PageDown, "Page Down"),
    ];
}
