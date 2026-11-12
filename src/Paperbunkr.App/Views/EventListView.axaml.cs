using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// The map's List view (docs/superpowers/specs/2026-11-11-continuity-list-atlas-views-design.md §1). Rows and headers are real controls in
/// a virtualizing ListBox; this code-behind syncs the ListBox selection with the map's shared selection, pins the top section's header,
/// marks the current section in the jump bar, and turns Enter / double-click into the inspector / reader.
/// </summary>
public partial class EventListView : UserControl
{
    /// <summary>Below this width the jump bar shrinks to a strip of progress marks (§1 "Responsive").</summary>
    private const double NarrowWidth = 900;

    private EventMapViewModel? _vm;
    private bool _syncingSelection;

    public EventListView()
    {
        InitializeComponent();
        Rows.SelectionChanged += OnSelectionChanged;
        Rows.DoubleTapped += OnDoubleTapped;
        Rows.AddHandler(KeyDownEvent, OnRowsKeyDown, RoutingStrategies.Tunnel);
        Rows.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => UpdatePinnedHeader(), RoutingStrategies.Bubble);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                bool narrow = Bounds.Width > 0 && TopLevel.GetTopLevel(this) is { } top && top.Bounds.Width < NarrowWidth;
                Root.Classes.Set("narrow", narrow);
                JumpBar.Width = narrow ? 44 : 180;
            }
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.ScrollToListSectionRequested -= OnScrollToSection;
            _vm.ListRebuilt -= OnListRebuilt;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as EventMapViewModel;
        if (_vm is not null)
        {
            _vm.ScrollToListSectionRequested += OnScrollToSection;
            _vm.ListRebuilt += OnListRebuilt;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EventMapViewModel.SelectedIndex))
        {
            SyncSelectionFromViewModel();
        }
    }

    private void OnListRebuilt() => Dispatcher.UIThread.Post(SyncSelectionFromViewModel);

    private void SyncSelectionFromViewModel()
    {
        if (_vm is null)
        {
            return;
        }

        var target = _vm.ListItems.OfType<EventListIssueItem>().FirstOrDefault(r => r.Card.Index == _vm.SelectedIndex);
        if (!ReferenceEquals(Rows.SelectedItem, target))
        {
            _syncingSelection = true;
            Rows.SelectedItem = target;
            _syncingSelection = false;
        }
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || _vm is null)
        {
            return;
        }

        if (Rows.SelectedItem is EventListIssueItem row && row.Card.Index != _vm.SelectedIndex)
        {
            _vm.Select(row.Card.Index, reveal: false);
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is not null && Rows.SelectedItem is EventListIssueItem)
        {
            _vm.OpenReaderCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnRowsKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when Rows.SelectedItem is EventListIssueItem row:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                {
                    _vm.OpenReaderCommand.Execute(null);
                }
                else
                {
                    _vm.ActivateCard(row.Card.Index);
                }

                e.Handled = true;
                break;
            case Key.Enter or Key.Space when Rows.SelectedItem is EventListHeaderItem header:
                _vm.ToggleListSectionCommand.Execute(header);
                e.Handled = true;
                break;
            case Key.Left when Rows.SelectedItem is EventListIssueItem row:
                // Left on a row goes to its section header.
                Rows.SelectedItem = _vm.HeaderFor(row.SectionKey);
                Rows.ScrollIntoView(Rows.SelectedItem!);
                e.Handled = true;
                break;
            case Key.Left when Rows.SelectedItem is EventListHeaderItem { IsExpanded: true } header:
            case Key.Right when Rows.SelectedItem is EventListHeaderItem { IsExpanded: false } header2:
                _vm.ToggleListSectionCommand.Execute(Rows.SelectedItem as EventListHeaderItem);
                e.Handled = true;
                break;
            case Key.Escape:
                e.Handled = _vm.Escape();
                break;
        }
    }

    private void OnScrollToSection(int key)
    {
        if (_vm?.HeaderFor(key) is not { } header)
        {
            return;
        }

        int index = _vm.ListItems.IndexOf(header);
        if (index < 0)
        {
            return;
        }

        // Scroll past the end first, then back to the header, so it lands at the top rather than the bottom of the viewport.
        Rows.ScrollIntoView(_vm.ListItems.Count - 1);
        Rows.ScrollIntoView(index);
        Rows.SelectedItem = header;
        Dispatcher.UIThread.Post(UpdatePinnedHeader);
    }

    /// <summary>Pins the header of the section that owns the top visible row; hides it when that section's own header is on screen.</summary>
    private void UpdatePinnedHeader()
    {
        if (_vm is null)
        {
            return;
        }

        EventListItem? top = null;
        double topY = double.MaxValue;
        foreach (var container in Rows.GetRealizedContainers())
        {
            if (container.DataContext is not EventListItem item || container.TranslatePoint(default, Rows) is not { } point)
            {
                continue;
            }

            if (point.Y + container.Bounds.Height > 0 && point.Y < topY)
            {
                topY = point.Y;
                top = item;
            }
        }

        if (top is null)
        {
            PinnedHeader.IsVisible = false;
            _vm.SetCurrentListSection(null);
            return;
        }

        _vm.SetCurrentListSection(top.SectionKey);
        if (top is EventListHeaderItem || topY >= 0 || _vm.HeaderFor(top.SectionKey) is not { } header)
        {
            PinnedHeader.IsVisible = false;
            return;
        }

        PinnedHeaderText.Text = header.Label;
        PinnedHeaderCount.Text = header.CountLabel;
        PinnedHeader.IsVisible = true;
    }
}
