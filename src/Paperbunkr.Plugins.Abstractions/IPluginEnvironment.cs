using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.Plugins;

/// <summary>
/// Ported from ComicRackCE's <c>IPluginEnvironment</c> (docs/superpowers/specs/
/// 2026-08-24-plugin-api-v2-design.md §4) - same shape, Avalonia-appropriate member types.
/// Extended by Plugin API v3 (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md)
/// with <see cref="Metadata"/> (§2), <see cref="Rules"/> (§4) and <see cref="Writer"/> (§5).
/// </summary>
public interface IPluginEnvironment : IPluginConfig, ICloneable
{
    IPluginHostWindow MainWindow { get; }

    IApplication App { get; }

    IOpenBooksManager OpenBooks { get; }

    IBrowser Browser { get; }

    IComicDisplay ComicDisplay { get; }

    /// <summary>Read access to the relationship/event/continuity/age graph (Plugin API v3 §2).</summary>
    IMetadataGraph Metadata { get; }

    /// <summary>Runs the app's own Smart List matcher for the plugin (Plugin API v3 §4).</summary>
    IRulesEngine Rules { get; }

    /// <summary>Curated, audited per-field metadata write surface (Plugin API v3 §5).</summary>
    IMetadataWriter Writer { get; }

    /// <summary>Folder the currently-executing command's script/manifest lives in; set on the per-command clone by <see cref="Command.Initialize"/>.</summary>
    string CommandPath { get; set; }

    /// <summary>Key of the plugin owning the currently-executing command; set on the per-command clone by <see cref="Command.Initialize"/>. Scopes <see cref="IPluginConfig.GetSetting"/>/<see cref="IPluginConfig.SetSetting"/>.</summary>
    string PluginKey { get; set; }

    IThemePlugin ThemePlugin { get; }

    /// <summary>No localization pipeline exists yet - ships as a documented pass-through (returns <paramref name="text"/> unchanged). Shape kept for future-proofing only.</summary>
    string Localize(string resourceKey, string elementKey, string text);
}
