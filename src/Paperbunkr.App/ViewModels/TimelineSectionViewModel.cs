using System.Collections.ObjectModel;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One labeled era section in the Story Events screen's Timeline mode (docs/superpowers/specs/
/// 2026-08-27-metadata-model-phase4g-age-progression-design.md) - one per <see cref="Paperbunkr.Data.Metadata.ComicAge"/>
/// present in the family (ages with zero issues are skipped, not shown empty).
/// </summary>
public sealed class TimelineSectionViewModel
{
    public required string Label { get; init; }

    /// <summary>The commonly-cited scholarly range shown as a tooltip when non-null (Modern has none - CE and Wikipedia don't meaningfully disagree there).</summary>
    public string? CommonlyCitedRange { get; init; }

    public bool HasCommonlyCitedRange => !string.IsNullOrEmpty(CommonlyCitedRange);

    public ObservableCollection<TimelineIssueCard> Issues { get; } = new();
}
