using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// Code-behind for <see cref="MangaSeriesEditorOverlay"/> (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §6). Shortcuts go through the input service like every
/// other editor: Ctrl+S saves even from inside a text field, and the tab-switch actions step the strip. Present from the first commit alongside the .axaml (CLAUDE.md's AVLN2000 gotcha).
/// </summary>
public partial class MangaSeriesEditorOverlay : UserControl
{
    private readonly AttachedInputRegistration _screenInput;

    /// <summary>The input service this overlay's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _screenInput.Service;
        set => _screenInput.Service = value;
    }

    public MangaSeriesEditorOverlay()
    {
        InitializeComponent();
        KeyDown += (_, e) => e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
        _screenInput = ScreenInput.Attach(this, InputScope.Editor, new Dictionary<string, Func<bool>>
        {
            // The editor closes itself on save, so the rebuild runs after the key press has finished routing.
            [InputActionIds.Save] = () => DataContext is MangaSeriesEditorViewModel vm && vm.SaveCommand.CanExecute(null) && ScreenInput.Deferred(() => vm.SaveCommand.Execute(null)),
            [InputActionIds.TabNext] = () => TabStrip.Step(this, 1),
            [InputActionIds.TabPrevious] = () => TabStrip.Step(this, -1),
        });
    }
}
