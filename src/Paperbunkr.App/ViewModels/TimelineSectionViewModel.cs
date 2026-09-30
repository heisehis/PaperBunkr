using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One labeled era section of a Timeline (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-age-progression-design.md) - one per
/// <see cref="ComicAge"/> present (ages with zero issues are skipped, not shown empty). The Continuity screen redesign
/// (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md, "Timeline") adds the era's own colour (via the
/// <c>Is…</c> flags the view turns into style classes), its years, counts and read progress, and folding.
/// </summary>
public sealed partial class TimelineSectionViewModel : ObservableObject
{
    public required string Label { get; init; }

    /// <summary>The commonly-cited scholarly range shown beside the header when non-null (Modern has none).</summary>
    public string? CommonlyCitedRange { get; init; }

    public bool HasCommonlyCitedRange => !string.IsNullOrEmpty(CommonlyCitedRange);

    /// <summary>The era; defaults to Modern for callers that predate era colours.</summary>
    public ComicAge Era { get; init; } = ComicAge.Modern;

    public bool IsPlatinum => Era == ComicAge.Platinum;

    public bool IsGolden => Era == ComicAge.Golden;

    public bool IsSilver => Era == ComicAge.Silver;

    public bool IsBronze => Era == ComicAge.Bronze;

    public bool IsModern => Era == ComicAge.Modern;

    public ObservableCollection<TimelineIssueCard> Issues { get; } = new();

    /// <summary>True while any issue in this era is unread - the node is filled; an outline once all are read
    /// (docs/superpowers/specs/2026-09-21-cosmetics-pitch-design.md #4). Evaluated after <see cref="Issues"/> is filled.</summary>
    public bool HasUnread => Issues.Any(i => i.IsUnread);

    public int IssueCount => Issues.Count;

    public int ReadCount => Issues.Count(i => !i.IsUnread);

    public double ReadFraction => Issues.Count == 0 ? 0 : (double)ReadCount / Issues.Count;

    /// <summary>"Modern Age · 2000–2015" - the era's name and the years its issues actually span.</summary>
    public string HeaderLabel
    {
        get
        {
            var years = Issues.Select(i => i.Year).OfType<int>().ToList();
            string span = years.Count == 0 ? string.Empty
                : years.Min() == years.Max() ? years.Min().ToString(CultureInfo.InvariantCulture)
                : $"{years.Min().ToString(CultureInfo.InvariantCulture)}–{years.Max().ToString(CultureInfo.InvariantCulture)}";
            return span.Length == 0 ? Label : $"{Label} · {span}";
        }
    }

    /// <summary>"142 issues · 58 read".</summary>
    public string CountsLabel => $"{(IssueCount == 1 ? "1 issue" : $"{IssueCount:N0} issues")} · {ReadCount:N0} read";

    /// <summary>Folded eras hide their covers; remembered for the session, reset on switching continuity or event.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    private bool _isFolded;

    public bool IsExpanded => !IsFolded;

    [RelayCommand]
    private void ToggleFold() => IsFolded = !IsFolded;
}
