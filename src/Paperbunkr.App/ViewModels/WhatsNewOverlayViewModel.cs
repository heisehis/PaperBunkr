using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The "What's New" overlay (docs/superpowers/specs/2026-09-09-startup-onboarding-whats-new-
/// design.md, Decisions 5-6). Shown automatically on the first launch after the version bumps,
/// covering every release since the one last run; also opened on demand (current release only)
/// from the welcome screen's link and Preferences → About.
///
/// Mirrors <see cref="UpdateAvailableOverlayViewModel"/>'s shape - a small VM with a
/// <see cref="_requestClose"/> action, carrying already-selected data (<see cref="MainViewModel"/>
/// does the CHANGELOG load + entry selection before calling <see cref="Show"/>). Rendering reuses
/// <see cref="ChangelogBodyFormatter"/> at the view layer, exactly as the About accordion does.
/// </summary>
public partial class WhatsNewOverlayViewModel : ViewModelBase
{
    private readonly Action _requestClose;
    private readonly Action _openFullChangelog;

    public WhatsNewOverlayViewModel(Action requestClose, Action openFullChangelog)
    {
        _requestClose = requestClose;
        _openFullChangelog = openFullChangelog;
    }

    /// <summary>The entries to render, newest-first. One entry in current-only mode.</summary>
    [ObservableProperty]
    private IReadOnlyList<ChangelogEntry> _entries = [];

    /// <summary>True when opened from the welcome link / About button - one entry, always expanded,
    /// no collapsed history list.</summary>
    [ObservableProperty]
    private bool _currentEntryOnly;

    [ObservableProperty]
    private string _headerText = string.Empty;

    /// <summary>Set by <see cref="MainViewModel"/> right before opening the overlay.</summary>
    public void Show(IReadOnlyList<ChangelogEntry> entries, bool currentEntryOnly)
    {
        Entries = entries;
        CurrentEntryOnly = currentEntryOnly;
        HeaderText = currentEntryOnly
            ? $"What's new in Paperbunkr {ReleaseVersion.DisplayString}"
            : $"Updated to Paperbunkr {ReleaseVersion.DisplayString}";
    }

    /// <summary>
    /// The changelog entries strictly newer than <paramref name="lastRunVersion"/> (a four-part
    /// assembly version string, e.g. "0.2.0.0"), preserving the parser's newest-first order.
    /// Entries whose heading version can't be parsed, or that aren't newer, are dropped. A null
    /// <paramref name="lastRunVersion"/> (fresh install) yields an empty list - the caller shows
    /// nothing in that case, since there's no prior version to have changed from.
    /// </summary>
    public static IReadOnlyList<ChangelogEntry> SelectEntriesSince(IReadOnlyList<ChangelogEntry> all, string? lastRunVersion)
    {
        if (string.IsNullOrWhiteSpace(lastRunVersion) || !Version.TryParse(lastRunVersion, out var since))
        {
            return [];
        }

        return all
            .Where(e => ReleaseVersion.TryParseHeading(e.Version, out var v) && ReleaseVersion.IsNewerThan(v, since))
            .ToList();
    }

    [RelayCommand]
    private void GotIt() => _requestClose();

    [RelayCommand]
    private void OpenFullChangelog()
    {
        _requestClose();
        _openFullChangelog();
    }
}
