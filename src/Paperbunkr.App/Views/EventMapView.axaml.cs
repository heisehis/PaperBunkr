using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.Services.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>Connects the drawn map to its view model and handles focused-item navigation.</summary>
public partial class EventMapView : UserControl
{
    private EventMapViewModel? _vm;
    private int _bandClickGeneration;
    private readonly AttachedInputRegistration _mapInput;

    public EventMapView()
    {
        InitializeComponent();
        _mapInput = new AttachedInputRegistration(this, InputScope.Continuity, OnMapInput, service: InputServiceLocator.Current, focusRoot: () => this);
        MapCanvas.CardPressed += OnCardPressed;
        MapCanvas.SelectionCleared += () => _vm?.Select(null, reveal: false);
        MapCanvas.BandPressed += OnBandPressed;
        MapCanvas.LanePressed += _ => _vm?.ToggleShortLanesCommand.Execute(null);
        MatrixCanvas.CardPressed += OnCardPressed;
        MatrixCanvas.OpenOnMap += index => _vm?.OpenMatrixIssueOnMap(index);
        AtlasCanvas.OpenEvent += eventId => _vm?.OpenAtlasEvent(eventId);
        MapCanvas.BracketCreatorPressed += creator => _vm?.ToggleCreatorHighlight(creator);
        MapCanvas.PeekChanged += index =>
        {
            if (_vm is not null) _vm.PeekCard = index is int i && i < _vm.Cards.Count ? _vm.Cards[i] : null;
        };
        MapCanvas.ViewportChanged += UpdateMinimumScale;
        AddHandler(KeyDownEvent, OnMapKeyDown, RoutingStrategies.Tunnel);
    }

    public IInputService InputService { get => _mapInput.Service; set => _mapInput.Service = value; }

