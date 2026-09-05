using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Plugins.Hooks;

namespace Paperbunkr.App.Plugins;

/// <summary>
/// Proactive Activity Center alerts for grouped <see cref="PluginHooks.CreateBookList"/> results
/// (docs/superpowers/specs/2026-09-05-plugin-grouped-review-and-scan-alerts-design.md §4). No new
/// plugin hook - <c>CreateBookList</c> is already invocable on demand; this just re-runs it after a
/// scan/import and alerts when the group count grew.
///
/// Tracking is in-memory, not persisted: <see cref="ActivityAlert"/>'s own doc comment states
/// alerts are "session-scoped... not persisted to history" - a durable table tracking "last known
/// group count" would outlive the very alert it drives, which would be a real inconsistency. This
/// resets every app session, same lifetime as the alerts themselves.
/// </summary>
public sealed class PluginScanAlertService
{
    private readonly PluginHostService _pluginHost;
    private readonly IActivityService _activityService;
    private readonly Dictionary<string, int> _lastKnownGroupCounts = new();

    public PluginScanAlertService(PluginHostService pluginHost, IActivityService activityService)
    {
        _pluginHost = pluginHost;
        _activityService = activityService;
    }

    /// <summary>Called from <see cref="LibraryFolderScanner"/> after a scan/import actually adds something - never on a no-op scan.</summary>
    public async Task CheckForNewGroupsAsync()
    {
        foreach (var command in _pluginHost.Engine.GetCommands(PluginHooks.CreateBookList))
        {
            if (command.Environment is null)
            {
                continue;
            }

            var result = await _pluginHost.RunCommandAsync(command, new CreateBookListHookGlobals { Environment = command.Environment }).ConfigureAwait(false);
            if (!result.Success || result.ReturnValue is not IEnumerable<PluginBookGroup> groups)
            {
                // Not a grouped result (a flat Issue[] command, or one that failed) - never
                // participates in alerting, per the design's own scoping decision.
                continue;
            }

            string key = $"{command.PluginKey}:{command.Key}";
            int count = groups.Count();
            int last = _lastKnownGroupCounts.GetValueOrDefault(key);

            if (count > last)
            {
                string dedupeKey = $"plugin-grouped:{key}";

                // ActivityService.RaiseAlert's dedupe path only refreshes an existing alert's
                // CreatedUtc, not its Title - re-raising with the same DedupeKey after a real
                // growth (e.g. 1 -> 3 duplicates) would silently leave the stale "1" title showing
                // forever. Dismiss the old row first so the fresh count actually reaches the UI.
                var existing = _activityService.Alerts.FirstOrDefault(a => a.DedupeKey == dedupeKey);
                if (existing is not null)
                {
                    _activityService.DismissAlert(existing.Id);
                }

                _activityService.RaiseAlert(new ActivityAlert
                {
                    Severity = ActivityAlertSeverity.Info,
                    Title = $"{count} possible duplicate{(count == 1 ? "" : "s")} found",
                    Detail = command.Name,
                    ActionLabel = "Review",
                    ActionLink = new ActivityLink(ActivityLinkKind.PluginGroupedReview, $"{command.PluginKey}|{command.Key}"),
                    DedupeKey = dedupeKey,
                });
            }

            // Updated regardless of whether an alert fired, so a shrink is remembered too - a
            // later regrow past the new (lower) baseline still alerts correctly.
            _lastKnownGroupCounts[key] = count;
        }
    }
}
