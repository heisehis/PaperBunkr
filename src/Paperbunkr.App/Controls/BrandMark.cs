using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentIcons.Common;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Controls;

/// <summary>What kind of value a <see cref="BrandMark"/> is showing.</summary>
public enum MarkFamily
{
    Service,
    Publisher,
    Format,
    AgeRating,
    Language,

    /// <summary><see cref="Paperbunkr.Data.Entities.ReadingStatus"/> as a coloured glyph
    /// (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-design.md §8a).</summary>
    ReadingStatus,

    /// <summary>A manga chapter's scanlation group as a "people" glyph + name.</summary>
    ScanGroup,
}

/// <summary>
/// Renders a metadata value as a brand / metadata mark - a bundled SVG logo, a country flag, a
/// FluentIcons glyph, a coloured letter chip, or (when nothing better fits) just the plain text,
/// so a call site can replace a bare <c>&lt;TextBlock Text="{Binding X}"/&gt;</c> with a
/// <c>&lt;ctl:BrandMark .../&gt;</c> with no layout regression
/// (docs/superpowers/specs/2026-08-28-brand-metadata-iconography-design.md).
///
/// Code-only <see cref="TemplatedControl"/> (the <c>SplitText</c> pattern - template is an implicit
/// <c>ControlTheme</c> in <c>Styles/Marks.axaml</c>). All resolution runs through the static
/// <see cref="MarkResolver"/>; SVG rasterisation through <see cref="SvgMarkRenderer"/>.
/// </summary>
public class BrandMark : TemplatedControl
{
    public static readonly StyledProperty<MarkFamily> FamilyProperty =
        AvaloniaProperty.Register<BrandMark, MarkFamily>(nameof(Family));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<BrandMark, string?>(nameof(Value));

    /// <summary>The issue/series year a <see cref="MarkFamily.Publisher"/> mark should be resolved
    /// for, so a publisher's era-specific CE logo (DC 1977-2004, Dark Horse 1992-2003, ...) is
    /// shown (docs/superpowers/specs/2026-09-25-publisher-icons-and-reader-textures-design.md §A2).
    /// Ignored by every other family.</summary>
    public static readonly StyledProperty<int?> YearProperty =
        AvaloniaProperty.Register<BrandMark, int?>(nameof(Year));

    /// <summary>Optional issue month, so a month-qualified era logo (DC Comics(2024_12)) is chosen
    /// over the year's default. Only meaningful together with <see cref="Year"/>.</summary>
    public static readonly StyledProperty<int?> MonthProperty =
        AvaloniaProperty.Register<BrandMark, int?>(nameof(Month));

    /// <summary>For a mark laid directly on arbitrary art (a Library cover tile): publisher SVGs keep
    /// their own colours (no theme tint) and, like raster logos, sit on a light or dark plate chosen
    /// from their pixels so they read on any cover. Off everywhere else.</summary>
    public static readonly StyledProperty<bool> EnsureContrastProperty =
        AvaloniaProperty.Register<BrandMark, bool>(nameof(EnsureContrast));

    /// <summary>Widest a logo image may be drawn. A wide wordmark (Dynamite, Titan) at
    /// <see cref="MarkSize"/> height would otherwise sprawl across a whole cover tile, so it is scaled
    /// down (aspect kept) until it fits; square-ish logos stay at full height. Infinity (default) =
    /// no cap, i.e. height-only sizing as everywhere else.</summary>
    public static readonly StyledProperty<double> MaxMarkWidthProperty =
        AvaloniaProperty.Register<BrandMark, double>(nameof(MaxMarkWidth), defaultValue: double.PositiveInfinity);

    /// <summary>The height the template draws the image at: <see cref="MarkSize"/>, reduced when the
    /// logo is wide enough that <see cref="MaxMarkWidth"/> would otherwise be exceeded.</summary>
    public static readonly DirectProperty<BrandMark, double> MarkHeightProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, double>(nameof(MarkHeight), o => o._markHeight);

    public static readonly StyledProperty<bool> ShowTextProperty =
        AvaloniaProperty.Register<BrandMark, bool>(nameof(ShowText), defaultValue: true);