    private void OnMapInput(InputActionEventArgs e)
    {
        if (_vm is null || !_vm.HasMap || !_vm.IsMapMode) return;
        bool zoomIn = e.Action.Id == InputActionIds.ContinuityMapZoomIn;
        if (!zoomIn && e.Action.Id != InputActionIds.ContinuityMapZoomOut) return;
        var point = e.GetPosition(MapCanvas);
        if (point is { } p && !new Rect(MapCanvas.Bounds.Size).Contains(p)) return;
        var anchor = point is { } cursor ? new Point(cursor.X - MapCanvas.RailWidth, cursor.Y - MapCanvas.RulerHeight)
            : new Point(MapCanvas.ContentViewport.Width / 2, MapCanvas.ContentViewport.Height / 2);
        double notches = e.WheelDelta.Y == 0 ? 1 : Math.Abs(e.WheelDelta.Y);
        MapCanvas.ZoomAbout(anchor, Math.Pow(1.1, zoomIn ? notches : -notches));
        e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PeekCard = null;
            _vm.RevealRequested -= OnRevealRequested;
            _vm.FitRequested -= FitAll;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.RevealSeriesRequested -= OnRevealSeries;
            _vm.FitAtlasRequested -= AtlasCanvas.Fit;
            _vm.SaveImageRequested -= OnSaveImageRequested;
        }
        _vm = DataContext as EventMapViewModel;
        if (_vm is not null)
        {
            _vm.RevealRequested += OnRevealRequested;
            _vm.FitRequested += FitAll;
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            _vm.RevealSeriesRequested += OnRevealSeries;
            _vm.FitAtlasRequested += AtlasCanvas.Fit;
            _vm.SaveImageRequested += OnSaveImageRequested;
        }
    }

    private void OnCardPressed(int index, int clicks)
    {
        if (_vm is null) return;
        _vm.ActivateCard(index);
        if (_vm.IsMapMode) OnRevealRequested(index);
        if (clicks >= 2) _vm.OpenReaderCommand.Execute(null);
    }

    private void OnRevealSeries(int seriesId)
    {
        if (_vm is null) return;
        if (_vm.IsMatrixMode) MatrixCanvas.RevealSeries(seriesId);
        else if (_vm.IsListMode)
        {
            var header = _vm.ListItems.OfType<EventListIssueItem>().FirstOrDefault(r => r.Card.SeriesId == seriesId);
            if (header is not null) _vm.Select(header.Card.Index, reveal: false);
        }
        else
        {
            MapCanvas.RevealSeries(seriesId);
        }
    }

    private void OnBandPressed(int index, int clicks)
    {
        if (_vm is null || index < 0 || index >= _vm.Layout.Blocks.Count) return;
        int generation = ++_bandClickGeneration;
        if (clicks >= 2)
        {
            var span = _vm.Layout.BlockColumns(_vm.Layout.Blocks[index]);
            double scale = EventMapZoom.FitBand(span.Last - span.First + 1, MapCanvas.ContentViewport.Width);
            scale = EventMapZoom.ClampScale(scale, _vm.MinimumScale);
            double center = (span.First + span.Last + 1) * EventMapMetrics.ForScale(scale).SlotWidth / 2;
            MapCanvas.GlideToView(scale, new Vector(center - MapCanvas.ContentViewport.Width / 2, MapCanvas.Offset.Y));
            return;
        }
        DispatcherTimer.RunOnce(() => { if (generation == _bandClickGeneration) _vm?.OpenBand(index); }, TimeSpan.FromMilliseconds(350));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EventMapViewModel.Layout)) Dispatcher.UIThread.Post(UpdateMinimumScale, DispatcherPriority.Background);
    }

    private void UpdateMinimumScale()
    {
        if (_vm is null || _vm.Layout.IsEmpty || MapCanvas.Bounds.Width <= 0 || MapCanvas.Bounds.Height <= 0) return;
        _vm.MinimumScale = EventMapZoom.MinimumScaleForFit(EventMapZoom.FitScale(_vm.Layout.ColumnCount, _vm.Layout.TrackCount, MapCanvas.Bounds.Size, MapCanvas.RailWidth, MapCanvas.RulerHeight));
    }

    private void FitAll()
    {
        if (_vm is null) return;
        UpdateMinimumScale();
        double fitScale = EventMapZoom.FitScale(_vm.Layout.ColumnCount, _vm.Layout.TrackCount, MapCanvas.Bounds.Size, MapCanvas.RailWidth, MapCanvas.RulerHeight);
        var metrics = EventMapMetrics.ForScale(fitScale);
        var total = _vm.Layout.TotalSizeFor(metrics);
        var viewport = MapCanvas.ContentViewport;
        var target = new Vector((total.Width - viewport.Width) / 2, (total.Height - viewport.Height) / 2);
        if (_vm.SelectedIndex is int index && _vm.IsInspectorOpen && !_vm.IsInspectorDocked)
        {
            var rect = _vm.Layout.CardRect(index, metrics).Inflate(24);
            double visibleWidth = Math.Max(1, viewport.Width - 364);
            if (rect.Right > target.X + visibleWidth) target = new Vector(rect.Right - visibleWidth, target.Y);
        }
        MapCanvas.GlideToView(fitScale, target);
    }

    private void OnRevealRequested(int index) => Dispatcher.UIThread.Post(() =>
    {
        if (_vm is not null) MapCanvas.Reveal(index, _vm.IsInspectorOpen && !_vm.IsInspectorDocked ? 364 : 0);
    }, DispatcherPriority.Background);

    private void OnMapKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _vm is null || !_vm.HasMap || e.Source is TextBox || IsWithin<Controls.SuggestBox>(e.Source)) return;
        if (_vm.IsMatrixMode && IsWithin(e.Source, MatrixCanvas))
        {
            if (e.Key == Key.Escape) e.Handled = MatrixCanvas.CloseExpansion() || _vm.Escape();
            return;
        }
        if (e.Key != Key.Escape && !IsWithin(e.Source, MapCanvas)) return;
        e.Handled = HandleKey(_vm, e.Key, e.KeyModifiers);
    }

    internal static bool HandleKey(EventMapViewModel vm, Key key, KeyModifiers modifiers)
    {
        switch (key)
        {
            // A move that has nowhere to go is not used, so Left on the first card or Up on the top lane reaches the shell (the nav rail, the toolbar).
            case Key.Right:
                return vm.MoveNext();
            case Key.Left:
                return vm.MovePrevious();
            case Key.Up:
                return vm.MoveUp();
            case Key.Down:
                return vm.MoveDown();
            case Key.Home:
                return vm.MoveFirst();
            case Key.End:
                return vm.MoveLast();
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
