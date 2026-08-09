using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// One row in Preferences &gt; Keyboard Shortcuts, wrapping a single <see cref="KeyboardCommandDescriptor"/>
/// (docs/alpha-roadmap.md P5 follow-up). Persists immediately on selection, same as every other
/// Preferences toggle - there's no Save/Cancel step here.
/// </summary>
public partial class KeyBindingRowViewModel : ViewModelBase
{
    private readonly KeyboardCommandDescriptor _command;
    private readonly KeyBindingService _service;
    private readonly Action _onChanged;

    public KeyBindingRowViewModel(KeyboardCommandDescriptor command, Key currentKey, KeyBindingService service, Action onChanged)
    {
        _command = command;
        _service = service;
        _onChanged = onChanged;

        // currentKey might not be in the curated Options list (e.g. a stale/manually-edited DB
        // row) - fall back to a synthetic option for it rather than silently snapping to the
        // first candidate, so the picker shows what's actually bound instead of lying about it.
        var match = KeyOptions.All.Where(o => o.Key == currentKey).Cast<KeyOption?>().FirstOrDefault();
        _selectedKey = match ?? new KeyOption(currentKey, currentKey.ToString());
    }

    public string Group => _command.Group;

    public string Label => _command.Label;

    public string CommandId => _command.Id;

    public IReadOnlyList<KeyOption> Options => KeyOptions.All;

    [ObservableProperty]
    private KeyOption _selectedKey;

    partial void OnSelectedKeyChanged(KeyOption value)
    {
        _service.SetKey(CommandId, value.Key);
        _onChanged();
    }
}
