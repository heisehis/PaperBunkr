using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>One row in a Reading List's item grid, wrapping a real <see cref="ReadingListItem"/>.</summary>
public partial class ReadingListItemRowViewModel : ViewModelBase
{
    private readonly Action<ReadingListItemRowViewModel> _onMoveUp;
    private readonly Action<ReadingListItemRowViewModel> _onMoveDown;
    private readonly Action<ReadingListItemRowViewModel> _onRemove;
    private readonly Action<ReadingListItemRowViewModel> _onFieldChanged;
    private readonly Action<ReadingListItemRowViewModel> _onLink;
    private readonly Action<ReadingListItemRowViewModel> _onOpen;
    private readonly Action<ReadingListItemRowViewModel> _onToggleRead;

    public ReadingListItemRowViewModel(
        ReadingListItem item,
        Action<ReadingListItemRowViewModel> onMoveUp,
        Action<ReadingListItemRowViewModel> onMoveDown,
        Action<ReadingListItemRowViewModel> onRemove,
        Action<ReadingListItemRowViewModel> onFieldChanged,
        Action<ReadingListItemRowViewModel> onLink,
        Action<ReadingListItemRowViewModel> onOpen,
        Action<ReadingListItemRowViewModel> onToggleRead)
    {
        Item = item;
        _onMoveUp = onMoveUp;
        _onMoveDown = onMoveDown;
        _onRemove = onRemove;
        _onFieldChanged = onFieldChanged;
        _onLink = onLink;
        _onOpen = onOpen;
        _onToggleRead = onToggleRead;
        _selectedRole = item.Role;
        _selectedRoleOption = item.Role is EventMembershipRole role ? RoleOptions.FirstOrDefault(o => o.Role == role) : null;
        _notes = item.Notes ?? string.Empty;
    }

    public ReadingListItem Item { get; }

    /// <summary>1-based reading-order position across the whole list (set by the parent) - the rail number.</summary>
    [ObservableProperty]
    private int _position;

    public string Number => Item.Issue?.EffectiveNumber() ?? "?";

    /// <summary>Title line: "Name #Number", or just "Name" when the issue has no number.</summary>
    public string TitleLine => Number == "?" ? Name : $"{Name} #{Number}";

    public string Name => Item.Issue?.EffectiveTitle() ?? Item.Issue?.Series?.Name ?? "Unknown";

    /// <summary>Secondary line under the title: "Series · Year", or the missing-file note.</summary>
    public string SeriesLine
    {
        get
        {
            if (IsMissing)
            {
                return "missing — not in your library";
            }

            string? series = Item.Issue?.Series?.Name;
            int? year = Item.Issue?.EffectiveYear();
            return (series, year) switch
            {
                ({ } s, { } y) when y > 0 => $"{s} · {y}",
                ({ } s, _) => s,
                _ => string.Empty,
            };
        }
    }

    /// <summary>App-wide read signal (<see cref="IssueMetadataExtensions.HasBeenRead"/>) - false for
    /// a missing/unscanned issue (no <see cref="Issue.PageCount"/>), same as every other list in the app.</summary>
    public bool IsRead => Item.Issue?.HasBeenRead() == true;

    public bool IsInProgress => Item.Issue?.IsInProgress() == true;

    /// <summary>Set by the parent after it picks the "Continue" target - drives the highlighted row + Read button.</summary>
    [ObservableProperty]
    private bool _isNextUp;

    /// <summary>
    /// Resolved to a cover <c>Bitmap</c> lazily via <c>CoverImageConverter</c> (docs/superpowers/
    /// specs/2026-08-22-cbl-manager-arc-cover-and-synopsis-design.md) - real gap found live: every
    /// row in a reading list rendered a flat placeholder square with no cover art at all, unlike
    /// every other issue-listing screen in the app (Library, Smart Lists) which already show one.
    /// Null for a placeholder issue with no file, same as every other <c>CoverImageCache</c> caller.
    /// </summary>
    public int? CoverIssueId => Item.Issue?.Id;

    public bool IsOwned => Item.Issue is { FileIsMissing: false };

    public bool IsMissing => !IsOwned;

    /// <summary>Phase 4c overhaul (docs/superpowers/specs/2026-08-17-metadata-model-phase4c-reading-list-overhaul-design.md) - reuses <see cref="EventMembershipRole"/>, optional/blank by default.</summary>
    public static Models.EventMembershipRoleOption[] RoleOptions => Models.EventMembershipRoleOption.All;

    [ObservableProperty]
    private EventMembershipRole? _selectedRole;

    /// <summary>
    /// Bound to the ComboBox's <c>SelectedItem</c> instead of <c>SelectedValue</c>/
    /// <c>SelectedValueBinding</c> - the latter resolves its binding path against this row's own
    /// ambient DataContext, not the <c>ItemsSource</c> element type, so `{Binding Role}` there was
    /// silently unresolvable (a real, permanent XAML bug, not a build-tooling artifact - see
    /// docs/superpowers/specs/2026-08-18-selectedvaluebinding-xaml-fix-design.md). Null clears the
    /// optional role, matching the ComboBox's placeholder-text empty state.
    /// </summary>
    [ObservableProperty]
    private EventMembershipRoleOption? _selectedRoleOption;

    partial void OnSelectedRoleOptionChanged(EventMembershipRoleOption? value)
    {
        SelectedRole = value?.Role;
        OnPropertyChanged(nameof(HasRole));
        OnPropertyChanged(nameof(RoleChipLabel));
    }

    partial void OnSelectedRoleChanged(EventMembershipRole? value)
    {
        Item.Role = value;
        _onFieldChanged(this);
    }

    /// <summary>A role is set - drives the small read-only chip shown on the row.</summary>
    public bool HasRole => SelectedRole is not null;

    public string RoleChipLabel => SelectedRoleOption?.Label ?? string.Empty;

    /// <summary>Toggled from the row's ⋯ menu "Add a note" - reveals the inline note editor.</summary>
    [ObservableProperty]
    private bool _noteEditing;

    [RelayCommand]
    private void BeginNote() => NoteEditing = true;

    /// <summary>Role picks from the ⋯ submenu.</summary>
    [RelayCommand]
    private void SetRole(EventMembershipRoleOption? option) => SelectedRoleOption = option;

    [ObservableProperty]
    private string _notes;

    partial void OnNotesChanged(string value)
    {
        Item.Notes = string.IsNullOrWhiteSpace(value) ? null : value;
        _onFieldChanged(this);
    }

    [RelayCommand]
    private void MoveUp() => _onMoveUp(this);

    [RelayCommand]
    private void MoveDown() => _onMoveDown(this);

    [RelayCommand]
    private void Remove() => _onRemove(this);

    [RelayCommand]
    private void Link() => _onLink(this);

    [RelayCommand]
    private void Open() => _onOpen(this);

    /// <summary>Manual mark-read / mark-unread (docs/superpowers/specs/2026-08-23-mark-as-read-design.md);
    /// the parent flips <see cref="Issue.LastPageRead"/> via <c>IssueReadStateResolver</c> and reloads.</summary>
    [RelayCommand]
    private void ToggleRead() => _onToggleRead(this);
}