    public static readonly StyledProperty<double> MarkSizeProperty =
        AvaloniaProperty.Register<BrandMark, double>(nameof(MarkSize), defaultValue: 16d);

    // --- computed outputs the template binds to ---
    public static readonly DirectProperty<BrandMark, IImage?> ImageSourceProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, IImage?>(nameof(ImageSource), o => o._imageSource);

    public static readonly DirectProperty<BrandMark, Symbol> GlyphProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, Symbol>(nameof(Glyph), o => o._glyph);

    public static readonly DirectProperty<BrandMark, string?> LabelProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, string?>(nameof(Label), o => o._label);

    /// <summary>Per-mark glyph colour for a <see cref="MarkKind.Glyph"/> result whose
    /// <see cref="MarkSpec.Foreground"/> is a <c>#hex</c> (reading-status). Null → the template
    /// falls back to the control's inherited <see cref="TemplatedControl.Foreground"/>.</summary>
    public static readonly DirectProperty<BrandMark, IBrush?> GlyphBrushProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, IBrush?>(nameof(GlyphBrush), o => o._glyphBrush);

    public static readonly DirectProperty<BrandMark, IBrush?> ChipBackgroundProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, IBrush?>(nameof(ChipBackground), o => o._chipBackground);

    public static readonly DirectProperty<BrandMark, MarkKind> ResolvedKindProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, MarkKind>(nameof(ResolvedKind), o => o._kind);

    public static readonly DirectProperty<BrandMark, bool> ShowLabelProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, bool>(nameof(ShowLabel), o => o._showLabel);

    public static readonly DirectProperty<BrandMark, bool> IsImageProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, bool>(nameof(IsImage), o => o._isImage);

    /// <summary>True for a <see cref="MarkKind.Raster"/> publisher logo - the template draws it on
    /// <see cref="PlateBrush"/> instead of the bare SVG image.</summary>
    public static readonly DirectProperty<BrandMark, bool> IsRasterProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, bool>(nameof(IsRaster), o => o._isRaster);

    /// <summary>Contrast plate behind a raster logo: null for an opaque image, else a light or dark
    /// brush chosen from the logo's own pixels (<see cref="PublisherIconBitmaps"/>).</summary>
    /// <summary>Template switch: an image drawn inside the plate <c>Border</c> (a raster logo, or a
    /// publisher SVG that got a contrast plate).</summary>
    public static readonly DirectProperty<BrandMark, bool> IsPlatedImageProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, bool>(nameof(IsPlatedImage), o => o._isPlatedImage);

    /// <summary>Template switch: a plain SVG / flag image with no plate.</summary>
    public static readonly DirectProperty<BrandMark, bool> IsBareImageProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, bool>(nameof(IsBareImage), o => o._isBareImage);

    public static readonly DirectProperty<BrandMark, IBrush?> PlateBrushProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, IBrush?>(nameof(PlateBrush), o => o._plateBrush);

    public static readonly DirectProperty<BrandMark, bool> IsGlyphProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, bool>(nameof(IsGlyph), o => o._isGlyph);

    public static readonly DirectProperty<BrandMark, bool> IsChipProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, bool>(nameof(IsChip), o => o._isChip);

    public static readonly DirectProperty<BrandMark, bool> IsPlainTextProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, bool>(nameof(IsPlainText), o => o._isPlainText);

    private IImage? _imageSource;
    private Symbol _glyph;
    private string? _label;
    private IBrush? _chipBackground;
    private IBrush? _glyphBrush;
    private MarkKind _kind = MarkKind.None;
    private bool _showLabel;
    private bool _isImage;
    private bool _isGlyph;
    private bool _isRaster;
    private double _markHeight = 16d;
    private bool _isPlatedImage;
    private bool _isBareImage;
    private IBrush? _plateBrush;
    private bool _isChip;
    private bool _isPlainText;

