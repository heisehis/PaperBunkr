using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in Preferences &gt; Keyboard Shortcuts, wrapping a single <see cref="KeyboardCommandDescriptor"/>
/// (docs/Paperbunkr-Roadmap.md P5 follow-up). A command may be bound to more than one gesture
/// simultaneously (docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md, closing
/// a real CE-parity gap - CE supports 4 slots per command). Persists immediately on add/remove, same
/// as every other Preferences toggle - there's no Save/Cancel step here.
/// </summary>
public partial class KeyBindingRowViewModel : ViewModelBase
{
    private readonly KeyboardCommandDescriptor _command;
    private readonly KeyBindingService _service;
    private readonly Action _onChanged;

    public KeyBindingRowViewModel(KeyboardCommandDescriptor command, IReadOnlyList<KeyGesture> currentGestures, KeyBindingService service, Action onChanged)
    {
        _command = command;
        _service = service;
        _onChanged = onChanged;

        // A stored gesture might not be in the curated Options list (e.g. a stale/manually-edited DB
        // row) - fall back to a synthetic option for it rather than silently dropping it, so the
        // chip list shows what's actually bound instead of lying about it.
        var options = currentGestures.Select(gesture =>
            KeyOptions.All.Where(o => o.Gesture == gesture).Cast<KeyOption?>().FirstOrDefault() ?? new KeyOption(gesture, gesture.ToString()));
        BoundKeys = new ObservableCollection<KeyOption>(options);
    }

    public string Group => _command.Group;

    public string Label => _command.Label;

    public string CommandId => _command.Id;

    /// <summary>Drives PreferencesScreenViewModel's pairwise conflict check - see ConflictContext's own doc comment.</summary>
    public ConflictContext Context => _command.Context;

    /// <summary>Every gesture currently bound to this command - at least one, never empty (see <see cref="RemoveKey"/>).</summary>
    public ObservableCollection<KeyOption> BoundKeys { get; }

    /// <summary>The curated options not already bound to this row - feeds the trailing "Add shortcut…" picker so it never offers a gesture this row already has.</summary>
    public IReadOnlyList<KeyOption> AvailableKeyOptions => KeyOptions.All.Where(o => !BoundKeys.Contains(o)).ToList();

    /// <summary>Labels of <see cref="AvailableKeyOptions"/> for the string-only <c>SuggestBox</c>
    /// "Add shortcut…" picker (docs/superpowers/specs/2026-09-10-suggestbox-migration-plan.md).
    /// <see cref="KeyOption.Label"/> is a stable unique display string, so the label round-trips
    /// back to the gesture in <see cref="PendingAddText"/>.</summary>
    public IReadOnlyList<string> AvailableKeyOptionNames => AvailableKeyOptions.Select(o => o.Label).ToList();

    /// <summary>String twin of <see cref="PendingAddOption"/> for the <c>SuggestBox</c> picker. The
    /// getter is always empty so the field snaps back to its watermark after a pick; the setter
    /// resolves the label to a curated option and runs <see cref="AddKeyCommand"/> as a side
    /// effect. The reset notification is posted so it lands after <c>SuggestBox</c> finishes its
    /// own text sync.</summary>
    public string PendingAddText
    {
        get => string.Empty;
        set
        {
            if (!string.IsNullOrEmpty(value) &&
                AvailableKeyOptions.FirstOrDefault(o => o.Label == value) is { Gesture: not null } option)
            {
                AddKeyCommand.Execute(option);
            }

            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(PendingAddText)));
        }
    }

    /// <summary>
    /// The trailing "Add shortcut…" ComboBox's SelectedItem target - a virtual property, not real
    /// state (docs/superpowers/specs/2026-09-07-keyboard-shortcuts-redesign-design.md §6). The
    /// getter always returns null so the ComboBox visually resets to its placeholder right after a
    /// selection is applied; the setter runs <see cref="AddKeyCommand"/> as a side effect, avoiding
    /// a separate SelectionChanged code-behind handler for this repeated-3x row template.
    /// </summary>
    public KeyOption? PendingAddOption
    {
        get => null;
        set
        {
            if (value is { } option)
            {
                AddKeyCommand.Execute(option);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>Feeds the remove chip's IsVisible - a command can never end up with zero bound gestures, so the remove control hides on a single-chip row (defense in depth alongside RemoveKey's own no-op).</summary>
    public static readonly IValueConverter CountGreaterThanOne = new FuncValueConverter<int, bool>(count => count > 1);

    /// <summary>Highlights this row when one of its gestures collides with another row's - set by PreferencesScreenViewModel.RecomputeKeyBindingConflict.</summary>
    [ObservableProperty]
    private bool _isConflicted;

    [RelayCommand]
    private void AddKey(KeyOption option)
    {
        if (BoundKeys.Contains(option))
        {
            return;
        }

        // A fresh row's BoundKeys can start as a synthetic "default" that was never an explicit DB
        // row (KeyBindingService.GetKeys' own zero-stored-rows-means-default fallback). Customizing
        // beyond that single default by adding a second gesture must persist the whole currently-
        // displayed set as real rows (each AddKey call is idempotent, so this is a no-op for any
        // already-stored gesture) - otherwise a later reload would silently drop whatever was only
        // ever a display-time default, even though it's still showing as a chip right now.
        foreach (var existing in BoundKeys)
        {
            _service.AddKey(CommandId, existing.Gesture);
        }

        _service.AddKey(CommandId, option.Gesture);
        BoundKeys.Add(option);
        OnPropertyChanged(nameof(AvailableKeyOptions));
        OnPropertyChanged(nameof(AvailableKeyOptionNames));
        _onChanged();
    }

    /// <summary>
    /// A command can never end up with zero bound gestures (docs/superpowers/specs/2026-09-07-
    /// keyboard-shortcuts-redesign-design.md's Non-goals) - the View also hides the remove control
    /// on a single-chip row, this is defense in depth, not the only guard.
    /// </summary>
    [RelayCommand]
    private void RemoveKey(KeyOption option)
    {
        if (BoundKeys.Count <= 1)
        {
            return;
        }

        _service.RemoveKey(CommandId, option.Gesture);

        // Deferred: this command runs from the chip's own ✕ Button.Click still routing through
        // BoundKeys' own ItemsControl - removing the item here would detach that same chip mid-route
        // and crash Avalonia's detach walk with an ArgumentOutOfRangeException (see
        // Paperbunkr.App.Controls.SuggestBox.Commit for the fully diagnosed case).
        Dispatcher.UIThread.Post(() =>
        {
            BoundKeys.Remove(option);
            OnPropertyChanged(nameof(AvailableKeyOptions));
            OnPropertyChanged(nameof(AvailableKeyOptionNames));
            _onChanged();
        });
    }
}
