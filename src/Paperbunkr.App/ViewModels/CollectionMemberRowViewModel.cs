using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
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
    private readonly CollectionMember _member;
    private readonly Action<CollectionMemberRowViewModel>? _onUseAsCover;
    private LibraryTile? _tile;

    public CollectionMemberRowViewModel(CollectionMember member, Action<CollectionMemberRowViewModel> onRemove, Action<CollectionMemberRowViewModel>? onUseAsCover = null)
    {
        _member = member;
        CollectionItemId = member.CollectionItemId;
        DisplayTitle = member.DisplayTitle;
        KindLabel = member.Kind.ToString();
        Kind = member.Kind;
        _onRemove = onRemove;
        _onUseAsCover = onUseAsCover;
    }

    public CollectionMemberKind Kind { get; }

    /// <summary>
    /// The row's cover, resolved on first read (the row template is the first reader, so an unrealized row of a 150+ member
    /// collection never decodes anything) through the same <see cref="LibraryTile.FromMember"/> path the Library grid and Home shelf use.
    /// </summary>
    public LibraryTile Tile => _tile ??= LibraryTile.FromMember(_member);

    /// <summary>True while a member is being dragged over this row - the drop line (accent) shows on it.</summary>
    [ObservableProperty]
    private bool _isDropTarget;

    public int? CollectionItemId { get; }

    public bool IsRuleMatched => CollectionItemId is null;

    public string? RuleMatchedTooltip => IsRuleMatched
        ? "Matches this collection's rule — edit the rule to exclude it."
        : null;

    public string DisplayTitle { get; }

    public string KindLabel { get; }

    public string KindLabelDisplay => IsRuleMatched ? $"{KindLabel} · matches rule" : KindLabel;

    /// <summary>True when this row is the collection's chosen cover (the automatic cover uses it); shown as a filled star.</summary>
    [ObservableProperty]
    private bool _isCoverMember;

    /// <summary>"Use as cover" is offered on manual rows only: a rule-matched row has no <see cref="CollectionItemId"/> to point at.</summary>
    public bool CanUseAsCover => !IsRuleMatched;

    public string UseAsCoverLabel => IsCoverMember ? "Stop using as the cover" : "Use as the cover";

    partial void OnIsCoverMemberChanged(bool value) => OnPropertyChanged(nameof(UseAsCoverLabel));

    [RelayCommand(CanExecute = nameof(CanUseAsCover))]
    private void UseAsCover() => _onUseAsCover?.Invoke(this);

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove() => _onRemove(this);

    private bool CanRemove() => !IsRuleMatched;
}
