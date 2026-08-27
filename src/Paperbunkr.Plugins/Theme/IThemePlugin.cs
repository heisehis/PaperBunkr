namespace Paperbunkr.Plugins.Theme;

/// <summary>
/// Replaces CE's WinForms-only <c>IThemePlugin</c> (<c>ToolStripRenderer</c>, <c>ITheme</c>).
/// No dark-mode flag - Paperbunkr's skin system doesn't track a light/dark axis; skins are
/// arbitrary token sets, not a binary (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §4).
/// </summary>
public interface IThemePlugin
{
    /// <summary>Key of the currently active skin - "default" or an installed .crpck's key, mirrors <c>AppSettings.ActiveSkinKey</c>.</summary>
    string CurrentSkinKey { get; }
}
