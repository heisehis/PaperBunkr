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

/// <summary>
/// Preferences &gt; Keyboard Shortcuts (docs/superpowers/specs/2026-10-03-input-service-design.md §9), rebuilt on the input service: one row per registered action, grouped as the action
/// catalog groups them, each showing every key, mouse button, wheel direction and gamepad input bound to it. Data-driven off <see cref="IInputActionCatalog"/>, so an action a plugin
/// registers later gets a row with no change here. Changes apply and persist immediately, like every other Preferences control. This replaces the registry-backed editor and its curated
/// key list, which could not express a mouse or wheel binding.
/// </summary>
public partial class ShortcutsEditorViewModel : ViewModelBase
{
    private readonly IInputService _input;
    private readonly IFilePickerService _filePicker;
    private readonly Action<string, string> _showToast;
    private bool _syncQueued;

    public ShortcutsEditorViewModel(IInputService input, IFilePickerService filePicker, Action<string, string> showToast)
    {
        _input = input;
        _filePicker = filePicker;
        _showToast = showToast;
        Groups = new ObservableCollection<ShortcutGroupViewModel>();
        _input.BindingsChanged += OnBindingsChanged;
        Refresh();
    }

    public ObservableCollection<ShortcutGroupViewModel> Groups { get; }

    /// <summary>The first binding shared by two actions that could be active together, shown as a banner; null when there is none.</summary>
    [ObservableProperty]
    private string? _conflictError;

    public bool HasConflictError => !string.IsNullOrEmpty(ConflictError);

    partial void OnConflictErrorChanged(string? value) => OnPropertyChanged(nameof(HasConflictError));

    /// <summary>Rebuilds every group and row from the catalog (also the way to pick up an action registered after this screen was built).</summary>
    public void Refresh()
    {
        Groups.Clear();
        foreach (var group in _input.Actions.All.GroupBy(info => info.Group))
        {
            Groups.Add(new ShortcutGroupViewModel(group.Key, group.Select(info => new ShortcutRowViewModel(info, _input)).ToList()));
        }

        RecomputeConflicts();
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
    /// sits, though the banner names only the first.
    /// </summary>
    private void RecomputeConflicts()
    {
        var rows = Groups.SelectMany(g => g.Rows).ToList();
        var byAction = rows.ToDictionary(r => r.ActionId, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            row.IsConflicted = false;
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

                    row.IsConflicted = true;
                    other.IsConflicted = true;
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

/// <summary>One bound input on a row, shown as a removable chip.</summary>
public sealed record ShortcutChip(InputBinding Binding)
{
    public string Label => InputBindingDisplay.Format(Binding);
}

/// <summary>
/// One action's row: its chips, plus the actions on it (capture a new binding, remove one, reset the row). Edits go through <see cref="IInputService"/>, which saves them; the chip list
/// follows in <see cref="Sync"/>, which the editor runs after the click that caused the change has finished routing.
/// </summary>
public partial class ShortcutRowViewModel : ViewModelBase
{
    private readonly InputActionInfo _info;
    private readonly IInputService _input;

    public ShortcutRowViewModel(InputActionInfo info, IInputService input)
    {
        _info = info;
        _input = input;
        Chips = new ObservableCollection<ShortcutChip>(input.GetBindings(info.Action).Select(b => new ShortcutChip(b)));
        UpdateDerivedState();
    }

    public string ActionId => _info.Id;

    public InputAction Action => _info.Action;

    public string Label => _info.Label;

    public string Group => _info.Group;

    public ObservableCollection<ShortcutChip> Chips { get; }

    /// <summary>False for the controller's analogue axes, whose bindings are fixed to a stick or the triggers and cannot be captured from a key press.</summary>
    public bool CanCapture => _info.Kind == InputActionKind.Button;

    public bool HasNoBindings => Chips.Count == 0;

    /// <summary>True when the user has changed this action from its defaults, which enables the row's reset button.</summary>
    [ObservableProperty]
    private bool _isCustomised;

    /// <summary>Highlights the row when one of its bindings collides with another row's - set by <see cref="ShortcutsEditorViewModel"/>.</summary>
    [ObservableProperty]
    private bool _isConflicted;

    /// <summary>Shows the capture box ("press your combination") in place of the add button.</summary>
    [ObservableProperty]
    private bool _isCapturing;

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
    }
}
