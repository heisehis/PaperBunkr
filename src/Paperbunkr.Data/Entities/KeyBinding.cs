namespace Paperbunkr.Data.Entities;

/// <summary>
/// One user-remapped keyboard command (Preferences &gt; Keyboard Shortcuts, docs/alpha-roadmap.md
/// P5 follow-up - "when we add something new, we can also set up navigation and shortcuts for
/// them"). Only remapped commands get a row - a command with no row here just uses its
/// registry-defined default (<c>Paperbunkr.App.Models.KeyboardCommandRegistry</c>), so adding a
/// new remappable command elsewhere in the app never needs a migration, only a new registry entry.
/// A plain EF table rather than living on <see cref="AppSettings"/>'s singleton row - this is a
/// growable list keyed by command, not a fixed set of named toggles.
/// </summary>
public class KeyBinding
{
    public int Id { get; set; }

    /// <summary>Stable identifier matching a <c>Paperbunkr.App.Models.KeyboardCommandRegistry</c> entry, e.g. "Reader.PageTurnLeft".</summary>
    public string CommandId { get; set; } = string.Empty;

    /// <summary><c>Avalonia.Input.KeyGesture</c> string form of the remapped key, e.g. "Left" or "Shift+R".</summary>
    public string Key { get; set; } = string.Empty;
}
