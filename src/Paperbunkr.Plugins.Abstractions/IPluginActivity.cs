using Paperbunkr.Plugins.Abstractions.Native;

namespace Paperbunkr.Plugins;

/// <summary>Severity of a plugin-raised Activity Center alert. Maps one-to-one onto the app's own alert severities.</summary>
public enum PluginAlertSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// How a plugin reports background work and problems through the Activity Center (docs/superpowers/
/// specs/2026-09-20-plugin-api-4-1-design.md §4) - available to script and native plugins alike via
/// <see cref="IPluginEnvironment.Activity"/>.
/// <para>
/// Deliberately narrower than the host's own service: a plugin can start a job (always shown as a
/// visible row, always attributed to the plugin) and raise an alert, and that's all. It cannot
/// register the ambient "upkeep" row, choose a job kind or trigger, or control toast behaviour -
/// those stay host-controlled so a noisy plugin can't spam toasts or masquerade as an app job.
/// </para>
/// <para>
/// A native plugin's separate <see cref="INativePluginEnvironment.StartActivityJob"/> (full trust, any
/// job kind) is unchanged.
/// </para>
/// </summary>
public interface IPluginActivity
{
    /// <summary>
    /// Starts a tracked job. The row is titled "&lt;plugin name&gt;: <paramref name="title"/>". Drive it
    /// through the returned handle and dispose it; disposing without calling
    /// <see cref="IPluginActivityHandle.Succeed"/> or <see cref="IPluginActivityHandle.Fail"/> records
    /// it as cancelled.
    /// </summary>
    IPluginActivityHandle StartJob(string title, bool cancellable = true);

    /// <summary>
    /// Raises an alert titled "&lt;plugin name&gt;: <paramref name="title"/>". Repeats with the same
    /// <paramref name="dedupeKey"/> from the same plugin collapse into one alert; a key is scoped to the
    /// plugin, so two plugins (or a plugin and the app) can never suppress each other's alerts. With no
    /// key, every call raises its own alert.
    /// </summary>
    void RaiseAlert(PluginAlertSeverity severity, string title, string? detail = null, string? dedupeKey = null);
}
