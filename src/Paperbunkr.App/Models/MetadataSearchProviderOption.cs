using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;

namespace Paperbunkr.App.Models;

/// <summary>
/// One entry in the Detail screen's Apply-from-Provider search picker - either a built-in
/// <see cref="ExternalMetadataProvider"/> (AniList/MangaBaka) or a Plugin API v2 NetSearch-hook
/// command (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-hooks-plan.md §8). The
/// ComboBox item type used to be a bare <c>ExternalMetadataProvider</c> - this wrapper is what lets
/// a plugin-registered provider appear "alongside AniList/MangaBaka" (the spec's own wording)
/// without touching that enum, which is persisted (<c>SeriesExternalLink.Provider</c>) and has no
/// slot for an arbitrary plugin.
/// </summary>
public sealed record MetadataSearchProviderOption(string Label, ExternalMetadataProvider? BuiltIn, Command? PluginCommand, Paperbunkr.App.Plugins.PluginMetadataProvider? PluginProvider = null)
{
    public bool IsPlugin => PluginCommand is not null || PluginProvider is not null;

    /// <summary>A plugin metadata provider (Plugin API 4.5 §C): its matches become proposals the user reviews.</summary>
    public static MetadataSearchProviderOption For(Paperbunkr.App.Plugins.PluginMetadataProvider provider) => new($"{provider.DisplayName} (plugin)", null, null, provider);

    /// <summary>String form of <see cref="BuiltIn"/> for <c>BrandMark.Value</c> (a <c>string?</c> property) - null for a plugin entry, which has no bundled brand asset.</summary>
    public string? BrandMarkValue => BuiltIn?.ToString();

    public static MetadataSearchProviderOption For(ExternalMetadataProvider provider) => new(provider.ToString(), provider, null);

    public static MetadataSearchProviderOption For(Command command) => new(command.Name, null, command);
}
