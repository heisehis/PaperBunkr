using System.Collections.ObjectModel;

namespace Paperbunkr.App.Models;

/// <summary>
/// One run's worth of issues in the Detail screen's Issues tab (docs/superpowers/specs/
/// 2026-08-30-series-detail-run-separator-design.md) - a same-titled series that restarted
/// numbering across eras (e.g. "Venom (2018)" vs "Venom (2022)"), keyed by <see cref="Data.Entities.Issue.Volume"/>.
/// <see cref="Header"/> is null for the "no Volume set" bucket and for the sole group of a
/// single-run series (see <c>DetailTabsViewModel.LoadSeries</c>'s collapse rule) - the Issues tab
/// renders no separator bar in either case.
/// </summary>
public sealed class IssueRunGroup
{
    public string? Header { get; init; }
    public string? Subtitle { get; init; }
    public int ReadCount { get; init; }
    public int TotalCount { get; init; }
    public string ProgressLabel => $"{ReadCount:N0} of {TotalCount:N0} read";
    public double ReadPercent => TotalCount > 0 ? 100.0 * ReadCount / TotalCount : 0;
    public bool HasStructure => TotalCount > 0;
    public bool IsFirstSection { get; init; }
    public string ReadLabel { get; init; } = "Read in order";
    public System.Windows.Input.ICommand? ReadCommand { get; init; }
    public System.Windows.Input.ICommand? SaveCommand { get; init; }

    public ObservableCollection<IssueCardSample> Items { get; init; } = new();
}
