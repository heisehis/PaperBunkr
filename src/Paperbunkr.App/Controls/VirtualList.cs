using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A list that only builds the rows in view (docs/superpowers/specs/2026-09-26-library-health-subtabs-design.md): a
/// <see cref="ScrollViewer"/> capped at <see cref="MaxListHeight"/> around an <see cref="ItemsControl"/> whose panel is a
/// <see cref="VirtualizingStackPanel"/>. A plain <c>ItemsControl</c> inside the page's own ScrollViewer is measured with
/// infinite height, so it builds every row and never virtualizes (about 13 ms a row for a duplicate-group card in a Debug
/// build: 200 rows 2.7 s, 1,000 rows 14.9 s, against 4 built rows for a virtualized 480 px list). It is the only
/// virtualizing option left: <c>ItemsRepeater</c> is unsupported as of Avalonia 12.
/// <para>
/// It uses <c>MaxHeight</c>, not <c>Height</c>, so a short list is exactly as tall as its rows and only a long one scrolls
/// inside itself; scroll chaining stays on, so at either end of a long list the wheel carries on into the page. A hidden
/// list (collapsed section, inactive tab) is never measured, so it builds nothing.
/// </para>
/// </summary>
public class VirtualList : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<VirtualList, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<VirtualList, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<double> MaxListHeightProperty =
        AvaloniaProperty.Register<VirtualList, double>(nameof(MaxListHeight), DefaultMaxListHeight);

    /// <summary>Tall enough for three or four typical rows before it starts scrolling on its own.</summary>
    public const double DefaultMaxListHeight = 420;

    private readonly ScrollViewer _scroller;
    private readonly ItemsControl _items;

    static VirtualList()
    {
        ItemsSourceProperty.Changed.AddClassHandler<VirtualList>((list, e) => list._items.ItemsSource = e.NewValue as IEnumerable);
        ItemTemplateProperty.Changed.AddClassHandler<VirtualList>((list, e) => list._items.ItemTemplate = e.NewValue as IDataTemplate);
        MaxListHeightProperty.Changed.AddClassHandler<VirtualList>((list, e) => list._scroller.MaxHeight = (double)e.NewValue!);
    }

    public VirtualList()
    {
        _items = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
        };
        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            MaxHeight = DefaultMaxListHeight,
            Content = _items,
        };
        Content = _scroller;
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public double MaxListHeight
    {
        get => GetValue(MaxListHeightProperty);
        set => SetValue(MaxListHeightProperty, value);
    }
}