    public MarkFamily Family { get => GetValue(FamilyProperty); set => SetValue(FamilyProperty, value); }
    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int? Year { get => GetValue(YearProperty); set => SetValue(YearProperty, value); }
    public int? Month { get => GetValue(MonthProperty); set => SetValue(MonthProperty, value); }
    public bool EnsureContrast { get => GetValue(EnsureContrastProperty); set => SetValue(EnsureContrastProperty, value); }
    public double MaxMarkWidth { get => GetValue(MaxMarkWidthProperty); set => SetValue(MaxMarkWidthProperty, value); }
    public double MarkHeight => _markHeight;
    public bool ShowText { get => GetValue(ShowTextProperty); set => SetValue(ShowTextProperty, value); }
    public double MarkSize { get => GetValue(MarkSizeProperty); set => SetValue(MarkSizeProperty, value); }

    public IImage? ImageSource => _imageSource;
    public Symbol Glyph => _glyph;
    public string? Label => _label;
    public IBrush? ChipBackground => _chipBackground;
    public IBrush? GlyphBrush => _glyphBrush;
    public MarkKind ResolvedKind => _kind;
    public bool ShowLabel => _showLabel;
    public bool IsImage => _isImage;
    public bool IsGlyph => _isGlyph;
    public bool IsRaster => _isRaster;
    public bool IsPlatedImage => _isPlatedImage;
    public bool IsBareImage => _isBareImage;
    public IBrush? PlateBrush => _plateBrush;
    public bool IsChip => _isChip;
    public bool IsPlainText => _isPlainText;

