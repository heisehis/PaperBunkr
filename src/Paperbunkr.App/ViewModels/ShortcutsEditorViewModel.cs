using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.ViewModels;

/// <summary>Which devices' bindings the Keyboard Shortcuts list shows: an action matches when any of its bindings is of that kind.</summary>
public enum ShortcutDeviceFilter
{
    All,
    KeyboardMouse,
    Controller,
}

/// <summary>
/// Preferences &gt; Keyboard Shortcuts (docs/superpowers/specs/2026-10-03-input-service-design.md §9, laid out per docs/superpowers/specs/2026-10-04-keyboard-shortcuts-master-detail-design.md):
/// one row per registered action, grouped as the action catalog groups them, each showing every key, mouse button, wheel direction and gamepad input bound to it. The page is a list of
/// those rows (<see cref="FilteredGroups"/>, narrowed by search, device and "customised only") beside an editor for the selected one (<see cref="SelectedRow"/>). Data-driven off
/// <see cref="IInputActionCatalog"/>, so an action a plugin registers later gets a row with no change here. Changes apply and persist immediately, like every other Preferences control.
/// </summary>
public partial class ShortcutsEditorViewModel : ViewModelBase
{
    private readonly IInputService _input;
    private readonly IFilePickerService _filePicker;
    private readonly Action<string, string> _showToast;
    private bool _syncQueued;
    private bool _rebuildQueued;
    private string? _selectedActionId;

    public ShortcutsEditorViewModel(IInputService input, IFilePickerService filePicker, Action<string, string> showToast)
    {
        _input = input;
        _filePicker = filePicker;
        _showToast = showToast;
        Groups = new ObservableCollection<ShortcutGroupViewModel>();
        FilteredGroups = new ObservableCollection<ShortcutFilteredGroup>();
        _input.BindingsChanged += OnBindingsChanged;
        Refresh();
    }

    /// <summary>Every group and row, unfiltered.</summary>
    public ObservableCollection<ShortcutGroupViewModel> Groups { get; }

    /// <summary>The groups the list shows: <see cref="Groups"/> narrowed by the search text, device filter and "customised only", with empty groups left out. The selected row is always kept in.</summary>
    public ObservableCollection<ShortcutFilteredGroup> FilteredGroups { get; }

    /// <summary>True when the filters match no action at all.</summary>
    [ObservableProperty]
    private bool _hasNoMatches;

    /// <summary>The first binding shared by two actions that could be active together; null when there is none. Not painted in the view any more (each row and chip carries its own marker), kept for the tests and as the one-line summary.</summary>
    [ObservableProperty]
    private string? _conflictError;

    public bool HasConflictError => !string.IsNullOrEmpty(ConflictError);

    partial void OnConflictErrorChanged(string? value) => OnPropertyChanged(nameof(HasConflictError));

    // ----- Filters -----

    /// <summary>Matches an action's label, its group's title and the text of any of its bindings; every word typed must match somewhere.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => RebuildFiltered();

    [ObservableProperty]
    private ShortcutDeviceFilter _deviceFilter = ShortcutDeviceFilter.All;

