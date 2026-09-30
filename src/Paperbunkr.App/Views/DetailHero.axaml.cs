using Avalonia;
using Avalonia.Controls;
using Paperbunkr.App.Services;

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

    // Scroll-linked backdrop parallax: removed 2026-09-08 (no spare image to pan within, so the edge showed), restored
    // 2026-09-28 by the Home pitch (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md C9) through ParallaxBackdrop, which
    // draws the backdrop oversized and clamps the drift to that margin. All scroll tracking lives in that control.

    public DetailHero()
    {
        InitializeComponent();
    }

    /// <summary>Applies the "Hero backdrop" preference (docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #8). Opacity, not
    /// IsVisible, because the backdrop's visibility is already bound to "has a backdrop image". Home's spotlight (<see cref="MutedBackdrop"/>)
    /// is unaffected - the setting is about the Detail screens.</summary>
    private void ApplyBackdropSetting()
        => Backdrop.Opacity = MutedBackdrop || CosmeticThumbnailSettings.HeroBackdrop ? 1 : 0;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CosmeticThumbnailSettings.OverlaySettingsChanged += ApplyBackdropSetting;
        ApplyBackdropSetting();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CosmeticThumbnailSettings.OverlaySettingsChanged -= ApplyBackdropSetting;
        base.OnDetachedFromVisualTree(e);
    }
}
