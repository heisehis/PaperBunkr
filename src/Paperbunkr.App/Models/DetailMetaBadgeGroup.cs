using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Models;

/// <summary>
/// Caps the hero band's <see cref="DetailMetaBadge"/> row at 3 visible badges with a "+N more"
/// expander (docs/superpowers/specs/2026-09-13-detail-screens-redesign-design.md) - fixes
/// <c>DetailMetaBadge.Build</c> returning up to 8 badges with no cap, which wrapped the hero's
/// badge row onto multiple lines against the fixed 360px hero height. Same expand/collapse
/// convention <see cref="DetailBandGroupViewModel"/> uses for its own chip groups, but a separate
/// small type since three view-models (<see cref="ViewModels.DetailScreenViewModel"/>,
/// <see cref="ViewModels.MangaDetailScreenViewModel"/>, <see cref="ViewModels.BookDetailScreenViewModel"/>)
/// each build their own <c>MetaBadges</c> and would otherwise each need their own expand-state field.
/// </summary>
public partial class DetailMetaBadgeGroup : ObservableObject
{
    private const int Cap = 3;

    private readonly IReadOnlyList<DetailMetaBadge> _all;

    public DetailMetaBadgeGroup(IReadOnlyList<DetailMetaBadge> all) => _all = all;

    [ObservableProperty]
    private bool _isExpanded;

    public IReadOnlyList<DetailMetaBadge> Visible => IsExpanded ? _all : _all.Take(Cap).ToList();

    /// <summary>Every badge regardless of expand state - for callers that need the full underlying
    /// set rather than what's currently on screen (e.g. asserting aggregation correctness in tests).</summary>
    public IReadOnlyList<DetailMetaBadge> All => _all;

    /// <summary>Total badge count regardless of expand state - <see cref="IDetailHeaderSource.HasMetaBadges"/>
    /// reads this, not <see cref="Visible"/>.Count, so it still means "any badge exists" rather than
    /// "any badge is currently shown."</summary>
    public int TotalCount => _all.Count;

    public int OverflowCount => System.Math.Max(0, _all.Count - Cap);

    public bool HasOverflow => OverflowCount > 0;

    public string MoreLabel => IsExpanded ? "show less" : $"+{OverflowCount} more";

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(Visible));
        OnPropertyChanged(nameof(MoreLabel));
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;
}
