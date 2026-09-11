namespace Paperbunkr.Plugins;

/// <summary>Ported from ComicRackCE's <c>IPluginConfig</c>, extended with per-plugin persistent settings (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md §6).</summary>
public interface IPluginConfig
{
    IEnumerable<string> LibraryPaths { get; }

    /// <summary>
    /// The stored value for <paramref name="key"/> in the calling command's own plugin scope, or
    /// null if unset (docs/superpowers/specs/2026-08-28-plugin-api-v3-data-manager-design.md §6).
    /// Scoped automatically per <see cref="IPluginEnvironment.PluginKey"/> — one plugin can't read
    /// another's settings by construing a key collision.
    /// </summary>
    string? GetSetting(string key);

    /// <summary>Persists <paramref name="value"/> under <paramref name="key"/> in the calling command's own plugin scope. The plugin owns parsing/formatting its own values.</summary>
    void SetSetting(string key, string value);
}