    partial void OnDeviceFilterChanged(ShortcutDeviceFilter value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterKeyboardMouse));
        OnPropertyChanged(nameof(IsFilterController));
        RebuildFiltered();
    }

    public bool IsFilterAll => DeviceFilter == ShortcutDeviceFilter.All;

    public bool IsFilterKeyboardMouse => DeviceFilter == ShortcutDeviceFilter.KeyboardMouse;

    public bool IsFilterController => DeviceFilter == ShortcutDeviceFilter.Controller;

    /// <summary>Only the actions whose bindings differ from their defaults.</summary>
    [ObservableProperty]
    private bool _customisedOnly;

    partial void OnCustomisedOnlyChanged(bool value) => RebuildFiltered();

    [RelayCommand]
    private void SetDeviceFilter(ShortcutDeviceFilter filter) => DeviceFilter = filter;

    [RelayCommand]
    private void ToggleCustomisedOnly() => CustomisedOnly = !CustomisedOnly;

    private bool FiltersActive => SearchText.Trim().Length > 0 || DeviceFilter != ShortcutDeviceFilter.All || CustomisedOnly;

    // ----- Selection -----

    /// <summary>The action the editor pane shows. Null only when the catalog has no actions.</summary>
    [ObservableProperty]
    private ShortcutRowViewModel? _selectedRow;

    partial void OnSelectedRowChanged(ShortcutRowViewModel? value) => OnPropertyChanged(nameof(HasSelection));

    public bool HasSelection => SelectedRow is not null;

    /// <summary>
    /// Selects <paramref name="row"/>. It is called from a row's own click or focus, so a list rebuild that the new selection needs (the old row no longer matches the filters) is posted
    /// to run after that event instead of detaching the clicked button while it is still routing (see <see cref="OnBindingsChanged"/>).
    /// </summary>
    public void SelectRow(ShortcutRowViewModel? row)
    {
        if (ReferenceEquals(row, SelectedRow))
        {
            return;
        }

        if (SelectedRow is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedRow = row;
        if (row is not null)
        {
            row.IsSelected = true;
            _selectedActionId = row.ActionId;
        }

        if (FiltersActive)
        {
            QueueRebuild();
        }
    }

    /// <summary>A Preferences search hit on a group's anchor (<c>shortcuts.navigation</c>): clears the filters so the group's header is in the list, and selects its first action.</summary>
    public void SelectGroupByTag(string tag)
    {
        var group = Groups.FirstOrDefault(g => string.Equals(g.Tag, tag, StringComparison.Ordinal));
        if (group is null || group.Rows.Count == 0)
        {
            return;
        }

        SearchText = string.Empty;
        DeviceFilter = ShortcutDeviceFilter.All;
        CustomisedOnly = false;
        SelectRow(group.Rows[0]);
    }

    /// <summary>Rebuilds every group and row from the catalog (also the way to pick up an action registered after this screen was built). Keeps the same action selected when it still exists.</summary>
    public void Refresh()
    {
        Groups.Clear();
        foreach (var group in _input.Actions.All.GroupBy(info => info.Group))
        {
            Groups.Add(new ShortcutGroupViewModel(group.Key, group.Select(info => new ShortcutRowViewModel(info, _input, SelectRow)).ToList()));
        }

        var rows = Groups.SelectMany(g => g.Rows).ToList();
        SelectedRow = null;
        var selected = rows.FirstOrDefault(r => r.ActionId == _selectedActionId) ?? rows.FirstOrDefault();
        if (selected is not null)
        {
            selected.IsSelected = true;
            _selectedActionId = selected.ActionId;
        }

        SelectedRow = selected;
        RecomputeConflicts();
        RebuildFiltered();
    }

    /// <summary>
    /// Recomputes <see cref="FilteredGroups"/>. A rebuild that would produce the same groups and rows is skipped, so a change that does not move anything (a chip added to a row that stays in
    /// the list) never re-creates the list's buttons and drops the focus on them.
    /// </summary>
    internal void RebuildFiltered()
    {
        var terms = SearchText.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var matched = Groups.ToDictionary(g => g, g => g.Rows.Where(r => Matches(r, terms)).ToHashSet());

        // The selected row stays in the list while anything matches, so editing never makes the row being edited vanish; when nothing matches the list is empty and says so.
        bool anyMatch = matched.Values.Any(rows => rows.Count > 0);
        var next = new List<ShortcutFilteredGroup>();
        foreach (var group in Groups)
        {
            var rows = group.Rows.Where(r => matched[group].Contains(r) || (anyMatch && ReferenceEquals(r, SelectedRow))).ToList();
            if (rows.Count > 0)
            {
                next.Add(new ShortcutFilteredGroup(group, rows));
            }
        }

        HasNoMatches = !anyMatch;

        if (next.Count == FilteredGroups.Count && next.Zip(FilteredGroups).All(pair => pair.First.SameContentAs(pair.Second)))
        {
            return;
        }

        FilteredGroups.Clear();
        foreach (var group in next)
        {
            FilteredGroups.Add(group);
        }
    }

    private bool Matches(ShortcutRowViewModel row, string[] terms)
    {
        if (CustomisedOnly && !row.IsCustomised)
        {
            return false;
        }

        bool deviceOk = DeviceFilter switch
        {
            ShortcutDeviceFilter.KeyboardMouse => row.Chips.Any(c => !c.IsPad),
            ShortcutDeviceFilter.Controller => row.Chips.Any(c => c.IsPad),
            _ => true,
        };
        return deviceOk && row.MatchesSearch(terms);
    }

    private void QueueRebuild()
    {
        if (_rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            RebuildFiltered();
        });
    }

    /// <summary>
    /// Brings every row's chips back in line with the service and re-checks conflicts. Rows edit through the service and wait for this rather than changing their own chip list, because
    /// the change is usually made from a chip's own remove button: replacing that chip while its click is still routing detaches the button mid-event and crashes Avalonia's detach walk
    /// (see <c>Paperbunkr.App.Controls.SuggestBox.Commit</c> for the fully diagnosed case), so the update is posted to run after the click finishes.
    /// </summary>
    private void OnBindingsChanged(object? sender, EventArgs e)
    {
        if (_syncQueued)
        {
            return;
        }

        _syncQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _syncQueued = false;
            SyncAll();
        });
    }

    internal void SyncAll()
    {
        foreach (var row in Groups.SelectMany(g => g.Rows))
        {
            row.Sync();
        }

        RecomputeConflicts();
        RebuildFiltered();
    }

    [RelayCommand]
    private async Task ImportLayout()
    {
        string? path = await _filePicker.PickOpenFileAsync("Import Keyboard Shortcuts", "json", "Keyboard Shortcut Layout");
        if (path is null)
        {
            return;
        }

        int applied;
        try
        {
            applied = KeymapLayoutIO.Import(_input, path);
        }
        catch (InvalidDataException ex)
        {
            _showToast("Couldn't import keyboard shortcuts", ex.Message);
            return;
        }

        SyncAll();
        _showToast("Keyboard shortcuts imported", $"Applied {applied} binding{(applied == 1 ? "" : "s")}.");
    }

    [RelayCommand]
    private async Task ExportLayout()
    {
        string? path = await _filePicker.PickSaveFileAsync("Export Keyboard Shortcuts", "paperbunkr-shortcuts.json", "json", "Keyboard Shortcut Layout");
        if (path is null)
        {
            return;
        }

        KeymapLayoutIO.Export(_input, path);
        _showToast("Keyboard shortcuts exported", $"Saved to {path}.");
    }

    /// <summary>
    /// Whole-layout revert (docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md) - matches CE's own "Restore Default Keyboard Layout" menu action. No confirmation
    /// dialog: this section applies every change immediately with no Save/Cancel step, and the action is recoverable via Import if a layout was exported first.
    /// </summary>
    [RelayCommand]
    private void ResetAll()
    {
        _input.ResetAll();
        SyncAll();
        _showToast("Keyboard shortcuts reset", "Every shortcut is back to its default.");
    }

    /// <summary>
    /// Soft validation, not a hard block (the row already saved its new binding by the time this runs, matching every other Preferences toggle). Two actions conflict when one binding
    /// reaches both in the same scope with overlapping reader states (<see cref="IInputService.FindConflicts"/>): mode-specific actions (paged, zoomed, continuous) are mutually exclusive
    /// at runtime, so sharing a key across them is fine, while an always-available action shadows any mode-specific one it collides with. Every row involved is flagged, wherever it
    /// sits, and each keeps the list of what it collides with for the editor pane; the banner text names only the first.
    /// </summary>
    private void RecomputeConflicts()
    {
        var rows = Groups.SelectMany(g => g.Rows).ToList();
        var byAction = rows.ToDictionary(r => r.ActionId, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            row.ClearConflicts();
        }

        string? first = null;
        foreach (var row in rows)
        {
            foreach (var chip in row.Chips)
            {
                foreach (var conflict in _input.FindConflicts(row.Action, chip.Binding))
                {
                    if (!byAction.TryGetValue(conflict.Other.Id, out var other))
                    {
                        continue;
                    }

                    row.AddConflict(other, chip);
                    other.AddConflict(row, other.Chips.FirstOrDefault(c => c.Binding == chip.Binding) ?? chip);
                    first ??= $"\"{chip.Label}\" is assigned to both \"{row.Label}\" and \"{other.Label}\".";
                }
            }
        }

        ConflictError = first;
    }
}

