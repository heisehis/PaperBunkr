using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>A single focusable, viewport-culled series-by-phase matrix.</summary>
public sealed class EventMatrixCanvas : Control
{
    public static readonly StyledProperty<EventMatrix?> MatrixProperty = AvaloniaProperty.Register<EventMatrixCanvas, EventMatrix?>(nameof(Matrix));
    public static readonly StyledProperty<IReadOnlyList<EventMapCardViewModel>?> CardsProperty = AvaloniaProperty.Register<EventMatrixCanvas, IReadOnlyList<EventMapCardViewModel>?>(nameof(Cards));
    public static readonly StyledProperty<bool> CoversProperty = AvaloniaProperty.Register<EventMatrixCanvas, bool>(nameof(Covers));
    public static readonly StyledProperty<int?> SelectedIndexProperty = AvaloniaProperty.Register<EventMatrixCanvas, int?>(nameof(SelectedIndex));
    private const double Rail = 200, Header = 40, Column = 112, Expansion = 96;
    private readonly EventMapTextCache _text = new();
    private EventMapCoverSource? _covers;
    private Vector _offset;
    private int _row, _column;
    private int? _expandedRow, _expandedColumn;
    private int? _hoverRow, _hoverColumn;
    private double _expansionOffset;
    public event Action<int, int>? CardPressed;
    public event Action<int>? OpenOnMap;
    internal event Action? ViewportChanged;

    static EventMatrixCanvas() => AffectsRender<EventMatrixCanvas>(MatrixProperty, CardsProperty, CoversProperty, SelectedIndexProperty);
    public EventMatrixCanvas()
    {
        Focusable = true; ClipToBounds = true;
        _covers = new EventMapCoverSource(InvalidateVisual);
        ActualThemeVariantChanged += (_, _) => { _text.Clear(); InvalidateVisual(); };
        ResourcesChanged += (_, _) => { _text.Clear(); InvalidateVisual(); };
    }
    public EventMatrix? Matrix { get => GetValue(MatrixProperty); set => SetValue(MatrixProperty, value); }
    public IReadOnlyList<EventMapCardViewModel>? Cards { get => GetValue(CardsProperty); set => SetValue(CardsProperty, value); }
    public bool Covers { get => GetValue(CoversProperty); set => SetValue(CoversProperty, value); }
    public int? SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    internal double RowHeight => Covers ? 64 : 28;
    internal bool HasExpansion => _expandedRow is not null;
    internal int? ExpandedRow => _expandedRow;
    internal int? ExpandedColumn => _expandedColumn;
    internal int FocusedRow => _row;
    internal int FocusedColumn => _column;
    internal Vector Offset => _offset;

    /// <summary>Decode covers inline: an off-screen export (M6b §4) needs them all in its single render.</summary>
    internal void UseSynchronousCovers()
    {
        _covers?.Dispose();
        _covers = new EventMapCoverSource(InvalidateVisual, synchronous: true);
    }

    /// <summary>The full content size (no scrolling), for an export.</summary>
    internal Size ContentSize => Matrix is { } m ? new Size(Rail + m.Columns.Count * Column, Header + m.Rows.Count * RowHeight) : default;

