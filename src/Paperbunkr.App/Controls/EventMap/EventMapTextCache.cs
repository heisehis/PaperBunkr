using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Paperbunkr.App.Controls.EventMap;

internal sealed class EventMapTextCache : IDisposable
{
    private const int Capacity = 1200;
    private readonly Dictionary<(string Text, Typeface Typeface, double Size, double Width, Color Color), TextLayout> _layouts = new();

    public TextLayout Get(string text, Typeface typeface, double size, double width, Color color)
    {
        var key = (text, typeface, size, Math.Max(1, Math.Floor(width)), color);
        if (_layouts.TryGetValue(key, out var layout)) return layout;
        if (_layouts.Count >= Capacity) Clear();
        layout = new TextLayout(text, typeface, size, new SolidColorBrush(color), textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: key.Item4, maxHeight: double.PositiveInfinity, maxLines: 1);
        _layouts.Add(key, layout);
        return layout;
    }

    public void Clear()
    {
        foreach (var layout in _layouts.Values) layout.Dispose();
        _layouts.Clear();
    }

    public void Dispose() => Clear();
}
