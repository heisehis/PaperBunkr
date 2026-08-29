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

    public static readonly DirectProperty<BrandMark, IBrush?> ChipBackgroundProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, IBrush?>(nameof(ChipBackground), o => o._chipBackground);

    public static readonly DirectProperty<BrandMark, MarkKind> ResolvedKindProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, MarkKind>(nameof(ResolvedKind), o => o._kind);

    public static readonly DirectProperty<BrandMark, bool> ShowLabelProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, bool>(nameof(ShowLabel), o => o._showLabel);

    public static readonly DirectProperty<BrandMark, bool> IsImageProperty =
        AvaloniaProperty.RegisterDirect<BrandMark, bool>(nameof(IsImage), o => o._isImage);

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
    private MarkKind _kind = MarkKind.None;
    private bool _showLabel;
    private bool _isImage;
    private bool _isGlyph;
    private bool _isChip;
    private bool _isPlainText;

    public MarkFamily Family { get => GetValue(FamilyProperty); set => SetValue(FamilyProperty, value); }
    public string? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool ShowText { get => GetValue(ShowTextProperty); set => SetValue(ShowTextProperty, value); }
    public double MarkSize { get => GetValue(MarkSizeProperty); set => SetValue(MarkSizeProperty, value); }

    public IImage? ImageSource => _imageSource;
    public Symbol Glyph => _glyph;
    public string? Label => _label;
    public IBrush? ChipBackground => _chipBackground;
    public MarkKind ResolvedKind => _kind;
    public bool ShowLabel => _showLabel;
    public bool IsImage => _isImage;
    public bool IsGlyph => _isGlyph;
    public bool IsChip => _isChip;
    public bool IsPlainText => _isPlainText;

    static BrandMark()
    {
        AffectsMeasure<BrandMark>(ResolvedKindProperty, ImageSourceProperty, LabelProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == FamilyProperty || change.Property == ValueProperty ||
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
            MarkFamily.Publisher => MarkResolver.Instance.ResolvePublisher(Value),
            MarkFamily.Format => MarkResolver.Instance.ResolveFormat(Value),
            MarkFamily.AgeRating => MarkResolver.Instance.ResolveAgeRating(Value),
            MarkFamily.Language => MarkResolver.Instance.ResolveLanguage(Value),
            _ => MarkSpec.None,
        };

        SetAndRaise(ResolvedKindProperty, ref _kind, spec.Kind);
        Symbol glyph = spec.Glyph ?? default;
        SetAndRaise(GlyphProperty, ref _glyph, glyph);

        SetAndRaise(IsImageProperty, ref _isImage, spec.Kind is MarkKind.SvgAsset or MarkKind.Flag);
        SetAndRaise(IsGlyphProperty, ref _isGlyph, spec.Kind is MarkKind.Glyph);
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
            ShowText && spec.Kind is MarkKind.SvgAsset or MarkKind.Flag or MarkKind.Glyph);

        IBrush? bg = TryBrush(spec.Background);
        SetAndRaise(ChipBackgroundProperty, ref _chipBackground, bg);

        IImage? image = null;
        if (spec.Kind is MarkKind.SvgAsset or MarkKind.Flag && spec.AssetPath is { } path)
        {
            int px = Math.Max(8, (int)Math.Round(MarkSize * 2)); // render 2x for crispness
            Color? tint = spec.Foreground == MarkResolver.ThemeTint
                ? (Foreground as ISolidColorBrush)?.Color ?? Colors.White
                : null;
            image = SvgMarkRenderer.Render(path, px, tint);
        }
        SetAndRaise(ImageSourceProperty, ref _imageSource, image);

        AutomationProperties.SetName(this, spec.Text ?? Value ?? string.Empty);
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
