using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real <see cref="IPluginEnvironment"/> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
/// §4). <see cref="Clone"/> is shallow by design, matching CE's own <c>env.Clone()</c> usage in
/// <c>Command.Initialize</c> - every sub-interface adapter is a shared, app-lifetime singleton
/// (constructed once by <see cref="PluginHostService"/>), only <see cref="CommandPath"/> differs
/// per command clone.
/// </summary>
public sealed class PaperbunkrPluginEnvironment : IPluginEnvironment
{
    public required IPluginHostWindow MainWindow { get; init; }

    public required IApplication App { get; init; }

    public required IOpenBooksManager OpenBooks { get; init; }

    public required IBrowser Browser { get; init; }

    public required IComicDisplay ComicDisplay { get; init; }

    public required IThemePlugin ThemePlugin { get; init; }

    public string CommandPath { get; set; } = string.Empty;

    public IEnumerable<string> LibraryPaths
    {
        get
        {
            using var context = PaperbunkrDb.CreateContext();
            return context.WatchedFolders.Select(f => f.Path).ToList();
        }
    }

    /// <summary>No localization pipeline exists yet - documented pass-through (docs §4).</summary>
    public string Localize(string resourceKey, string elementKey, string text) => text;

    public object Clone() => (PaperbunkrPluginEnvironment)MemberwiseClone();
}
