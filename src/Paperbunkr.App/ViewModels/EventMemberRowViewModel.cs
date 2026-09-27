using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>One row in a Story Event's ordered member list, wrapping a real <see cref="EventMembership"/>. Mirrors <see cref="ReadingListItemRowViewModel"/>, plus a <see cref="SelectedRole"/> picker.</summary>
public partial class EventMemberRowViewModel : ViewModelBase, Models.ISelectableCard
{
    /// <summary>Selection identity for <see cref="Paperbunkr.App.Services.TileSelectionController{TCard}"/> - the <see cref="EventMembership"/> row id.</summary>
    public int Id => Member.Id;

    [ObservableProperty]
    private bool _isSelected;

    private readonly Action<EventMemberRowViewModel> _onMoveUp;
    private readonly Action<EventMemberRowViewModel> _onMoveDown;
    private readonly Action<EventMemberRowViewModel> _onRemove;
    private readonly Action<EventMemberRowViewModel> _onRoleChanged;

    public EventMemberRowViewModel(
        EventMembership member,
        Action<EventMemberRowViewModel> onMoveUp,
        Action<EventMemberRowViewModel> onMoveDown,
        Action<EventMemberRowViewModel> onRemove,
        Action<EventMemberRowViewModel> onRoleChanged)
    {
        Member = member;
        _onMoveUp = onMoveUp;
        _onMoveDown = onMoveDown;
        _onRemove = onRemove;
        _onRoleChanged = onRoleChanged;
        _selectedRole = member.Role;
        _selectedRoleOption = RoleOptions.First(o => o.Role == member.Role);
    }

    public EventMembership Member { get; }

    /// <summary>1-based reading-order position across the event, set by the parent.</summary>
    [ObservableProperty]
    private int _position;

    public string Number => Member.Issue?.EffectiveNumber() ?? "?";

    public string Name => Member.Issue?.Series?.Name ?? Member.Issue?.EffectiveTitle() ?? "Unknown";

    /// <summary>Title line: "{issue title or series} #{Number}".</summary>
    public string TitleLine
    {
        get
        {
            string head = Member.Issue?.EffectiveTitle() ?? Member.Issue?.Series?.Name ?? "Unknown";
            return Number == "?" ? head : $"{head} #{Number}";
        }
    }

    /// <summary>Secondary line: "{Series} · {Year}".</summary>
    public string SeriesLine
    {
        get
        {
            string? series = Member.Issue?.Series?.Name;
            int? year = Member.Issue?.EffectiveYear();
            return (series, year) switch
            {
                ({ } s, { } y) when y > 0 => $"{s} · {y}",
                ({ } s, _) => s,
                _ => string.Empty,
            };
        }
    }

    public int? CoverIssueId => Member.Issue?.Id;

    /// <summary>What <c>views:AsyncCoverImage.SourceId</c> actually keys its lookup on - see
    /// <see cref="Models.IssueCardSample.CoverKey"/>'s doc comment.</summary>
    public string? CoverKey => Member.Issue is { } issue
        ? Services.CoverFingerprint.Stem(issue.Id, issue.FilePath, issue.FileSize)
        : null;

    public bool HasRole => true; // role is always set on an EventMembership (no "unset" state)

    /// <summary>The role, with a small "auto" marker when detection (not the user) chose it.</summary>
    public string RoleChipLabel => (SelectedRoleOption?.Label ?? string.Empty) + (IsAutoRole ? " · auto" : string.Empty);

    /// <summary>The role was applied by role detection and may be cleared or replaced.</summary>
    public bool IsAutoRole => Member.RoleSource == RoleAssignmentSource.Auto;

    /// <summary>Tooltip for the role chip: why detection chose it.</summary>
    public string? RoleReasonText => Member.RoleReason is { Length: > 0 } reason ? $"Detected automatically: {reason}" : null;

    /// <summary>Detection has a role in mind that it was not sure enough to apply (or that differs from the user's own).</summary>
    public bool HasRoleSuggestion => Member.SuggestedRole is not null;

    public string SuggestionText => Member.SuggestedRole is { } role
        ? $"Suggested: {RoleOptions.First(o => o.Role == role).Label} ({Member.SuggestedReason})"
        : string.Empty;

    private void RaiseRoleStateChanged()
    {
        OnPropertyChanged(nameof(IsAutoRole));
        OnPropertyChanged(nameof(RoleChipLabel));
        OnPropertyChanged(nameof(RoleReasonText));
        OnPropertyChanged(nameof(HasRoleSuggestion));
        OnPropertyChanged(nameof(SuggestionText));
    }

    /// <summary>Takes the suggested role as the user's own choice.</summary>
    [RelayCommand]
    private void AcceptSuggestion()
    {
        if (Member.SuggestedRole is { } role)
        {
            SelectedRoleOption = RoleOptions.First(o => o.Role == role);        // the change handler marks it user-set and saves
        }
    }

    /// <summary>Rejects the suggestion; detection does not raise it again for this member.</summary>
    [RelayCommand]
    private void DismissSuggestion()
    {
        MemberRoleApplier.Dismiss(Member);
        RaiseRoleStateChanged();
        _onRoleChanged(this);
    }

    /// <summary>Drops an automatically detected role (back to Core); detection does not put it back.</summary>
    [RelayCommand]
    private void ClearAutoRole()
    {
        if (MemberRoleApplier.ClearAuto(Member))
        {
            SelectedRoleOption = RoleOptions.First(o => o.Role == Member.Role);
            RaiseRoleStateChanged();
            _onRoleChanged(this);
        }
    }

    public static EventMembershipRoleOption[] RoleOptions => EventMembershipRoleOption.All;

    /// <summary>Role picks from the row's ⋮ menu.</summary>
    [RelayCommand]
    private void SetRole(EventMembershipRole role) =>
        SelectedRoleOption = RoleOptions.First(o => o.Role == role);

    [ObservableProperty]
    private EventMembershipRole _selectedRole;

    /// <summary>
    /// Bound to the ComboBox's <c>SelectedItem</c> instead of <c>SelectedValue</c>/
    /// <c>SelectedValueBinding</c> - the latter resolves its binding path against this row's own
    /// ambient DataContext, not the <c>ItemsSource</c> element type, so `{Binding Role}` there was
    /// silently unresolvable (a real, permanent XAML bug, not a build-tooling artifact - see
    /// docs/superpowers/specs/2026-08-18-selectedvaluebinding-xaml-fix-design.md).
    /// </summary>
    [ObservableProperty]
    private EventMembershipRoleOption _selectedRoleOption = null!;

    partial void OnSelectedRoleOptionChanged(EventMembershipRoleOption value)
    {
        SelectedRole = value.Role;
        OnPropertyChanged(nameof(RoleChipLabel));
    }

    partial void OnSelectedRoleChanged(EventMembershipRole value)
    {
        Member.Role = value;
        MemberRoleApplier.MarkUserSet(Member);          // a role chosen here is the user's own, never replaced by detection
        RaiseRoleStateChanged();
        _onRoleChanged(this);
    }

    [RelayCommand]
    private void MoveUp() => _onMoveUp(this);

    [RelayCommand]
    private void MoveDown() => _onMoveDown(this);

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
