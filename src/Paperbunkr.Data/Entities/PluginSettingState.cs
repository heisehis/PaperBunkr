namespace Paperbunkr.Data.Entities;

/// <summary>
/// One persisted plugin setting (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-
/// design.md §6). Same sparse-table convention as <see cref="PluginCommandState"/>: a plugin that
/// never calls <c>SetSetting</c> has no rows here. Scoped by <see cref="PluginKey"/> so one plugin
/// can't read or overwrite another's settings by construing a <see cref="Key"/> collision.
/// </summary>
public class PluginSettingState
{
    public int Id { get; set; }

    /// <summary>Matches the plugin manifest's <c>Plugin/@key</c> attribute (falls back to the plugin's folder name when a manifest doesn't declare one) — same value as <see cref="PluginCommandState.PluginKey"/>.</summary>
    public string PluginKey { get; set; } = string.Empty;

    /// <summary>Plugin-defined setting name.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Raw string value — the plugin owns parsing/formatting it.</summary>
    public string Value { get; set; } = string.Empty;
}
