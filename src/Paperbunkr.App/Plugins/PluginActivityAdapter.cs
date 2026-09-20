using System;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Plugins;
using Paperbunkr.Plugins.Abstractions.Native;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Real <see cref="IPluginActivity"/> (docs/superpowers/specs/2026-09-20-plugin-api-4-1-design.md §4):
/// a deliberately narrow view of <see cref="IActivityService"/> for one plugin. Every job is a visible
/// <see cref="ActivityJobKind.Plugin"/> row triggered by <see cref="ActivityTrigger.Plugin"/> and titled
/// with the plugin's name; toast policy is fixed by the host (failures only) so a plugin can't spam
/// toasts; and alert dedupe keys are namespaced by plugin key so one plugin can never suppress another
/// plugin's - or the app's - alerts.
/// <para>
/// Bound to one plugin key at construction. <see cref="PaperbunkrPluginEnvironment"/> is shallow-cloned
/// per command, so the environment builds one of these per <c>Activity</c> access rather than caching
/// it - a cached instance would stay bound to whichever clone created it.
/// </para>
/// </summary>
internal sealed class PluginActivityAdapter : IPluginActivity
{
    private readonly IActivityService _activity;
    private readonly string _pluginKey;
    private readonly string _pluginName;

    public PluginActivityAdapter(IActivityService activity, string pluginKey, string pluginName)
    {
        _activity = activity;
        _pluginKey = pluginKey;
        _pluginName = string.IsNullOrWhiteSpace(pluginName) ? pluginKey : pluginName;
    }

    public IPluginActivityHandle StartJob(string title, bool cancellable = true)
    {
        IActivityJobHandle handle = _activity.StartJob(
            ActivityJobKind.Plugin,
            Prefixed(title),
            cancellable,
            ActivityTrigger.Plugin,
            ActivityToastPolicy.FailuresOnly);
        return new PluginActivityHandleAdapter(handle);
    }

    public void RaiseAlert(PluginAlertSeverity severity, string title, string? detail = null, string? dedupeKey = null)
    {
        _activity.RaiseAlert(new ActivityAlert
        {
            Severity = severity switch
            {
                PluginAlertSeverity.Warning => ActivityAlertSeverity.Warning,
                PluginAlertSeverity.Error => ActivityAlertSeverity.Error,
                _ => ActivityAlertSeverity.Info,
            },
            Title = Prefixed(title),
            Detail = detail,
            DedupeKey = string.IsNullOrEmpty(dedupeKey)
                ? Guid.NewGuid().ToString()
                : $"plugin:{_pluginKey}:{dedupeKey}",
        });
    }

    private string Prefixed(string title) => string.IsNullOrWhiteSpace(title) ? _pluginName : $"{_pluginName}: {title}";
}
