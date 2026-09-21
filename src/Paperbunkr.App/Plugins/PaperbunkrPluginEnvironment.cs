using System;
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

    /// <summary>The app's Activity Center, which <see cref="Activity"/> reports through.</summary>
    public required IActivityService ActivityService { get; init; }

    /// <summary>Resolves a plugin key to its display name for Activity Center titles; defaults to the key itself.</summary>
    public Func<string, string> ResolvePluginName { get; init; } = key => key;

    /// <summary>
    /// Built per access, deliberately not cached: this environment is shallow-cloned per command
    /// (<see cref="Clone"/>) and only <see cref="PluginKey"/> differs, so an adapter cached in a field
    /// would be copied by <c>MemberwiseClone</c> still bound to the original clone's key.
    /// </summary>
    public IPluginActivity Activity => new PluginActivityAdapter(ActivityService, PluginKey, ResolvePluginName(PluginKey));

    /// <summary>Where plugin logs are written; defaults to the real per-user folder, tests point it at a temp one.</summary>
    public PluginLogFiles LogFiles { get; init; } = PluginLogFiles.Default;

    /// <summary>Built per access for the same reason as <see cref="Activity"/>: only <see cref="PluginKey"/> differs between the per-command clones.</summary>
    public IPluginLogger Log => new PluginLogger(LogFiles, PluginKey);

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

    /// <summary>
    /// Schema-aware settings storage (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §6). The
    /// default resolves no schemas, so an environment built without one behaves exactly as before: every
    /// key is read and written verbatim. <c>PluginHostService</c> supplies one wired to the engine's
    /// discovered schemas.
    /// </summary>
    public PluginSettingsAccess SettingsAccess { get; init; } =
        new(PaperbunkrDb.CreateContext, _ => null, DpapiSecretProtector.Instance);

    /// <summary>Reads this command's own plugin-scoped setting (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md §6); for a key the plugin declared, the host has already sanitised it (see <see cref="PluginSettingsAccess"/>).</summary>
    public string? GetSetting(string key) => SettingsAccess.Get(PluginKey, key);

    /// <summary>Persists a plugin-scoped setting - sparse-table upsert, same convention as <c>PluginCommandState</c>; a declared <c>secret</c> is encrypted first.</summary>
    public void SetSetting(string key, string value) => SettingsAccess.Set(PluginKey, key, value);

    /// <summary>No localization pipeline exists yet - documented pass-through (docs §4).</summary>
    public string Localize(string resourceKey, string elementKey, string text) => text;

    public object Clone() => (PaperbunkrPluginEnvironment)MemberwiseClone();
}