/// <summary>One titled group of shortcut rows ("Navigation", "Zoom &amp; Fit", …).</summary>
public sealed class ShortcutGroupViewModel
{
    public ShortcutGroupViewModel(string title, IReadOnlyList<ShortcutRowViewModel> rows)
    {
        Title = title;
        Rows = rows;
        Tag = TagFor(title);
    }

    public string Title { get; }

    /// <summary>The group title in capitals, the section caption style.</summary>
    public string Caption => Title.ToUpperInvariant();

    public IReadOnlyList<ShortcutRowViewModel> Rows { get; }

    /// <summary>The Preferences search anchor (<c>PreferenceIndex</c>): <c>shortcuts.navigation</c>, <c>shortcuts.zoomFit</c>, …</summary>
    public string Tag { get; }

    /// <summary>The search anchor for a shortcut group title, in camelCase: "Navigation" is <c>shortcuts.navigation</c>, "Zoom &amp; Fit" is <c>shortcuts.zoomFit</c>, "Book reader" is <c>shortcuts.bookReader</c>.</summary>
    public static string TagFor(string title)
    {
        var words = new System.Text.StringBuilder();
        bool upperNext = false;
        foreach (char c in title)
        {
            if (!char.IsLetterOrDigit(c))
            {
                upperNext = words.Length > 0;
                continue;
            }

            words.Append(upperNext ? char.ToUpperInvariant(c) : words.Length == 0 ? char.ToLowerInvariant(c) : c);
            upperNext = false;
        }

        return "shortcuts." + words;
    }
}

