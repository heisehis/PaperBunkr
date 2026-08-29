using System.Collections.Generic;
using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Automation;
using Paperbunkr.Plugins.Theme;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real <see cref="IPluginEnvironment"/> (docs/superpowers/specs/2026-08-24-plugin-api-v2-design.md
/// §4, extended by 2026-08-28-plugin-api-v3-data-manager-design.md). <see cref="Clone"/> is shallow
/// by design, matching CE's own <c>env.Clone()</c> usage in <c>Command.Initialize</c> - every
/// sub-interface adapter is a shared, app-lifetime singleton (constructed once by
/// <see cref="PluginHostService"/>), only <see cref="CommandPath"/> / <see cref="PluginKey"/>
/// differ per command clone. The v3 write/confirm state is per-invocation, carried by
/// <see cref="PluginInvocationContext"/>, not by the clone.
/// </summary>
public sealed class PaperbunkrPluginEnvironment : IPluginEnvironment
{
    public required IPluginHostWindow MainWindow { get; init; }

    public required IApplication App { get; init; }

    public required IOpenBooksManager OpenBooks { get; init; }

    public required IBrowser Browser { get; init; }

    public required IComicDisplay ComicDisplay { get; init; }

    public required IMetadataGraph Metadata { get; init; }

    public required IRulesEngine Rules { get; init; }

    public required IMetadataWriter Writer { get; init; }

    public required IThemePlugin ThemePlugin { get; init; }

    public string CommandPath { get; set; } = string.Empty;

    public string PluginKey { get; set; } = string.Empty;

    public IEnumerable<string> LibraryPaths
    {
        get
        {
            using var context = PaperbunkrDb.CreateContext();
            return context.WatchedFolders.Select(f => f.Path).ToList();
        }
    }

    /// <summary>Reads this command's own plugin-scoped setting (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md §6).</summary>
    public string? GetSetting(string key)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.PluginSettingStates
            .Where(s => s.PluginKey == PluginKey && s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefault();
    }

    /// <summary>Persists a plugin-scoped setting - sparse-table upsert, same convention as <c>PluginCommandState</c>.</summary>
    public void SetSetting(string key, string value)
    {
        using var context = PaperbunkrDb.CreateContext();
        var row = context.PluginSettingStates.FirstOrDefault(s => s.PluginKey == PluginKey && s.Key == key);
        if (row is null)
        {
            context.PluginSettingStates.Add(new Data.Entities.PluginSettingState { PluginKey = PluginKey, Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }

        context.SaveChanges();
    }

    /// <summary>No localization pipeline exists yet - documented pass-through (docs §4).</summary>
    public string Localize(string resourceKey, string elementKey, string text) => text;

    public object Clone() => (PaperbunkrPluginEnvironment)MemberwiseClone();
}
