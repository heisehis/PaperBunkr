using Avalonia.Controls;
using Paperbunkr.Plugins.Abstractions.Native;

namespace Paperbunkr.Plugins.Abstractions.Ui;

/// <summary>
/// Wider environment surface for a UI-capable native plugin (docs/superpowers/specs/2026-09-11-
/// plugin-api-v4-native-tier-design.md §3). The host always constructs the richer implementation in
/// practice and hands it to <c>INativePluginModule.Initialize</c> — a plugin that wants to show a
/// dialog recovers this wider surface with <c>if (environment is INativePluginUiEnvironment ui)</c>;
/// a purely headless plugin simply never does that check and never references this project.
/// </summary>
public interface INativePluginUiEnvironment : INativePluginEnvironment
{
    /// <summary>Shows <paramref name="content"/> — a plugin-compiled <see cref="Control"/> (e.g. a
    /// settings screen, a collision dialog, a match-review dialog) — as a modal overlay hosted by the
    /// app's own generic native-plugin modal host (one shared <c>OverlayShell</c> slot, not a bespoke
    /// one per dialog type), and awaits a result the plugin's own view/viewmodel supplies. The plugin
    /// can't touch <c>MainWindow.axaml</c> itself, so this is the one hosting primitive it gets.</summary>
    Task<TResult> ShowModalAsync<TResult>(Control content);
}
