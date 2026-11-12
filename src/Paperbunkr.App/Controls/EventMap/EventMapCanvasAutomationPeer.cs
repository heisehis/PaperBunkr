using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Paperbunkr.App.Services.EventMap;

namespace Paperbunkr.App.Controls.EventMap;

internal sealed class EventMapCanvasAutomationPeer : ControlAutomationPeer, ISelectionProvider
{
    private readonly EventMapCanvas _canvas;
    public EventMapCanvasAutomationPeer(EventMapCanvas canvas) : base(canvas)
    {
        _canvas = canvas;
        canvas.ViewportChanged += InvalidateChildren;
    }

    public bool CanSelectMultiple => false;
    public bool IsSelectionRequired => false;
    public IReadOnlyList<AutomationPeer> GetSelection() => GetChildren().OfType<EventMapCardAutomationPeer>().Where(p => p.IsSelected).Cast<AutomationPeer>().ToArray();
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    internal void SelectionChanged(int? oldIndex, int? newIndex)
    {
        foreach (var child in GetChildren().OfType<EventMapCardAutomationPeer>())
        {
            if (child.Index == oldIndex) child.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, true, false);
            if (child.Index == newIndex) child.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, false, true);
        }
    }

    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
    {
        if (_canvas.Layout is not { } layout || _canvas.Cards is null || layout.IsEmpty) return Array.Empty<AutomationPeer>();
        var viewport = new Rect(new Point(_canvas.Offset.X, _canvas.Offset.Y), _canvas.ContentViewport);
        return layout.CellsIn(layout.VisibleRange(viewport, _canvas.Metrics))
            .Where(i => i < _canvas.Cards.Count && layout.CardRect(i, _canvas.Metrics).Intersects(viewport))
            .Select(i => (AutomationPeer)new EventMapCardAutomationPeer(_canvas, this, i)).ToArray();
    }
}

internal sealed class EventMapCardAutomationPeer : AutomationPeer, ISelectionItemProvider
{
    private readonly EventMapCanvas _canvas;
    private AutomationPeer? _parent;
    private readonly int _index;

    public EventMapCardAutomationPeer(EventMapCanvas canvas, AutomationPeer parent, int index)
    { _canvas = canvas; _parent = parent; _index = index; }

    public bool IsSelected => _canvas.SelectedIndex == _index;
    internal int Index => _index;
    public ISelectionProvider? SelectionContainer => _parent as ISelectionProvider;
    public void Select() => _canvas.SelectFromAutomation(_index);
    public void AddToSelection() => Select();
    public void RemoveFromSelection() { if (IsSelected) _canvas.ClearSelectionFromAutomation(); }
    protected override void BringIntoViewCore() => _canvas.Reveal(_index);
    protected override string? GetAcceleratorKeyCore() => null;
    protected override string? GetAccessKeyCore() => null;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
    protected override string? GetAutomationIdCore() => $"MapCard{_index}";
    protected override Rect GetBoundingRectangleCore()
    {
        if (_canvas.Layout is not { } layout || _parent is null) return default;
        var rect = layout.CardRect(_index, _canvas.Metrics);
        if (IsSelected && EventMapCardLooks.For(_canvas.Metrics.CardWidth) == EventMapCardLook.Dots) rect = rect.Inflate(2);
        var origin = _parent.GetBoundingRectangle().Position;
        return new Rect(origin.X + _canvas.RailWidth + rect.X - _canvas.Offset.X,
            origin.Y + _canvas.RulerHeight + rect.Y - _canvas.Offset.Y, rect.Width, rect.Height);
    }
    protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() => Array.Empty<AutomationPeer>();
    protected override string GetClassNameCore() => "EventMapCard";
    protected override AutomationPeer? GetLabeledByCore() => null;
    protected override string? GetNameCore() => _canvas.Cards is { } cards && _index < cards.Count
        ? cards[_index].AutomationName + (_canvas.NextUpIndex == _index ? ", next to read" : string.Empty)
        : null;
    protected override AutomationPeer? GetParentCore() => _parent;
    protected override bool HasKeyboardFocusCore() => _canvas.IsFocused && IsSelected;
    protected override bool IsContentElementCore() => true;
    protected override bool IsControlElementCore() => true;
    protected override bool IsEnabledCore() => _canvas.IsEffectivelyEnabled;
    protected override bool IsKeyboardFocusableCore() => true;
    protected override void SetFocusCore() { _canvas.Focus(); Select(); }
    protected override bool ShowContextMenuCore() => false;
    protected override bool TrySetParent(AutomationPeer? parent) { _parent = parent; return true; }
}
