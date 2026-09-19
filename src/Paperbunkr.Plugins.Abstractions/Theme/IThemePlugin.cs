namespace Paperbunkr.Plugins.Theme;

/// <summary>
/// Replaces CE's WinForms-only <c>IThemePlugin</c> (<c>ToolStripRenderer</c>, <c>ITheme</c>).
/// (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md §4). The "no dark-mode flag" note
/// this doc comment originally carried is no longer accurate as of
/// docs/superpowers/specs/2026-09-16-theme-system-design.md - the theme system now tracks a real
/// Light/Dark <c>mode</c> per theme and drives <c>Application.RequestedThemeVariant</c> from it.
/// No plugin-facing mode property was added here though - no real plugin consumes this interface
/// yet (only <c>PaperbunkrThemePlugin</c>, the adapter, and test doubles), so extending the public
/// contract stays deferred until an actual plugin needs it, rather than speculatively adding one now.
/// </summary>
public interface IThemePlugin
{
    /// <summary>Key of the currently active theme - "default" or an installed .crpck's key, mirrors <c>AppSettings.ActiveThemeKey</c>. Renamed from <c>CurrentSkinKey</c> by docs/superpowers/specs/2026-09-16-theme-system-design.md.</summary>
    string CurrentThemeKey { get; }
}
