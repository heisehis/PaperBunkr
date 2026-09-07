using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Paperbunkr.App.Controls;

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

    // Backdrop parallax (docs/superpowers/specs/2026-09-07-chrome-content-motion-polish-design.md
    // item 2) - found via the visual tree rather than wired per hosting screen, so it applies to
    // every DetailHero consumer (comic/manga/book detail, Home spotlight) with no per-consumer code.
    private const double ParallaxFactor = 0.32;

    private ScrollViewer? _scrollViewer;
    private Image? _backdrop;

    public DetailHero()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _backdrop = this.FindControl<Image>("Backdrop");
        _scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer = null;
        }

        _backdrop = null;
    }

    /// <summary>Reduced Motion pins the offset to 0 entirely rather than merely skipping a
    /// transition - this is continuous scroll-linked depth motion, not a discrete enter/exit, so
    /// "reduced" means "off," matching the same PbMotionStandard-zeroing every other consumer relies
    /// on, just read from code-behind instead of a XAML DynamicResource binding.</summary>
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_backdrop is null || _scrollViewer is null)
        {
            return;
        }

        _backdrop.RenderTransform = MotionTokens.IsReducedMotion()
            ? null
            : new TranslateTransform(0, _scrollViewer.Offset.Y * ParallaxFactor);
    }
}