/// <summary>A group as the list shows it: the rows that passed the filters, under the group's header.</summary>
public sealed class ShortcutFilteredGroup
{
    public ShortcutFilteredGroup(ShortcutGroupViewModel group, IReadOnlyList<ShortcutRowViewModel> rows)
    {
        Group = group;
        Rows = rows;
    }

    public ShortcutGroupViewModel Group { get; }

    public IReadOnlyList<ShortcutRowViewModel> Rows { get; }

    public string Caption => Group.Caption;

    /// <summary>The anchor <c>PreferencesScreen</c> scrolls to; carried by the group's header.</summary>
    public string Tag => Group.Tag;

    /// <summary>"28" when every action of the group shows, "3 of 28" when a filter hides some.</summary>
    public string CountText => Rows.Count == Group.Rows.Count ? Rows.Count.ToString() : $"{Rows.Count} of {Group.Rows.Count}";

    public bool SameContentAs(ShortcutFilteredGroup other) =>
        ReferenceEquals(Group, other.Group) && Rows.SequenceEqual(other.Rows);
}

/// <summary>One bound input on a row, shown as a chip (removable in the editor pane).</summary>
public sealed partial class ShortcutChip : ObservableObject
{
    public ShortcutChip(InputBinding binding) => Binding = binding;

    public InputBinding Binding { get; }

    public string Label => InputBindingDisplay.Format(Binding);

    /// <summary>A controller input: drawn with the gamepad icon and the pad tint.</summary>
    public bool IsPad => Binding.Device == InputDevice.Gamepad;

    /// <summary>A stick or trigger axis: fixed to that input, so it shows a lock and cannot be removed or re-captured.</summary>
    public bool IsLocked => Binding.Kind == InputBindingKind.Pad && GamepadInputs.IsAxis(Binding.Pad);

    public bool IsRemovable => !IsLocked;

