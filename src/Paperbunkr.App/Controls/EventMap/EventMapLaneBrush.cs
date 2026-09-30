using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Controls.EventMap;

/// <summary>
/// <c>em:EventMapLaneBrush.ColorIndex="{Binding ColorIndex}"</c> on a <see cref="Border"/> binds its background to the
/// lane's skin brush (<see cref="EventMapPalette"/>) as a live resource observable - the equivalent of a
/// <c>DynamicResource</c> whose key comes from data, which XAML can't express directly. Keeps lane colours following
/// the runtime skin system instead of a converter's one-time lookup.
/// </summary>
public static class EventMapLaneBrush
{
    public static readonly AttachedProperty<int?> ColorIndexProperty =
        AvaloniaProperty.RegisterAttached<Border, int?>("ColorIndex", typeof(EventMapLaneBrush));

    private static readonly ConditionalWeakTable<Border, IDisposable> Bindings = new();

    static EventMapLaneBrush()
    {
        ColorIndexProperty.Changed.AddClassHandler<Border>(OnColorIndexChanged);
    }

    public static int? GetColorIndex(Border element) => element.GetValue(ColorIndexProperty);

    public static void SetColorIndex(Border element, int? value) => element.SetValue(ColorIndexProperty, value);

    private static void OnColorIndexChanged(Border border, AvaloniaPropertyChangedEventArgs e)
    {
        if (Bindings.TryGetValue(border, out var previous))
        {
            previous.Dispose();
            Bindings.Remove(border);
        }

        if (e.NewValue is int index)
        {
            Bindings.Add(border, border.Bind(Border.BackgroundProperty, border.GetResourceObservable(EventMapPalette.BrushKey(index))));
        }
    }
}
