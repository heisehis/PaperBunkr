using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>
/// Font/theme sheet state (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md
/// §5, extended by docs/superpowers/specs/2026-09-01-books-reader-ergonomics-and-annotations-
/// design.md). Persisted via <see cref="AppSettings"/> global defaults + per-<see cref="Book"/>
/// overrides (see <c>BookReaderScreenViewModel.LoadBook</c>'s resolution chain) - this object
/// itself stays session state, seeded/written by the view model, not by database reads/writes of
/// its own. An <see cref="ObservableObject"/> (not a plain POCO) so the XAML-bound
/// <c>Settings.FontSize</c>-style nested paths in BookReaderScreen.axaml actually re-render when a
/// setting changes.
///
/// <see cref="BookFontFamilyOption"/>/<see cref="BookLineSpacingOption"/>/<see cref="BookTheme"/>
/// live in <c>Paperbunkr.Data.Entities</c>, not here - <see cref="AppSettings"/>'s columns need to
/// reference them and that project can't depend on this one.
/// </summary>
public sealed partial class BookReaderSettings : ObservableObject
{
    public const double MinFontSize = 12;
    public const double MaxFontSize = 28;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineHeightPixels))]
    private double _fontSize = 17;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolvedFontFamily))]
    private BookFontFamilyOption _fontFamilyOption = BookFontFamilyOption.Serif;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineHeightMultiplier))]
    [NotifyPropertyChangedFor(nameof(LineHeightPixels))]
    private BookLineSpacingOption _lineSpacing = BookLineSpacingOption.Normal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Background))]
    [NotifyPropertyChangedFor(nameof(Foreground))]
    private BookTheme _theme = BookTheme.MatchAppSkin;

    /// <summary>Extra tracking applied per character, in pixels - drives <c>ParagraphView</c>'s <c>TextLayout</c> directly (design spec Component 2).</summary>
    [ObservableProperty]
    private double _characterSpacing;

    /// <summary>Extra advance-width inserted after each space character, in pixels - see the design spec's Risks section for why this needs <c>ParagraphView</c> rather than a <c>TextBlock</c> property.</summary>
    [ObservableProperty]
    private double _wordSpacing;

    /// <summary>Bottom margin per paragraph, in pixels. Default 10 matches the fixed value this replaces (BookReaderScreen.axaml's old hardcoded paragraph <c>Margin="0,0,0,10"</c>).</summary>
    [ObservableProperty]
    private double _paragraphSpacing = 10;

    /// <summary>Horizontal padding either side of the reading column, in pixels. Default 40 matches the fixed value this replaces (BookReaderScreen.axaml's old hardcoded <c>ScrollViewer Padding="40,70,40,60"</c>).</summary>
    [ObservableProperty]
    private double _pageMargin = 40;

    public FontFamily ResolvedFontFamily => FontFamilyOption switch
    {
        BookFontFamilyOption.Sans => FontFamily.Parse("Segoe UI,Arial,sans-serif"),
        BookFontFamilyOption.Mono => FontFamily.Parse("Consolas,monospace"),
        BookFontFamilyOption.OpenDyslexic => FontFamily.Parse("OpenDyslexic,Georgia,Cambria,serif"),
        _ => FontFamily.Parse("Georgia,Cambria,serif"),
    };

    /// <summary>Multiplier over <see cref="FontSize"/> for line height, per <see cref="LineSpacing"/>.</summary>
    public double LineHeightMultiplier => LineSpacing switch
    {
        BookLineSpacingOption.Compact => 1.3,
        BookLineSpacingOption.Relaxed => 1.9,
        _ => 1.6,
    };

    /// <summary>Pixel line height for direct XAML binding (TextBlock.LineHeight takes pixels, not a multiplier).</summary>
    public double LineHeightPixels => FontSize * LineHeightMultiplier;

    // Same #14161B/#ECE7DB pair App.axaml defines for PbBgColor/PbTextColor - MatchAppSkin uses a
    // concrete value rather than a live DynamicResource lookup, since Background/Foreground need
    // to be plain IBrush values usable identically across all four theme options, not a XAML-only
    // binding path for one of them.
    public IBrush Background => Theme switch
    {
        BookTheme.Light => Brushes.White,
        BookTheme.Dark => new SolidColorBrush(Color.Parse("#1C1C1C")),
        BookTheme.Sepia => new SolidColorBrush(Color.Parse("#F2E6CE")),
        BookTheme.OledBlack => Brushes.Black,
        BookTheme.HighContrast => Brushes.Black,
        _ => new SolidColorBrush(Color.Parse("#14161B")),
    };

    public IBrush Foreground => Theme switch
    {
        BookTheme.Light => new SolidColorBrush(Color.Parse("#1A1A1A")),
        BookTheme.Dark => new SolidColorBrush(Color.Parse("#E0E0E0")),
        BookTheme.Sepia => new SolidColorBrush(Color.Parse("#3A2F22")),
        BookTheme.OledBlack => new SolidColorBrush(Color.Parse("#E0E0E0")),
        BookTheme.HighContrast => Brushes.White,
        _ => new SolidColorBrush(Color.Parse("#ECE7DB")),
    };
}
