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
    /// <summary>
    /// Shows a plugin-compiled <see cref="Control"/> (e.g. a collision dialog, a match-review dialog)
    /// as a modal overlay hosted by the app's own generic native-plugin modal host (one shared
    /// <c>OverlayShell</c> slot, not a bespoke one per dialog type) and awaits a result. The plugin
    /// can't touch <c>MainWindow.axaml</c> itself, so this is the one hosting primitive it gets.
    ///
    /// <paramref name="contentFactory"/> is a factory, not an already-built <see cref="Control"/>,
    /// because the host has no way to know what the plugin's own view/viewmodel needs in order to
    /// signal "done, here's the result" back - there's no shared marker interface a plugin's
    /// arbitrary ViewModel is required to implement. Instead the host builds the resolve callback
    /// FIRST and hands it to the factory, so the plugin's own ViewModel constructor can capture it
    /// and invoke it directly from whichever command/button should close the dialog:
    /// <code>
    /// var choice = await uiEnv.ShowModalAsync&lt;CollisionChoice&gt;(resolve =>
    ///     new FileConflictDialogView { DataContext = new FileConflictDialogViewModel(book, existing, resolve) });
    /// </code>
    /// </summary>
    Task<TResult> ShowModalAsync<TResult>(Func<Action<TResult>, Control> contentFactory);
}