    /// <summary>This input is also bound to another action that could be active at the same time.</summary>
    [ObservableProperty]
    private bool _isConflicted;
}

/// <summary>Another action that shares one of this row's bindings (see <see cref="ShortcutsEditorViewModel"/>'s conflict check), with the sentence the editor pane shows for it.</summary>
public sealed record ShortcutConflictInfo(ShortcutRowViewModel Other, string BindingLabel, string Message, bool Shadows);

/// <summary>
/// One action's row: its chips, plus the actions on it (capture a new binding, remove one, reset the row). Edits go through <see cref="IInputService"/>, which saves them; the chip list
/// follows in <see cref="Sync"/>, which the editor runs after the click that caused the change has finished routing.
/// </summary>
public partial class ShortcutRowViewModel : ViewModelBase
{
    private readonly InputActionInfo _info;
    private readonly IInputService _input;
    private readonly Action<ShortcutRowViewModel>? _select;

    public ShortcutRowViewModel(InputActionInfo info, IInputService input, Action<ShortcutRowViewModel>? select = null)
    {
        _info = info;
        _input = input;
        _select = select;
        Chips = new ObservableCollection<ShortcutChip>(input.GetBindings(info.Action).Select(b => new ShortcutChip(b)));
        Conflicts = new ObservableCollection<ShortcutConflictInfo>();
        Defaults = info.Defaults.Select(b => new ShortcutChip(b)).ToList();
        UpdateDerivedState();
    }

    public string ActionId => _info.Id;

    public InputAction Action => _info.Action;

    public string Label => _info.Label;

    public string Group => _info.Group;

    public ObservableCollection<ShortcutChip> Chips { get; }

    /// <summary>The bindings the action ships with, shown read-only in the editor pane.</summary>
    public IReadOnlyList<ShortcutChip> Defaults { get; }

    /// <summary>Where the action works, in words: "comic reader, paged mode", "library", "everywhere".</summary>
    public string ScopeText => InputScopeDisplay.Describe(_info.Scope, _info.Context);

    /// <summary>"Navigation · comic reader, paged mode", the editor pane's subtitle.</summary>
    public string Subtitle => $"{_info.Group} · {ScopeText}";

    /// <summary>False for the controller's analogue axes, whose bindings are fixed to a stick or the triggers and cannot be captured from a key press.</summary>
    public bool CanCapture => _info.Kind == InputActionKind.Button;

    public bool HasNoBindings => Chips.Count == 0;

    /// <summary>The list shows the first binding only; the editor pane shows them all.</summary>
    public ShortcutChip? SummaryChip => Chips.FirstOrDefault();

    public string SummaryLabel => SummaryChip?.Label ?? "Not set";

    public bool SummaryIsPad => SummaryChip?.IsPad == true;

    public bool HasExtra => Chips.Count > 1;

    /// <summary>"+2": how many more bindings the list row leaves out.</summary>
    public string ExtraText => Chips.Count > 1 ? $"+{Chips.Count - 1}" : string.Empty;

    /// <summary>True when the user has changed this action from its defaults, which enables the row's reset button.</summary>
    [ObservableProperty]
    private bool _isCustomised;

    /// <summary>One of this row's bindings collides with another row's - set by <see cref="ShortcutsEditorViewModel"/>. Shown as a small warning icon, never a coloured row.</summary>
    [ObservableProperty]
    private bool _isConflicted;

    /// <summary>The row the editor pane is showing; highlights it in the list.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Shows the capture box ("press your combination") in place of the add button.</summary>
    [ObservableProperty]
    private bool _isCapturing;

    /// <summary>What this row collides with, for the editor pane.</summary>
    public ObservableCollection<ShortcutConflictInfo> Conflicts { get; }

    public bool HasConflictDetails => Conflicts.Count > 0;

    [RelayCommand]
    private void Select() => _select?.Invoke(this);

