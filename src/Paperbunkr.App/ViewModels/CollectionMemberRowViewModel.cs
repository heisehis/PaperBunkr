using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.Collections;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in <see cref="CollectionPropertiesScreenViewModel"/>'s member list - wraps a resolved
/// <see cref="CollectionMember"/> with the Remove command the overlay's row needs. Reordering is
/// via Move Up/Down on the parent (no drag library pulled in for this - see the design doc's own
/// fallback note); removal is buffered like the reading-list overlay's cover pick, applied only on
/// <see cref="CollectionPropertiesScreenViewModel.SaveCommand"/>.
///
/// <see cref="IsRuleMatched"/> (docs/superpowers/specs/2026-08-30-smart-collections-design.md) is
/// true when this row has no backing <see cref="CollectionItem"/> - it's present only because it
/// matches one of the collection's rule slots. Remove/Move are disabled for these rows; there's
/// nothing to remove or reorder, only the rule itself controls membership.
/// </summary>
public sealed partial class CollectionMemberRowViewModel : ObservableObject
{
    private readonly Action<CollectionMemberRowViewModel> _onRemove;

    public CollectionMemberRowViewModel(CollectionMember member, Action<CollectionMemberRowViewModel> onRemove)
    {
        CollectionItemId = member.CollectionItemId;
        DisplayTitle = member.DisplayTitle;
        KindLabel = member.Kind.ToString();
        _onRemove = onRemove;
    }

    public int? CollectionItemId { get; }

    public bool IsRuleMatched => CollectionItemId is null;

    public string? RuleMatchedTooltip => IsRuleMatched
        ? "Matches this collection's rule — edit the rule to exclude it."
        : null;

    public string DisplayTitle { get; }

    public string KindLabel { get; }

    public string KindLabelDisplay => IsRuleMatched ? $"{KindLabel} · matches rule" : KindLabel;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove() => _onRemove(this);

    private bool CanRemove() => !IsRuleMatched;
}
