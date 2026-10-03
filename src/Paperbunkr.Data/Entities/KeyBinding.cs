namespace Paperbunkr.Data.Entities;

/// <summary>
/// LEGACY. One user-remapped keyboard command from the keyboard-shortcut system that preceded the application-wide input service
/// (docs/superpowers/specs/2026-10-03-input-service-design.md). Remaps now live in <c>keymap.json</c> next to the database (keyboard, mouse, wheel and
/// gamepad bindings alike); on the first launch after that change the rows here are imported once by <c>LegacyKeyBindingImporter</c> and are never written
/// again. The table is deliberately left in place rather than dropped: other checkouts and worktrees share a development database, and a migration that
/// removed it would break any of them still running the previous build. A later release can drop it.
/// </summary>
public class KeyBinding
{
    public int Id { get; set; }

    /// <summary>Stable identifier of the action this row remapped, e.g. "Reader.PageTurnLeft" (the same id the input service uses).</summary>
    public string CommandId { get; set; } = string.Empty;

    /// <summary><c>Avalonia.Input.KeyGesture</c> string form of the remapped key, e.g. "Left" or "Shift+R".</summary>
    public string Key { get; set; } = string.Empty;
}
