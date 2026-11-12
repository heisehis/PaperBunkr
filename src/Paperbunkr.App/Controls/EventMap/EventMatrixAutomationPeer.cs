using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace Paperbunkr.App.Controls.EventMap;

internal sealed class EventMatrixAutomationPeer : ControlAutomationPeer
{
    private readonly EventMatrixCanvas _canvas;
    public EventMatrixAutomationPeer(EventMatrixCanvas canvas) : base(canvas) { _canvas = canvas; canvas.ViewportChanged += InvalidateChildren; }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Table;
    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
    {
        if (_canvas.Matrix is not { } matrix) return Array.Empty<AutomationPeer>();
        var result = new List<AutomationPeer>();
        for (int r = 0; r < matrix.Rows.Count; r++)
        for (int c = 0; c < matrix.Columns.Count; c++)
        {
            double x = 200 + c * 112 - _canvas.Offset.X;
            double y = _canvas.RowScreenTop(r);
            if (x + 112 > 200 && x < _canvas.Bounds.Width && y + _canvas.RowHeight > 40 && y < _canvas.Bounds.Height)
                result.Add(new EventMatrixCellPeer(_canvas, this, r, c));
        }
        return result;
    }
}

internal sealed class EventMatrixCellPeer : AutomationPeer, IInvokeProvider
{
    private readonly EventMatrixCanvas _canvas;
    private AutomationPeer? _parent;
    private readonly int _row, _column;
    public EventMatrixCellPeer(EventMatrixCanvas canvas, AutomationPeer parent, int row, int column)
    { _canvas = canvas; _parent = parent; _row = row; _column = column; }
    public void Invoke() => _canvas.ActivateCell(_row, _column, false);
    protected override string? GetNameCore()
    {
        if (_canvas.Matrix is not { } m || _row >= m.Rows.Count || _column >= m.Columns.Count) return null;
        var cell = m.Rows[_row].Cells[_column];
        return $"{m.Rows[_row].Name}, {m.Columns[_column].Label}: {cell.Count} issues, {cell.Read} read" + (cell.Missing > 0 ? $", {cell.Missing} not owned" : "");
    }
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;
    protected override string? GetAutomationIdCore() => $"MatrixCell{_row}_{_column}";
    protected override string GetClassNameCore() => "EventMatrixCell";
    protected override Rect GetBoundingRectangleCore()
    {
        var origin = _parent?.GetBoundingRectangle().Position ?? default;
        return new Rect(origin.X + 200 + _column * 112 - _canvas.Offset.X,
            origin.Y + _canvas.RowScreenTop(_row), 112, _canvas.RowHeight);
    }
    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore()
    {
        if (_canvas.ExpandedRow != _row || _canvas.ExpandedColumn != _column || _canvas.Matrix is not { } matrix)
            return Array.Empty<AutomationPeer>();
        return matrix.Rows[_row].Cells[_column].Indices.Select(i => (AutomationPeer)new EventMatrixCardPeer(_canvas, this, i)).ToArray();
    }
    protected override AutomationPeer? GetParentCore() => _parent;
    protected override bool TrySetParent(AutomationPeer? parent) { _parent = parent; return true; }
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
    protected override bool IsEnabledCore() => _canvas.IsEffectivelyEnabled;
    protected override bool IsKeyboardFocusableCore() => true;
    protected override bool HasKeyboardFocusCore() => _canvas.IsFocused && _canvas.FocusedRow == _row && _canvas.FocusedColumn == _column;
    protected override void SetFocusCore() => _canvas.FocusCell(_row, _column);
    protected override void BringIntoViewCore() => _canvas.RevealSeries(_canvas.Matrix!.Rows[_row].SeriesId);
    protected override string? GetAccessKeyCore() => null;
    protected override string? GetAcceleratorKeyCore() => null;
    protected override AutomationPeer? GetLabeledByCore() => null;
    protected override bool ShowContextMenuCore() => false;
}

internal sealed class EventMatrixCardPeer : AutomationPeer, IInvokeProvider
{
    private readonly EventMatrixCanvas _canvas;
    private AutomationPeer? _parent;
    private readonly int _index;
    public EventMatrixCardPeer(EventMatrixCanvas canvas, AutomationPeer parent, int index) { _canvas = canvas; _parent = parent; _index = index; }
    public void Invoke() => _canvas.ActivateCard(_index);
    protected override string? GetNameCore() => _canvas.Cards is { } cards && _index < cards.Count ? cards[_index].AutomationName : null;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
    protected override string? GetAutomationIdCore() => $"MatrixCard{_index}";
    protected override string GetClassNameCore() => "EventMatrixCard";
    protected override Rect GetBoundingRectangleCore() => _parent?.GetBoundingRectangle() ?? default;
    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => Array.Empty<AutomationPeer>();
    protected override AutomationPeer? GetParentCore() => _parent;
    protected override bool TrySetParent(AutomationPeer? parent) { _parent = parent; return true; }
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
    protected override bool IsEnabledCore() => _canvas.IsEffectivelyEnabled;
    protected override bool IsKeyboardFocusableCore() => true;
    protected override bool HasKeyboardFocusCore() => false;
    protected override void SetFocusCore() { _canvas.Focus(); Invoke(); }
    protected override void BringIntoViewCore() { }
    protected override string? GetAccessKeyCore() => null;
    protected override string? GetAcceleratorKeyCore() => null;
    protected override AutomationPeer? GetLabeledByCore() => null;
    protected override bool ShowContextMenuCore() => false;
}
