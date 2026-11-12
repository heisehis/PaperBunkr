using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Views.EntityPages;

public enum EntityTileShape { Portrait, Squircle }

/// <summary>The shared entity tile (entity pages redesign §5.1). <see cref="Command"/> runs with the tile's <c>EntityTileItem</c> as its parameter.</summary>
public partial class EntityTile : UserControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<EntityTile, ICommand?>(nameof(Command));

    /// <summary>What <see cref="Command"/> receives; unset, the tile's own <c>EntityTileItem</c>.</summary>
    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<EntityTile, object?>(nameof(CommandParameter));

    public static readonly StyledProperty<EntityTileShape> ShapeProperty =
        AvaloniaProperty.Register<EntityTile, EntityTileShape>(nameof(Shape));

    public EntityTile()
    {
        InitializeComponent();
        ApplyShape();
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    /// <summary>Squircle for creators (a headshot), portrait for everything else.</summary>
    public EntityTileShape Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShapeProperty) ApplyShape();
        if (change.Property == CommandParameterProperty || change.Property == DataContextProperty) if (Cell is not null) Cell.CommandParameter = CommandParameter ?? DataContext;
    }

    private void ApplyShape() => Art?.Classes.Set("squircle", Shape == EntityTileShape.Squircle);
}
