using System.Collections;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

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

    /// <summary>Accept on a suggestion card (docs/superpowers/specs/2026-10-29-series-relation-connector-design.md §6.3); fires with the item's Payload.</summary>
    public static readonly StyledProperty<ICommand?> AcceptCommandProperty =
        AvaloniaProperty.Register<PosterRail, ICommand?>(nameof(AcceptCommand));

    /// <summary>When set, the context label becomes a link that fires this with <see cref="ContextCommandParameter"/> (an event row's "open event").</summary>
    public static readonly StyledProperty<ICommand?> ContextCommandProperty =
        AvaloniaProperty.Register<PosterRail, ICommand?>(nameof(ContextCommand));

    public static readonly StyledProperty<object?> ContextCommandParameterProperty =
        AvaloniaProperty.Register<PosterRail, object?>(nameof(ContextCommandParameter));

    public ICommand? AcceptCommand { get => GetValue(AcceptCommandProperty); set => SetValue(AcceptCommandProperty, value); }
    public ICommand? ContextCommand { get => GetValue(ContextCommandProperty); set => SetValue(ContextCommandProperty, value); }
    public object? ContextCommandParameter { get => GetValue(ContextCommandParameterProperty); set => SetValue(ContextCommandParameterProperty, value); }

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? ContextLabel { get => GetValue(ContextLabelProperty); set => SetValue(ContextLabelProperty, value); }
    public IEnumerable? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public bool ShowAddCard { get => GetValue(ShowAddCardProperty); set => SetValue(ShowAddCardProperty, value); }
    public ICommand? AddCommand { get => GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }
    public ICommand? RemoveCommand { get => GetValue(RemoveCommandProperty); set => SetValue(RemoveCommandProperty, value); }
    public ICommand? ClickCommand { get => GetValue(ClickCommandProperty); set => SetValue(ClickCommandProperty, value); }

    /// <summary>The ✕'s tooltip: a suggestion's ✕ dismisses it rather than removing a relation.</summary>
    public static readonly IValueConverter RemoveTipConverter = new FuncValueConverter<bool, string>(isSuggestion => isSuggestion ? "Not related" : "Remove");

    public PosterRail() => InitializeComponent();
}
