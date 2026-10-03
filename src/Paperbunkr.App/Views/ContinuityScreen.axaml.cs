using System;
using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.ViewModels;
using System.Collections.Generic;
using Paperbunkr.App.Services.Input;

namespace Paperbunkr.App.Views;

/// <summary>The Continuity screen (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md) - see the .axaml for the layout.</summary>
public partial class ContinuityScreen : UserControl
{
    /// <summary>Keeps keyboard focus inside the screen (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md): the
    /// Overview/Timeline/Map/Suggestions bodies are permanently attached and toggled by <c>IsVisible</c>, so a page or view switch hides
    /// whatever held focus. Falls back to the active view-toggle chip (or the empty prompt's first button).</summary>
    private readonly FocusReclaimer _focus;

    private readonly AttachedInputRegistration _screenInput;

    /// <summary>The input service this screen's actions arrive through: the application's, unless a test supplies its own.</summary>
    public IInputService InputService
    {
        get => _screenInput.Service;
        set => _screenInput.Service = value;
    }

    public ContinuityScreen()
    {
        InitializeComponent();
        _screenInput = ScreenInput.Attach(this, InputScope.Continuity, new Dictionary<string, Func<bool>>
        {
            [InputActionIds.TabNext] = () => TabStrip.Step(this, 1),
            [InputActionIds.TabPrevious] = () => TabStrip.Step(this, -1),
            [InputActionIds.Refresh] = () => DataContext is ContinuityScreenViewModel vm && ScreenInput.Deferred(vm.RefreshSidebar),
        });
        _focus = new FocusReclaimer(this, () => DataContext is ContinuityScreenViewModel,
            () => FocusReclaimer.FocusFirstButton(this, b => b.Classes.Contains("segToggle") && b.Classes.Contains("on")));
        KeyDown += (_, e) => e.Handled = FocusReclaimer.TryMoveDirectionally(this, e);
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                _focus.Reclaim();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _focus.Reclaim();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ContinuityScreenViewModel vm)
        {
            return;
        }

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ContinuityScreenViewModel.Page) or nameof(ContinuityScreenViewModel.DetailView))
            {
                _focus.ReclaimIfFocusWithinOrNowhere();
            }
        };
        vm.Timeline.Sections.CollectionChanged += (_, _) => _focus.ReclaimIfFocusWithinOrNowhere();
    }
}