    /// <summary>Selects the action a conflict names. Deferred a tick: it is run from a button inside the editor pane, and selecting swaps that pane's content, detaching the button mid-click otherwise.</summary>
    [RelayCommand]
    private void SelectConflictOther(ShortcutConflictInfo conflict) => Dispatcher.UIThread.Post(() => conflict.Other.Select());

    [RelayCommand]
    private void BeginCapture() => IsCapturing = CanCapture;

    /// <summary>Deferred a tick: the capture box reports from its own key or pointer event, and hiding it inside that event is the same detach-while-routing hazard as removing a chip.</summary>
    [RelayCommand]
    private void CancelCapture() => Dispatcher.UIThread.Post(() => IsCapturing = false);

    [RelayCommand]
    private void AddBinding(InputBinding binding)
    {
        Dispatcher.UIThread.Post(() => IsCapturing = false);
        var current = _input.GetBindings(_info.Action);
        if (!binding.IsDefined || current.Contains(binding))
        {
            return;
        }

        _input.SetBindings(_info.Action, [.. current, binding]);
    }

    [RelayCommand]
    private void RemoveChip(ShortcutChip chip) =>
        _input.SetBindings(_info.Action, _input.GetBindings(_info.Action).Where(b => b != chip.Binding).ToList());

    [RelayCommand]
    private void ResetRow() => _input.ResetBindings(_info.Action);

    /// <summary>True when every typed word is in the action's label, its group's title or the text of one of its bindings.</summary>
    internal bool MatchesSearch(string[] terms)
    {
        if (terms.Length == 0)
        {
            return true;
        }

        string haystack = $"{Label} {Group} {string.Join(' ', Chips.Select(c => c.Label))}";
        return terms.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    internal void ClearConflicts()
    {
        IsConflicted = false;
        Conflicts.Clear();
        foreach (var chip in Chips)
        {
            chip.IsConflicted = false;
        }

        OnPropertyChanged(nameof(HasConflictDetails));
    }

    /// <summary>Records that <paramref name="chip"/> (one of this row's inputs) is also bound to <paramref name="other"/>, and flags the row and the chip.</summary>
    internal void AddConflict(ShortcutRowViewModel other, ShortcutChip chip)
    {
        IsConflicted = true;
        chip.IsConflicted = true;
        if (Conflicts.Any(c => ReferenceEquals(c.Other, other) && c.BindingLabel == chip.Label))
        {
            return;
        }

        bool otherAlways = other._info.Context == InputContext.Always;
        bool thisAlways = _info.Context == InputContext.Always;
        bool shadows = otherAlways && !thisAlways;
        string message = shadows
            ? $"\"{chip.Label}\" is also bound to \"{other.Label}\", which is always available, so it can take the press instead of this action."
            : thisAlways && !otherAlways
                ? $"\"{chip.Label}\" is also bound to \"{other.Label}\" ({other.ScopeText}). This action is always available, so it can take the press from that one."
                : $"\"{chip.Label}\" is also bound to \"{other.Label}\", and both can be active at the same time.";
        Conflicts.Add(new ShortcutConflictInfo(other, chip.Label, message, shadows));
        OnPropertyChanged(nameof(HasConflictDetails));
    }

    /// <summary>Makes the chip list match the service's current bindings (a no-op when it already does).</summary>
    public void Sync()
    {
        var current = _input.GetBindings(_info.Action);
        if (!current.SequenceEqual(Chips.Select(c => c.Binding)))
        {
            Chips.Clear();
            foreach (var binding in current)
            {
                Chips.Add(new ShortcutChip(binding));
            }
        }

        UpdateDerivedState();
    }

    private void UpdateDerivedState()
    {
        IsCustomised = !Chips.Select(c => c.Binding).SequenceEqual(_info.Defaults);
        OnPropertyChanged(nameof(HasNoBindings));
        OnPropertyChanged(nameof(SummaryChip));
        OnPropertyChanged(nameof(SummaryLabel));
        OnPropertyChanged(nameof(SummaryIsPad));
        OnPropertyChanged(nameof(HasExtra));
        OnPropertyChanged(nameof(ExtraText));
    }
}
