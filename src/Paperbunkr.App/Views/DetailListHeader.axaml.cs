using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.Models;

namespace Paperbunkr.App.Views;

/// <summary>
/// The draggable header row over a comic List or manga Chapters list (docs/superpowers/specs/2026-10-19-detail-layouts-v2-design.md,
/// Q7). Resizing follows the grip live on the header and commits on release; a move commits on drop. Both commits are posted one
/// dispatcher tick later: the owner re-renders, which rebuilds these cells, and removing a control while its own pointer event is still
/// routing corrupts Avalonia's child list (CLAUDE.md, "don't remove/detach a control from inside a routed event it's still raising").
/// </summary>
public partial class DetailListHeader : UserControl
{
    public static readonly StyledProperty<IEnumerable<DetailListHeaderCell>?> CellsProperty =
        AvaloniaProperty.Register<DetailListHeader, IEnumerable<DetailListHeaderCell>?>(nameof(Cells));

    public static readonly StyledProperty<Thickness> LeadingInsetProperty =
        AvaloniaProperty.Register<DetailListHeader, Thickness>(nameof(LeadingInset));

    private const double DragThreshold = 6;
    private string? _dragField;
    private Point _dragStart;
    private bool _dragging;
    private Border? _dropTarget;

    public DetailListHeader() => InitializeComponent();

    public IEnumerable<DetailListHeaderCell>? Cells
    {
        get => GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    /// <summary>Space before the first column, so the headers sit over the row cells (a comic row's cover, a manga row's padding).</summary>
    public Thickness LeadingInset
    {
        get => GetValue(LeadingInsetProperty);
        set => SetValue(LeadingInsetProperty, value);
    }

    /// <summary>A column's new width (field, width), raised once per resize.</summary>
    public event Action<string, double>? ColumnResized;

    /// <summary>A column dropped onto another (field, target field).</summary>
    public event Action<string, string>? ColumnMoved;

    private void OnGripDragDelta(object? sender, VectorEventArgs e)
    {
        if (sender is Control { Tag: DetailListHeaderCell cell })
        {
            cell.Width = DetailListLayoutStateJson.MinColumnWidth > cell.Width + e.Vector.X
                ? DetailListLayoutStateJson.MinColumnWidth
                : Math.Min(DetailListLayoutStateJson.MaxColumnWidth, cell.Width + e.Vector.X);
        }
    }

    private void OnGripDragCompleted(object? sender, VectorEventArgs e)
    {
        if (sender is Control { Tag: DetailListHeaderCell cell })
        {
            string field = cell.Field;
            double width = Math.Round(cell.Width);
            Dispatcher.UIThread.Post(() => ColumnResized?.Invoke(field, width));
        }
    }

    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: string field } border && e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
        {
            _dragField = field;
            _dragStart = e.GetPosition(this);
            _dragging = false;
            e.Pointer.Capture(border);
        }
    }

    private void OnHeaderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragField is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (!_dragging && Math.Abs(position.X - _dragStart.X) < DragThreshold)
        {
            return;
        }

        _dragging = true;
        SetDropTarget(CellAt(position));
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        string? field = _dragField;
        var target = _dragging ? CellAt(e.GetPosition(this)) : null;
        _dragField = null;
        _dragging = false;
        SetDropTarget(null);
        e.Pointer.Capture(null);

        if (field is not null && target is { Tag: string targetField } && targetField != field)
        {
            Dispatcher.UIThread.Post(() => ColumnMoved?.Invoke(field, targetField));
        }
    }

    private void SetDropTarget(Border? border)
    {
        if (ReferenceEquals(border, _dropTarget))
        {
            return;
        }

        _dropTarget?.Classes.Remove("dropTarget");
        _dropTarget = border is { Tag: string field } && field != _dragField ? border : null;
        _dropTarget?.Classes.Add("dropTarget");
    }

    /// <summary>The header cell whose horizontal span (grip included) holds <paramref name="position"/> - the row is one line, so only X counts.</summary>
    private Border? CellAt(Point position) =>
        HeaderItems.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("detailHeaderCell"))
            .FirstOrDefault(b => b.TranslatePoint(new Point(0, 0), this) is { } origin
                                 && position.X >= origin.X && position.X < origin.X + b.Bounds.Width + 6);
}
