using Avalonia.Controls;
using Paperbunkr.Plugins.Abstractions.Native;

namespace Paperbunkr.Plugins.Abstractions.Ui;

/// <summary>
/// Optional capability a native plugin's module class implements alongside
/// <see cref="INativePluginModule"/> when it has a real, compiled settings screen (docs/superpowers/
/// specs/2026-09-11-plugin-api-v4-native-tier-design.md §3). A plugin with no settings UI simply
/// doesn't implement this interface and never needs this project to compile at all.
/// </summary>
public interface INativePluginSettingsUi
{
    /// <summary>Returns this plugin's own compiled settings <see cref="Control"/> (its own
    /// <c>UserControl</c>/<c>ViewModel</c> pair, e.g. a real <c>SettingsView.axaml</c>), or
    /// <see langword="null"/> if it has none — equivalent to no <c>ConfigScript</c> pairing today.
    /// The host hosts the returned <see cref="Control"/> generically; it never inspects or modifies
    /// its contents.</summary>
    Control? CreateSettingsView(INativePluginUiEnvironment environment);
}
