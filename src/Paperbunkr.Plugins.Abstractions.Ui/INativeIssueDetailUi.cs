using Avalonia.Controls;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Plugins.Abstractions.Ui;

/// <summary>
/// Optional capability a native plugin's module implements to add a panel for one issue (Plugin API 4.5, docs/superpowers/specs/2026-10-28-
/// plugin-api-4-5-pitch-completion-design.md §E). Shown in the Detail screen's Plugins tab for the issue the user has focused, under the
/// plugin's name. Absent means no capability, as with <see cref="INativeSeriesDetailUi"/>.
/// </summary>
public interface INativeIssueDetailUi
{
    /// <summary>
    /// A control for <paramref name="issue"/>, or <see langword="null"/> to show nothing for it. Hosted inline in the host's own tab, so it
    /// should read as part of that screen (use the app's theme resources, not fixed colours). A view that throws is skipped and logged.
    /// </summary>
    Control? CreateIssueDetailView(INativePluginUiEnvironment environment, Issue issue);
}

/// <summary>
/// Optional capability a native plugin's module implements to add a card to Insights (Plugin API 4.5 §E): one per plugin, at the bottom
/// of the Today tab, inside a host-drawn card titled with the plugin's name.
/// </summary>
public interface INativeInsightsWidget
{
    /// <summary>The card's content, or <see langword="null"/> to show no card. Called each time the Today tab is built. A view that throws is skipped and logged.</summary>
    Control? CreateInsightsView(INativePluginUiEnvironment environment);
}
