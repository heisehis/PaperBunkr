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

    // Scroll-linked backdrop parallax (docs/superpowers/specs/2026-09-07-chrome-content-motion-
    // polish-design.md item 2) removed 2026-09-08 (docs/superpowers/specs/2026-09-08-home-navrail-
    // visual-v2-design.md §4 follow-up) - user-confirmed real bug, not a tuning issue: the backdrop
    // Image fills its container exactly (Stretch="UniformToFill", no overscan margin) inside a
    // ClipToBounds="True" Border, so translating it on scroll had no extra image to reveal - the
    // edge it moved away from just showed empty space/the card's flat background instead of more
    // art. A correct implementation needs the backdrop rendered oversized (e.g. ~1.2x its container)
    // so there's always image to pan within; deferred to a future revision rather than fixed inline
    // here. Until then this control has no scroll-tracking code at all - the backdrop is static.

    public DetailHero()
    {
        InitializeComponent();
    }
}
