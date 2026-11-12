using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Views.EntityPages;

public partial class EntityPosterGrid : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsProperty =
        AvaloniaProperty.Register<EntityPosterGrid, IEnumerable?>(nameof(Items));

    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<EntityPosterGrid, string?>(nameof(Label));

    public EntityPosterGrid() => InitializeComponent();

    /// <summary><c>EntityPosterItem</c>s.</summary>
    public IEnumerable? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>The grid's accessible name ("Appearances", "Series").</summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }
}
