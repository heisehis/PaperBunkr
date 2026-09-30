using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class WantedScreen : UserControl
{
    /// <summary>Keeps keyboard focus inside the screen (docs/superpowers/specs/2026-09-29-keyboard-focus-reclaim-phases-2-6-design.md): the
    /// Queue/Series/Releases bodies are permanently attached and toggled by <c>IsVisible</c>, and a row's own Remove/Grab button (or a
    /// refresh) drops the row focus lived in. Falls back to the last-used row of the visible list, else the active tab header.</summary>
    private readonly FocusReclaimer _focus;

    private int? _lastRowIndex;

    public WantedScreen()
    {
        InitializeComponent();
        _focus = new FocusReclaimer(this, () => DataContext is WantedScreenViewModel, FocusFallback);
        DataContextChanged += OnDataContextChanged;
        AddHandler(GotFocusEvent, (_, _) =>
        {
            if (ActiveRowList() is { } list && VirtualizedFocus.FocusedIndex(list) is var index and >= 0)
            {
                _lastRowIndex = index;
            }
        });
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

    private ItemsControl? ActiveRowList() =>
        this.GetVisualDescendants().OfType<ItemsControl>()
            .FirstOrDefault(l => l.Classes.Contains("vlist") && l.IsEffectivelyVisible && l.ItemCount > 0);

    private void FocusFallback()
    {
        if (ActiveRowList() is { } list)
        {
            VirtualizedFocus.FocusIndex(list, _lastRowIndex ?? 0, 1);
            return;
        }

        FocusReclaimer.FocusFirstButton(this, b => b.Classes.Contains("tab") && b.Classes.Contains("active"));
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not WantedScreenViewModel vm)
        {
            return;
        }

        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WantedScreenViewModel.ActiveTab))
            {
                _lastRowIndex = null;
                _focus.ReclaimIfFocusWithinOrNowhere();
            }
        };
        vm.QueueItems.CollectionChanged += (_, _) => _focus.ReclaimIfFocusWithinOrNowhere();
        vm.SeriesRows.CollectionChanged += (_, _) => _focus.ReclaimIfFocusWithinOrNowhere();
        vm.ReleaseDays.CollectionChanged += (_, _) => _focus.ReclaimIfFocusWithinOrNowhere();
    }
}
