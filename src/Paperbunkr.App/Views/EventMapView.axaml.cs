using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// Code-behind for the Event Map (docs/superpowers/specs/2026-09-25-event-map-design.md §3-§4): wires the surface's card
/// presses to the view model, keeps the pinned ruler and lane headers in step with the scroll offset, handles the
/// map's own keyboard (control behavior rather than an input-service action - it only applies while the map has focus),
/// Ctrl+wheel density and Shift+wheel horizontal scroll, and scrolls cards into view.
/// </summary>
public partial class EventMapView : UserControl
{
    private const double RevealMargin = 24;
    private const double ShiftWheelStep = 80;

    private EventMapViewModel? _vm;
    private double _centerRatioX = 0.5;
    private double _centerRatioY = 0.5;

    public EventMapView()
    {
        InitializeComponent();
        Surface.CardPressed += OnCardPressed;
        Ruler.BandClicked += block => _vm?.OpenBand(block);
        Scroller.ScrollChanged += OnScrollChanged;
        Scroller.AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Tunnel, not bubble: the ScrollViewer would otherwise consume the arrow keys for scrolling before they reach us.
        AddHandler(KeyDownEvent, OnMapKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.RevealRequested -= OnRevealRequested;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = DataContext as EventMapViewModel;
        if (_vm is not null)
        {
            _vm.RevealRequested += OnRevealRequested;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnCardPressed(int index, int clickCount)
    {
        if (_vm is null)
        {
            return;
        }

        _vm.ActivateCard(index);
        if (clickCount >= 2)
        {
            _vm.OpenReaderCommand.Execute(null);
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var offset = Scroller.Offset;
        Ruler.OffsetX = offset.X;
        LaneHeaders.RenderTransform = new TranslateTransform(0, -offset.Y);

        var extent = Scroller.Extent;
        var viewport = Scroller.Viewport;
        if (extent.Width > 0)
        {
            _centerRatioX = (offset.X + (viewport.Width / 2)) / extent.Width;
        }

        if (extent.Height > 0)
        {
            _centerRatioY = (offset.Y + (viewport.Height / 2)) / extent.Height;
        }
    }

    /// <summary>
    /// A new layout (density, filter, spine or event change): once it has been measured, keep the selected card centred,
    /// or failing that the previous viewport centre.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EventMapViewModel.Layout))
        {
            return;
        }

        double ratioX = _centerRatioX;
        double ratioY = _centerRatioY;
        Dispatcher.UIThread.Post(() =>
        {
            if (_vm is null)
            {
                return;
            }

            if (_vm.SelectedIndex is int selected && selected < _vm.Layout.Cells.Count)
            {
                CenterOn(_vm.Layout.CardRect(selected).Center);
            }
            else
            {
                var total = _vm.Layout.TotalSize;
                CenterOn(new Point(ratioX * total.Width, ratioY * total.Height));
            }
        }, DispatcherPriority.Background);
    }

    private void CenterOn(Point point)
    {
        var viewport = Scroller.Viewport;
        Scroller.Offset = new Vector(Math.Max(0, point.X - (viewport.Width / 2)), Math.Max(0, point.Y - (viewport.Height / 2)));
    }

    /// <summary>Keyboard moves and link clicks: scroll just enough to bring the card fully into view, then focus it.</summary>
    private void OnRevealRequested(int index)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_vm is null || index >= _vm.Layout.Cells.Count)
            {
                return;
            }

            var rect = _vm.Layout.CardRect(index).Inflate(RevealMargin);
            var offset = Scroller.Offset;
            var viewport = Scroller.Viewport;
            double x = offset.X;
            double y = offset.Y;
            if (rect.Left < x) x = rect.Left;
            else if (rect.Right > x + viewport.Width) x = rect.Right - viewport.Width;
            if (rect.Top < y) y = rect.Top;
            else if (rect.Bottom > y + viewport.Height) y = rect.Bottom - viewport.Height;
            Scroller.Offset = new Vector(Math.Max(0, x), Math.Max(0, y));

            // The card is realized on the layout pass that follows the scroll.
            Dispatcher.UIThread.Post(() =>
            {
                if (IsKeyboardFocusWithin && _vm?.SelectedIndex == index)
                {
                    Surface.CardAt(index)?.Focus(NavigationMethod.Directional);
                }
            }, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _vm.StepDensity(e.Delta.Y > 0 ? 1 : -1);
            e.Handled = true;
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Delta.X == 0)
        {
            var offset = Scroller.Offset;
            Scroller.Offset = new Vector(Math.Max(0, offset.X - (e.Delta.Y * ShiftWheelStep)), offset.Y);
            e.Handled = true;
        }
    }

    /// <summary>Map keys apply while focus is on the map itself (a card or the scroller); Esc also works from the inspector.</summary>
    private void OnMapKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _vm is null || !_vm.HasMap || e.Source is TextBox || IsWithin<Controls.SuggestBox>(e.Source))
        {
            return;
        }

        if (e.Key != Key.Escape && !IsWithin(e.Source, Scroller))
        {
            return;
        }

        e.Handled = HandleKey(_vm, e.Key, e.KeyModifiers);
    }

    /// <summary>The map's keyboard (spec §4). Returns whether the key was used. Internal for headless tests.</summary>
    internal static bool HandleKey(EventMapViewModel vm, Key key, KeyModifiers modifiers)
    {
        switch (key)
        {
            case Key.Right:
                vm.MoveNext();
                return true;
            case Key.Left:
                vm.MovePrevious();
                return true;
            case Key.Up:
                vm.MoveUp();
                return true;
            case Key.Down:
                vm.MoveDown();
                return true;
            case Key.Home:
                vm.MoveFirst();
                return true;
            case Key.End:
                vm.MoveLast();
                return true;
            case Key.Enter when modifiers.HasFlag(KeyModifiers.Control):
                vm.OpenReaderCommand.Execute(null);
                return true;
            case Key.Enter:
                vm.OpenInspectorCommand.Execute(null);
                return true;
            case Key.Escape:
                return vm.Escape();
            default:
                return false;
        }
    }

    private static bool IsWithin<T>(object? source)
    {
        for (var element = source as StyledElement; element is not null; element = element.Parent)
        {
            if (element is T)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWithin(object? source, StyledElement ancestor)
    {
        for (var element = source as StyledElement; element is not null; element = element.Parent)
        {
            if (ReferenceEquals(element, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