    static BrandMark()
    {
        AffectsMeasure<BrandMark>(ResolvedKindProperty, ImageSourceProperty, LabelProperty, MarkHeightProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FamilyProperty || change.Property == ValueProperty || change.Property == EnsureContrastProperty || change.Property == MaxMarkWidthProperty || change.Property == YearProperty || change.Property == MonthProperty ||
            change.Property == MarkSizeProperty || change.Property == ShowTextProperty ||
            change.Property == ForegroundProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        MarkSpec spec = Family switch
        {
            MarkFamily.Service => MarkResolver.Instance.ResolveService(Value),
            MarkFamily.Publisher => MarkResolver.Instance.ResolvePublisher(Value, Year, Month),
            MarkFamily.Format => MarkResolver.Instance.ResolveFormat(Value),
            MarkFamily.AgeRating => MarkResolver.Instance.ResolveAgeRating(Value),
            MarkFamily.Language => MarkResolver.Instance.ResolveLanguage(Value),
            MarkFamily.ReadingStatus => MarkResolver.Instance.ResolveReadingStatus(Value),
            MarkFamily.ScanGroup => MarkResolver.Instance.ResolveScanGroup(Value),
            _ => MarkSpec.None,
        };

        SetAndRaise(ResolvedKindProperty, ref _kind, spec.Kind);
        Symbol glyph = spec.Glyph ?? default;
        SetAndRaise(GlyphProperty, ref _glyph, glyph);

        SetAndRaise(IsImageProperty, ref _isImage, spec.Kind is MarkKind.SvgAsset or MarkKind.Flag);
        SetAndRaise(IsGlyphProperty, ref _isGlyph, spec.Kind is MarkKind.Glyph);
        SetAndRaise(IsRasterProperty, ref _isRaster, spec.Kind is MarkKind.Raster);
        SetAndRaise(IsChipProperty, ref _isChip, spec.Kind is MarkKind.LetterMark);
        SetAndRaise(IsPlainTextProperty, ref _isPlainText,
            spec.Kind is MarkKind.Text or MarkKind.None && !string.IsNullOrWhiteSpace(spec.Text ?? Value));

        string? label = spec.Kind switch
        {
            MarkKind.LetterMark => spec.Text,
            MarkKind.Glyph => spec.Text,
            MarkKind.Text or MarkKind.None => spec.Text ?? Value,
            _ => Value,
        };
        SetAndRaise(LabelProperty, ref _label, label);

        SetAndRaise(ShowLabelProperty, ref _showLabel,
            ShowText && spec.Kind is MarkKind.SvgAsset or MarkKind.Flag or MarkKind.Glyph or MarkKind.Raster);

        IBrush? bg = TryBrush(spec.Background);
        SetAndRaise(ChipBackgroundProperty, ref _chipBackground, bg);

        // A Glyph result may carry its own #hex foreground (reading-status colour-codes each
        // status); anything else falls back to the control's inherited Foreground. Rebuild() also
        // re-runs on ForegroundProperty changes, so this stays correct if the theme swaps the
        // inherited brush later. SVG tinting is handled separately below.
        IBrush? glyphBrush = spec.Kind is MarkKind.Glyph ? (TryBrush(spec.Foreground) ?? Foreground) : null;
        SetAndRaise(GlyphBrushProperty, ref _glyphBrush, glyphBrush);

        IImage? image = null;
        IBrush? svgPlate = null;
        if (spec.Kind is MarkKind.SvgAsset or MarkKind.Flag && spec.AssetPath is { } path)
        {
            // Supersample the SVG raster well above display height (4x, min 64px) so it stays
            // crisp after Avalonia's HighQuality downscale to Image.Height on 150-200% DPI
            // screens - 2x was visibly soft on publisher wordmarks / provider logos there
            // (user report 2026-09-04). Marks are tiny + memoised, so the extra pixels are free.
            int px = Math.Max(64, (int)Math.Round(MarkSize * 4));
            // A contrast-guaranteed publisher mark keeps its own colours and gets a plate instead of
            // the theme tint (a white-tinted wordmark would vanish on a light cover).
            bool plateSvg = EnsureContrast && Family == MarkFamily.Publisher && spec.Kind is MarkKind.SvgAsset;
            Color? tint = spec.Foreground == MarkResolver.ThemeTint && !plateSvg
                ? (Foreground as ISolidColorBrush)?.Color ?? Colors.White
                : null;
            image = SvgMarkRenderer.Render(path, px, tint);
            if (plateSvg)
            {
                svgPlate = SvgMarkRenderer.PlateOf(image as Bitmap) switch
                {
                    IconPlate.Light => LightPlate,
                    IconPlate.Dark => DarkPlate,
                    _ => null,
                };
            }
        }

        IBrush? plate = svgPlate;
        if (spec.Kind is MarkKind.Raster && spec.AssetPath is { } rasterPath)
        {
            if (PublisherIconBitmaps.Load(rasterPath) is { } icon)
            {
                image = icon.Bitmap;
                plate = icon.Plate switch
                {
                    IconPlate.Light => LightPlate,
                    IconPlate.Dark => DarkPlate,
                    _ => null,
                };
            }
        }

        SetAndRaise(ImageSourceProperty, ref _imageSource, image);
        SetAndRaise(MarkHeightProperty, ref _markHeight, FitHeight(image, MarkSize, MaxMarkWidth));
        SetAndRaise(PlateBrushProperty, ref _plateBrush, plate);
        SetAndRaise(IsPlatedImageProperty, ref _isPlatedImage, spec.Kind is MarkKind.Raster || (spec.Kind is MarkKind.SvgAsset && plate is not null));
        SetAndRaise(IsBareImageProperty, ref _isBareImage, spec.Kind is MarkKind.SvgAsset or MarkKind.Flag && !_isPlatedImage);

        AutomationProperties.SetName(this, spec.Text ?? Value ?? string.Empty);
    }

    // Theme-independent on purpose: a plate contrasts with the *logo* (CE's set is drawn for a light
    // surface), not with the app skin, so these are not Pb* skin tokens.
    private static readonly IBrush LightPlate = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#F3F3F5"));
    private static readonly IBrush DarkPlate = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Color.Parse("#1C1C20"));

    /// <summary>Display height for <paramref name="image"/>: <paramref name="markSize"/>, or less when
    /// the image's aspect ratio would make it wider than <paramref name="maxWidth"/>.</summary>
    internal static double FitHeight(IImage? image, double markSize, double maxWidth)
    {
        if (image is null || double.IsInfinity(maxWidth) || maxWidth <= 0)
        {
            return markSize;
        }

        Size size = image.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return markSize;
        }

        return Math.Min(markSize, maxWidth * size.Height / size.Width);
    }

    private static IBrush? TryBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex[0] != '#')
        {
            return null;
        }

        try
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
