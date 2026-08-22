using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>One row in a Story Event's ordered member list, wrapping a real <see cref="EventMembership"/>. Mirrors <see cref="ReadingListItemRowViewModel"/>, plus a <see cref="Role"/> picker.</summary>
public partial class EventMemberRowViewModel : ViewModelBase
{
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

    public string Number => Member.Issue?.EffectiveNumber() ?? "?";

    public string Name => Member.Issue?.Series?.Name ?? Member.Issue?.Title ?? "Unknown";

    public static EventMembershipRoleOption[] RoleOptions => EventMembershipRoleOption.All;

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

    partial void OnSelectedRoleOptionChanged(EventMembershipRoleOption value) => SelectedRole = value.Role;

    partial void OnSelectedRoleChanged(EventMembershipRole value)
    {
        Member.Role = value;
        _onRoleChanged(this);
    }

    [RelayCommand]
    private void MoveUp() => _onMoveUp(this);

    [RelayCommand]
    private void MoveDown() => _onMoveDown(this);

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
