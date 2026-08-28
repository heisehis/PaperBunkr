using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Views;

public partial class PosterRail : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PosterRail, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ContextLabelProperty =
        AvaloniaProperty.Register<PosterRail, string?>(nameof(ContextLabel));

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<PosterRail, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<bool> ShowAddCardProperty =
        AvaloniaProperty.Register<PosterRail, bool>(nameof(ShowAddCard));

    public static readonly StyledProperty<ICommand?> AddCommandProperty =
        AvaloniaProperty.Register<PosterRail, ICommand?>(nameof(AddCommand));

    public static readonly StyledProperty<ICommand?> RemoveCommandProperty =
        AvaloniaProperty.Register<PosterRail, ICommand?>(nameof(RemoveCommand));

    public static readonly StyledProperty<ICommand?> ClickCommandProperty =
        AvaloniaProperty.Register<PosterRail, ICommand?>(nameof(ClickCommand));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? ContextLabel { get => GetValue(ContextLabelProperty); set => SetValue(ContextLabelProperty, value); }
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public bool ShowAddCard { get => GetValue(ShowAddCardProperty); set => SetValue(ShowAddCardProperty, value); }
    public ICommand? AddCommand { get => GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }
    public ICommand? RemoveCommand { get => GetValue(RemoveCommandProperty); set => SetValue(RemoveCommandProperty, value); }
    public ICommand? ClickCommand { get => GetValue(ClickCommandProperty); set => SetValue(ClickCommandProperty, value); }

    public PosterRail() => InitializeComponent();
}
