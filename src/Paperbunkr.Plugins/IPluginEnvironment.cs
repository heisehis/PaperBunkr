using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.Plugins;

/// <summary>
/// Ported from ComicRackCE's <c>IPluginEnvironment</c> (docs/superpowers/specs/
/// 2026-08-24-plugin-api-v2-design.md §4) - same shape, Avalonia-appropriate member types.
/// </summary>
public interface IPluginEnvironment : IPluginConfig, ICloneable
{
    IPluginHostWindow MainWindow { get; }

    IApplication App { get; }

    IOpenBooksManager OpenBooks { get; }

    IBrowser Browser { get; }

    IComicDisplay ComicDisplay { get; }

    /// <summary>Folder the currently-executing command's script/manifest lives in; set on the per-command clone by <see cref="Command.Initialize"/>.</summary>
    string CommandPath { get; set; }

    IThemePlugin ThemePlugin { get; }

    /// <summary>No localization pipeline exists yet - ships as a documented pass-through (returns <paramref name="text"/> unchanged). Shape kept for future-proofing only.</summary>
    string Localize(string resourceKey, string elementKey, string text);
}
