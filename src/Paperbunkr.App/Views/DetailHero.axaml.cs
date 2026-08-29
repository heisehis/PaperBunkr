using Avalonia;
using Avalonia.Controls;

namespace Paperbunkr.App.Views;

public partial class DetailHero : UserControl
{
    /// <summary>When true, an extra dark scrim sits over the blurred backdrop so it reads as a calm
    /// wash rather than the fuller cinematic treatment the detail screens use (docs/superpowers/
    /// specs/2026-08-28-home-screen-redesign-design.md §3). Home's spotlight sets this.</summary>
    public static readonly StyledProperty<bool> MutedBackdropProperty =
        AvaloniaProperty.Register<DetailHero, bool>(nameof(MutedBackdrop));

    /// <summary>Overall hero height. Default matches the detail screens (360); Home's spotlight
    /// runs shorter (docs/superpowers/specs/2026-08-28-home-screen-redesign-design.md §3).</summary>
    public static readonly StyledProperty<double> HeroHeightProperty =
        AvaloniaProperty.Register<DetailHero, double>(nameof(HeroHeight), 360d);

    public bool MutedBackdrop
    {
        get => GetValue(MutedBackdropProperty);
        set => SetValue(MutedBackdropProperty, value);
    }

    public double HeroHeight
    {
        get => GetValue(HeroHeightProperty);
        set => SetValue(HeroHeightProperty, value);
    }

    public DetailHero() => InitializeComponent();
}
