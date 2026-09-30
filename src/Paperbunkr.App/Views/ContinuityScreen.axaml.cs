using System;
using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>The Continuity screen (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md) - see the .axaml for the layout.</summary>
public partial class ContinuityScreen : UserControl
{
    /// <summary>Keeps keyboard focus inside the screen (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md): the
    /// Overview/Timeline/Map/Suggestions bodies are permanently attached and toggled by <c>IsVisible</c>, so a page or view switch hides
    /// whatever held focus. Falls back to the active view-toggle chip (or the empty prompt's first button).</summary>
    private readonly FocusReclaimer _focus;

    public ContinuityScreen()
    {
        InitializeComponent();
        _focus = new FocusReclaimer(this, () => DataContext is ContinuityScreenViewModel,
            () => FocusReclaimer.FocusFirstButton(this, b => b.Classes.Contains("segToggle") && b.Classes.Contains("on")));
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
