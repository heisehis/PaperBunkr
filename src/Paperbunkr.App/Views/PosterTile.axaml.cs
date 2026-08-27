using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Paperbunkr.App.Views;

/// <summary>
/// PosterTile primitive (docs/superpowers/specs/2026-08-24-design-language-foundation-design.md
/// Component Primitives section) - surface2 background, glow-ring hover/focus, optional
/// badge/progress-bar slots. Consumed by the Phase-1 showcase view now; real Library/Home grids
/// (Phases 3-4) place instances of this rather than reinventing card markup per screen.
/// </summary>
public partial class PosterTile : UserControl
{
    public static readonly StyledProperty<IImage?> CoverSourceProperty =
        AvaloniaProperty.Register<PosterTile, IImage?>(nameof(CoverSource));

    public static readonly StyledProperty<string?> TitleTextProperty =
        AvaloniaProperty.Register<PosterTile, string?>(nameof(TitleText));

    public static readonly StyledProperty<string?> MetaTextProperty =
        AvaloniaProperty.Register<PosterTile, string?>(nameof(MetaText));

    /// <summary>Null/empty hides the badge slot entirely.</summary>
    public static readonly StyledProperty<string?> BadgeTextProperty =
        AvaloniaProperty.Register<PosterTile, string?>(nameof(BadgeText));

    public static readonly StyledProperty<bool> ShowProgressProperty =
        AvaloniaProperty.Register<PosterTile, bool>(nameof(ShowProgress));

    /// <summary>0.0-1.0. Only rendered when <see cref="ShowProgress"/> is true.</summary>
    public static readonly StyledProperty<double> ProgressFractionProperty =
        AvaloniaProperty.Register<PosterTile, double>(nameof(ProgressFraction));

    public static readonly StyledProperty<System.Windows.Input.ICommand?> CommandProperty =
        AvaloniaProperty.Register<PosterTile, System.Windows.Input.ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<PosterTile, object?>(nameof(CommandParameter));

    public PosterTile()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
    }

    // Root is a Border, not a Button - Border directly supports BoxShadow (needed for the
    // glow-ring hover/focus treatment) without depending on whether Avalonia's Button promotes
    // that property onto TemplatedControl. Focusable="True" on the root (set in the .axaml) is
    // what makes :pointerover/:focus-visible pseudo-classes actually apply.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    public IImage? CoverSource
    {
        get => GetValue(CoverSourceProperty);
        set => SetValue(CoverSourceProperty, value);
    }

    public string? TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string? MetaText
    {
        get => GetValue(MetaTextProperty);
        set => SetValue(MetaTextProperty, value);
    }

    public string? BadgeText
    {
        get => GetValue(BadgeTextProperty);
        set => SetValue(BadgeTextProperty, value);
    }

    public bool ShowProgress
    {
        get => GetValue(ShowProgressProperty);
        set => SetValue(ShowProgressProperty, value);
    }

    public double ProgressFraction
    {
        get => GetValue(ProgressFractionProperty);
        set => SetValue(ProgressFractionProperty, value);
    }

    public System.Windows.Input.ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }
}
