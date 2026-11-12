using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Views.Home;

/// <summary>
/// The Continue hero of the Home Collections shelf (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md). DataContext is a
/// <c>HomeCollectionCard</c>; the two commands come from the Home view model through the section template, like <c>PosterTile.Command</c>.
/// </summary>
public partial class CollectionHeroCard : UserControl
{
    public static readonly StyledProperty<ICommand?> OpenCommandProperty =
        AvaloniaProperty.Register<CollectionHeroCard, ICommand?>(nameof(OpenCommand));

    public static readonly StyledProperty<ICommand?> ContinueCommandProperty =
        AvaloniaProperty.Register<CollectionHeroCard, ICommand?>(nameof(ContinueCommand));

    public CollectionHeroCard()
    {
        InitializeComponent();
    }

    /// <summary>Opens the collection in the Library (parameter: the card).</summary>
    public ICommand? OpenCommand
    {
        get => GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    /// <summary>Opens the next unread issue in the reader (parameter: the card).</summary>
    public ICommand? ContinueCommand
    {
        get => GetValue(ContinueCommandProperty);
        set => SetValue(ContinueCommandProperty, value);
    }
}
