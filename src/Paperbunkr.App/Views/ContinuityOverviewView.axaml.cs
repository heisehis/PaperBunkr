using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// A continuity's Overview. The code-behind only handles dragging posters in the Custom wall (docs/superpowers/specs/2026-09-28-continuity-
/// screen-redesign-design.md, F2): a press on a poster that moves a few pixels starts a drag, and dropping on another poster puts the
/// dragged series in that poster's place. The pointer handlers tunnel so the poster's own Button can't swallow the press; the reorder
/// itself (and its deferred reload) is <see cref="ContinuityPageViewModel.MoveSeriesTo"/>. Ctrl+←/→ on a focused poster is the keyboard
/// equivalent, bound in the XAML.
/// </summary>
public partial class ContinuityOverviewView : UserControl
{
    private static readonly DataFormat<SeriesCardSample> DragFormat = DataFormat.CreateInProcessFormat<SeriesCardSample>("paperbunkr-continuity-series");
    private const double DragThreshold = 6;

    private Point? _pressPoint;
    private PointerPressedEventArgs? _pressArgs;
    private SeriesCardSample? _pressCard;
    private bool _dragging;

    public ContinuityOverviewView()
    {
        InitializeComponent();
        CustomWall.AddHandler(PointerPressedEvent, OnWallPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        CustomWall.AddHandler(PointerMovedEvent, OnWallPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        CustomWall.AddHandler(PointerReleasedEvent, (_, _) => ResetPress(), RoutingStrategies.Tunnel, handledEventsToo: true);
        CustomWall.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        CustomWall.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private static SeriesCardSample? CardAt(object? source) =>
        source is Visual visual
            ? (visual.FindAncestorOfType<ContentPresenter>(includeSelf: true)?.DataContext as ContinuityPosterItem
               ?? (visual as StyledElement)?.DataContext as ContinuityPosterItem)?.Card
            : null;

    private void OnWallPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(CustomWall).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressCard = CardAt(e.Source);
        _pressPoint = _pressCard is null ? null : e.GetPosition(CustomWall);
        _pressArgs = _pressCard is null ? null : e;
    }

    private async void OnWallPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging || _pressPoint is not Point start || _pressCard is not { } card || _pressArgs is not { } pressed)
        {
            return;
        }

        var delta = e.GetPosition(CustomWall) - start;
        if (System.Math.Abs(delta.X) < DragThreshold && System.Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _dragging = true;
        try
        {
            var data = new DataTransfer();          // disposed by Avalonia when the drag completes
            data.Add(DataTransferItem.Create(DragFormat, card));
            await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move);     // the drag has to start from the press
        }
        finally
        {
            _dragging = false;
            ResetPress();
        }
    }

    private void ResetPress()
    {
        if (!_dragging)
        {
            _pressPoint = null;
            _pressCard = null;
            _pressArgs = null;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Formats.Contains(DragFormat) && CardAt(e.Source) is not null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ContinuityPageViewModel page || e.DataTransfer.TryGetValue(DragFormat) is not { } draggedCard || CardAt(e.Source) is not { } target)
        {
            return;
        }

        var dragged = page.Members.FirstOrDefault(m => m.SeriesId == draggedCard.SeriesId);
        if (dragged is not null && dragged != target)
        {
            page.MoveSeriesTo(dragged, page.Members.IndexOf(target));
        }

        e.Handled = true;
    }
}
