using Avalonia.Controls;

namespace Paperbunkr.App.Plugins;

/// <summary>One native-plugin-contributed view and the name of the plugin it came from (Plugin API 4.5, docs/superpowers/specs/2026-10-28-plugin-api-4-5-pitch-completion-design.md §E).</summary>
public sealed record PluginView(string PluginName, Control View);