    /// <summary>The next issue to read: the first unread card in reading order (the map's own rule lives in EventMapReadPath).</summary>
    internal int? NextUpIndex => Cards is { } cards && cards.FirstOrDefault(c => !c.IsRead) is { } card ? card.Index : null;
    protected override AutomationPeer OnCreateAutomationPeer() => new EventMatrixAutomationPeer(this);
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); _covers ??= new EventMapCoverSource(InvalidateVisual); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { base.OnDetachedFromVisualTree(e); _covers?.Dispose(); _covers = null; _text.Clear(); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MatrixProperty) { _row = _column = 0; _expandedRow = _expandedColumn = null; Clamp(); ViewportChanged?.Invoke(); }
        if (change.Property == CoversProperty || change.Property == BoundsProperty) Clamp();
    }
    private double RowTop(int row) => Header + row * RowHeight + (_expandedRow is int e && row > e ? Expansion : 0) - _offset.Y;
    internal double RowScreenTop(int row) => RowTop(row);
    private void Clamp()
    {
        int rows = Matrix?.Rows.Count ?? 0, cols = Matrix?.Columns.Count ?? 0;
        _offset = new Vector(Math.Clamp(_offset.X, 0, Math.Max(0, cols * Column - Math.Max(0, Bounds.Width - Rail))),
            Math.Clamp(_offset.Y, 0, Math.Max(0, rows * RowHeight + (HasExpansion ? Expansion : 0) - Math.Max(0, Bounds.Height - Header))));
    }
    public void RevealSeries(int seriesId)
    {
        if (Matrix is not { } matrix) return;
        int row = matrix.Rows.ToList().FindIndex(r => r.SeriesId == seriesId);
        if (row < 0) return;
        _row = row; EnsureVisible(); InvalidateVisual();
    }
    private void EnsureVisible()
    {
        double x = _column * Column, y = _row * RowHeight + (_expandedRow is int e && _row > e ? Expansion : 0);
        double vx = Math.Max(0, Bounds.Width - Rail), vy = Math.Max(0, Bounds.Height - Header);
        _offset = new Vector(x < _offset.X ? x : x + Column > _offset.X + vx ? x + Column - vx : _offset.X,
            y < _offset.Y ? y : y + RowHeight > _offset.Y + vy ? y + RowHeight - vy : _offset.Y);
        Clamp(); ViewportChanged?.Invoke();
    }
    public bool CloseExpansion()
    {
        if (!HasExpansion) return false;
        _expandedRow = _expandedColumn = null; Clamp(); InvalidateVisual(); ViewportChanged?.Invoke(); return true;
    }
    private void Expand(int row, int col)
    {
        if (Matrix is null || Matrix.Rows[row].Cells[col].Count == 0) return;
        if (_expandedRow == row && _expandedColumn == col) { CloseExpansion(); return; }
        _expandedRow = row; _expandedColumn = col; _expansionOffset = 0; EnsureVisible(); InvalidateVisual(); ViewportChanged?.Invoke();
    }
    internal void ActivateCell(int row, int col, bool openMap)
    {
        if (Matrix is not { } matrix || row < 0 || row >= matrix.Rows.Count || col < 0 || col >= matrix.Columns.Count) return;
        _row = row; _column = col; EnsureVisible();
        var indices = matrix.Rows[row].Cells[col].Indices;
        if (openMap && indices.Count > 0) OpenOnMap?.Invoke(indices[0]); else Expand(row, col);
        InvalidateVisual();
    }
    internal void ActivateCard(int index) => CardPressed?.Invoke(index, 1);
    internal void FocusCell(int row, int column)
    {
        if (Matrix is not { } matrix || row < 0 || row >= matrix.Rows.Count || column < 0 || column >= matrix.Columns.Count) return;
        _row = row; _column = column; Focus(); EnsureVisible(); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Matrix is not { } matrix) return;
        var p = e.GetPosition(this); Focus();
        if (_expandedRow is int er && _expandedColumn is int ec && p.Y >= RowTop(er) + RowHeight && p.Y < RowTop(er) + RowHeight + Expansion && p.X >= Rail)
        {
            int n = (int)((p.X - Rail + _expansionOffset) / 130);
            var issues = matrix.Rows[er].Cells[ec].Indices;
            if (n >= 0 && n < issues.Count) CardPressed?.Invoke(issues[n], e.ClickCount);
            e.Handled = true; return;
        }
        if (p.X < Rail || p.Y < Header) return;
        int col = (int)((p.X - Rail + _offset.X) / Column);
        for (int row = 0; row < matrix.Rows.Count; row++)
            if (p.Y >= RowTop(row) && p.Y < RowTop(row) + RowHeight && col >= 0 && col < matrix.Columns.Count)
            { ActivateCell(row, col, e.ClickCount >= 2); e.Handled = true; return; }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Matrix is null) return;
        var point = e.GetPosition(this);
        if (_expandedRow is int row && point.Y >= RowTop(row) + RowHeight && point.Y < RowTop(row) + RowHeight + Expansion && _expandedColumn is int col)
            _expansionOffset = Math.Clamp(_expansionOffset - e.Delta.Y * 80, 0, Math.Max(0, Matrix.Rows[row].Cells[col].Count * 130 - (Bounds.Width - Rail)));
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y))
            _offset = new Vector(_offset.X - (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 70, _offset.Y);
        else _offset = new Vector(_offset.X, _offset.Y - e.Delta.Y * 70);
        Clamp(); InvalidateVisual(); ViewportChanged?.Invoke(); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        int? row = null, column = null;
        if (Matrix is { } matrix && p.X >= Rail)
        {
            int c = (int)((p.X - Rail + _offset.X) / Column);
            if (c >= 0 && c < matrix.Columns.Count)
            {
                column = c;
                if (p.Y < Header) ToolTip.SetTip(this, matrix.Columns[c].Tooltip);
                else
                {
                    ToolTip.SetTip(this, null);
                    for (int r = 0; r < matrix.Rows.Count; r++)
                        if (p.Y >= RowTop(r) && p.Y < RowTop(r) + RowHeight) { row = r; break; }
                }
            }
        }
        else ToolTip.SetTip(this, null);
        if (_hoverRow != row || _hoverColumn != column) { _hoverRow = row; _hoverColumn = column; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e)
    { base.OnPointerExited(e); _hoverRow = _hoverColumn = null; ToolTip.SetTip(this, null); InvalidateVisual(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Matrix is not { Rows.Count: > 0, Columns.Count: > 0 } matrix) return;
        switch (e.Key)
        {
            case Key.Escape when CloseExpansion(): e.Handled = true; return;
            case Key.Left: _column = Math.Max(0, _column - 1); break;
            case Key.Right: _column = Math.Min(matrix.Columns.Count - 1, _column + 1); break;
            case Key.Up: _row = Math.Max(0, _row - 1); break;
            case Key.Down: _row = Math.Min(matrix.Rows.Count - 1, _row + 1); break;
            case Key.Home: _column = 0; if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) _row = 0; break;
            case Key.End: _column = matrix.Columns.Count - 1; if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) _row = matrix.Rows.Count - 1; break;
            case Key.Enter: ActivateCell(_row, _column, e.KeyModifiers.HasFlag(KeyModifiers.Control)); e.Handled = true; return;
            default: return;
        }
        EnsureVisible(); InvalidateVisual(); e.Handled = true;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Matrix is not { } matrix) return;
        IBrush Brush(string key) => new SolidColorBrush(EventMapPalette.ResolveColor(this, key));
        var bg = Brush("PbBgColor"); var chrome = Brush("PbChromeColor"); var text = EventMapPalette.ResolveColor(this, "PbTextColor");
        var muted = EventMapPalette.ResolveColor(this, "PbTextMutedColor"); var border = Brush("PbBorderColor"); var accent = Brush("PbAccentColor"); var success = Brush("PbSuccessColor");
        var typeface = Typeface.Default;
        void Text(string value, Rect rect, double size, Color color) { using (context.PushClip(rect)) _text.Get(value, typeface, size, rect.Width, color).Draw(context, rect.Position); }
        context.FillRectangle(bg, new Rect(Bounds.Size));
        var wanted = new HashSet<string>();
        if (Covers || HasExpansion)
            for (int r = 0; r < matrix.Rows.Count; r++)
            {
                double y = RowTop(r);
                if (y + RowHeight + (r == _expandedRow ? Expansion : 0) < Header || y > Bounds.Height) continue;
                for (int c = 0; c < matrix.Columns.Count; c++)
                {
                    double x = Rail + c * Column - _offset.X;
                    if (!(r == _expandedRow && c == _expandedColumn) && (x + Column < Rail || x > Bounds.Width || !Covers)) continue;
                    var indices = matrix.Rows[r].Cells[c].Indices;
                    var visible = r == _expandedRow && c == _expandedColumn ? indices : indices.Take(3);
                    foreach (int index in visible)
                        if (Cards is { } cards && index < cards.Count && cards[index].CoverKey is { } key) wanted.Add(key);
                }
            }
        _covers?.SetWanted(wanted);
        int? nextUp = NextUpIndex;
        using (context.PushClip(new Rect(0, Header, Bounds.Width, Math.Max(0, Bounds.Height - Header))))
        for (int r = 0; r < matrix.Rows.Count; r++)
        {
            double y = RowTop(r); if (y + RowHeight + (r == _expandedRow ? Expansion : 0) < Header || y > Bounds.Height) continue;
            var row = matrix.Rows[r];
            context.FillRectangle(r % 2 == 0 ? bg : chrome, new Rect(0, y, Bounds.Width, RowHeight));
            using (context.PushClip(new Rect(Rail, Header, Math.Max(0, Bounds.Width - Rail), Math.Max(0, Bounds.Height - Header))))
            for (int c = 0; c < matrix.Columns.Count; c++)
            {
                double x = Rail + c * Column - _offset.X; if (x + Column < Rail || x > Bounds.Width) continue;
                var cell = row.Cells[c]; var rect = new Rect(x, y, Column, RowHeight);
                if (_hoverRow == r && _hoverColumn == c) context.FillRectangle(Brush("PbSurface2Color"), rect);
                context.DrawRectangle(null, new Pen(border, 0.5), rect);
                if (cell.Missing > 0)
                {
                    // Issues the provider lists that the library doesn't own (M4): a dashed danger ring.
                    var danger = Brush("PbDangerColor");
                    context.DrawEllipse(null, new Pen(danger, 1.5, new DashStyle(new double[] { 2, 2 }, 0)), new Point(x + Column - 14, y + 14), 7.5, 7.5);
                    if (cell.Count == 0)
                        Text($"{cell.Missing} missing", new Rect(x + 8, y + 6, Column - 32, 16), 11, EventMapPalette.ResolveColor(this, "PbDangerColor"));
                }
                if (cell.Count > 0)
                {
                    double labelX = Covers ? x + 6 : x + 8;
                    double labelY = Covers ? y + RowHeight - 17 : y + 6;
                    Text(matrix.Columns[c].IsTotal ? $"{cell.Count} · {cell.Read} read" : cell.Count.ToString(), new Rect(labelX, labelY, Column - 32, 16), 11, text);
                    var centre = new Point(x + Column - 14, y + 14);
                    context.DrawEllipse(null, new Pen(border, 2), centre, 6, 6);
                    if (cell.Read == cell.Count) context.DrawEllipse(null, new Pen(success, 2), centre, 6, 6);
                    else if (cell.Read > 0) { var geo = new StreamGeometry(); using (var path = geo.Open()) { path.BeginFigure(new Point(centre.X, centre.Y - 6), false); double angle = Math.PI * 2 * cell.Read / cell.Count - Math.PI / 2; path.ArcTo(new Point(centre.X + Math.Cos(angle) * 6, centre.Y + Math.Sin(angle) * 6), new Size(6, 6), 0, cell.Read * 2 > cell.Count, SweepDirection.Clockwise); path.EndFigure(false); } context.DrawGeometry(null, new Pen(success, 2), geo); }
                    if (Covers && Cards is { } cards) for (int n = 0; n < Math.Min(3, cell.Count); n++)
                    { int index = cell.Indices[n]; if (index < cards.Count && _covers?.Get(cards[index].CoverKey, 48) is { } image) context.DrawImage(image, new Rect(image.Size), new Rect(x + 30 + n * 20, y + 5, 36, 54)); }
                }
                if ((SelectedIndex is int selected && cell.Indices.Contains(selected)) || (r == _row && c == _column && IsFocused))
                    context.DrawRectangle(null, new Pen(accent, 2), rect.Deflate(1));
                // "You are here" (M6b §1): the cell holding the next issue to read gets the map's ring, drawn still here.
                if (!matrix.Columns[c].IsTotal && nextUp is int next && cell.Indices.Contains(next))
                    using (context.PushOpacity(0.8)) context.DrawEllipse(null, new Pen(accent, 2.5), new Point(x + Column - 14, y + 14), 9.5, 9.5);
            }
            context.FillRectangle(r % 2 == 0 ? bg : chrome, new Rect(0, y, Rail, RowHeight));
            context.FillRectangle(Brush(EventMapPalette.KeyPrefix(row.ColorIndex) + "Color"), new Rect(0, y + 2, 3, RowHeight - 4));
            Text(row.Name, new Rect(9, y + 6, Rail - 55, 16), 11, text);
            Text(row.Cells[^1].Count.ToString(), new Rect(Rail - 40, y + 6, 35, 16), 10, muted);
            if (_expandedRow == r && _expandedColumn is int ec && Cards is { } expansionCards)
            {
                double ey = y + RowHeight; context.FillRectangle(chrome, new Rect(0, ey, Bounds.Width, Expansion));
                using (context.PushClip(new Rect(Rail, ey, Math.Max(0, Bounds.Width - Rail), Expansion)))
                for (int n = 0; n < row.Cells[ec].Count; n++)
                {
                    int index = row.Cells[ec].Indices[n]; if (index >= expansionCards.Count) continue;
                    double x = Rail + n * 130 - _expansionOffset; if (x + 124 < Rail || x > Bounds.Width) continue;
                    var card = expansionCards[index]; var rect = new Rect(x + 3, ey + 5, 124, 84);
                    context.DrawRectangle(bg, new Pen(index == SelectedIndex ? accent : border, 1), rect, 3, 3);
                    if (_covers?.Get(card.CoverKey, 48) is { } cover) context.DrawImage(cover, new Rect(cover.Size), new Rect(rect.X + 4, rect.Y + 4, 36, 54));
                    Text(card.Badge, new Rect(rect.X + 43, rect.Y + 6, 77, 28), 11, text);
                    Text(card.ReadStateLabel, new Rect(rect.X + 43, rect.Y + 28, 77, 16), 10, muted);
                }
            }
        }
        context.FillRectangle(chrome, new Rect(0, 0, Bounds.Width, Header));
        using (context.PushClip(new Rect(Rail, 0, Math.Max(0, Bounds.Width - Rail), Header)))
        for (int c = 0; c < matrix.Columns.Count; c++)
        { double x = Rail + c * Column - _offset.X; if (x + Column >= Rail && x <= Bounds.Width) Text(matrix.Columns[c].Label, new Rect(x + 8, 13, Column - 16, 18), 11, text); }
        context.FillRectangle(chrome, new Rect(0, 0, Rail, Header));
        Text("Series", new Rect(10, 13, Rail - 20, 18), 11, muted);
    }
}
