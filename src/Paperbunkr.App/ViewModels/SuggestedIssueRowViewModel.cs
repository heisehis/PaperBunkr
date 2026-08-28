using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in the Story Events screen's "Suggested for this Event" list (docs/superpowers/specs/
/// 2026-08-27-metadata-model-phase4e-format-signal-suggestions-design.md) - the issue, its
/// human-readable <see cref="Reason"/>, an inline <see cref="Role"/> picker (pre-filled from the
/// catalog's suggested role when it supplies one, otherwise <see cref="EventMembershipRole.Core"/>),
/// and Add / Dismiss actions.
/// </summary>
public partial class SuggestedIssueRowViewModel : ViewModelBase
{
    private readonly Action<SuggestedIssueRowViewModel> _onAdd;
    private readonly Action<SuggestedIssueRowViewModel> _onDismiss;

    public SuggestedIssueRowViewModel(EventSuggestion suggestion, Action<SuggestedIssueRowViewModel> onAdd, Action<SuggestedIssueRowViewModel> onDismiss)
    {
        _onAdd = onAdd;
        _onDismiss = onDismiss;
        IssueId = suggestion.Issue.Id;
        DisplayLabel = $"{suggestion.Issue.Series?.Name ?? "Unknown"} #{suggestion.Issue.EffectiveNumber()}";
        Reason = suggestion.Reason;
        IsStrong = suggestion.Strength == FormatSignalStrength.Strong;

        var role = suggestion.SuggestedRole ?? EventMembershipRole.Core;
        _selectedRole = role;
        _selectedRoleOption = RoleOptions.First(o => o.Role == role);
    }

    public int IssueId { get; }

    public string DisplayLabel { get; }

    public string Reason { get; }

    public bool IsStrong { get; }

    public static EventMembershipRoleOption[] RoleOptions => EventMembershipRoleOption.All;

    [ObservableProperty]
    private EventMembershipRole _selectedRole;

    /// <summary>Bound to the ComboBox's <c>SelectedItem</c> (not <c>SelectedValue</c>) - same permanent XAML-binding-scope bug as elsewhere on this screen.</summary>
    [ObservableProperty]
    private EventMembershipRoleOption _selectedRoleOption = null!;

    partial void OnSelectedRoleOptionChanged(EventMembershipRoleOption value) => SelectedRole = value.Role;

    [RelayCommand]
    private void Add() => _onAdd(this);

    [RelayCommand]
    private void Dismiss() => _onDismiss(this);
}
