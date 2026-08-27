namespace Paperbunkr.Data.Entities;

/// <summary>
/// One user-toggled override of a plugin command's default enabled state (docs/superpowers/specs/
/// 2026-08-24-plugin-api-v2-design.md §3). Follows <see cref="KeyBinding"/>'s sparse-table
/// convention: a command with no row here uses its manifest's own <c>enabled</c> default; only
/// commands the user has explicitly toggled get a row. Loading plugins never needs a migration -
/// only adding a new *kind* of override would.
/// </summary>
public class PluginCommandState
{
    public int Id { get; set; }

    /// <summary>Matches the plugin manifest's <c>Plugin/@key</c> attribute (falls back to the plugin's folder name when a manifest doesn't declare one).</summary>
    public string PluginKey { get; set; } = string.Empty;

    /// <summary>Matches the plugin manifest's <c>Command/@key</c> attribute.</summary>
    public string CommandKey { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}
