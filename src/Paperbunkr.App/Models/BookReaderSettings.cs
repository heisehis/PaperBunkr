using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.Models;

public enum BookFontFamilyOption
{
    Serif,
    Sans,
    Mono,
}

public enum BookLineSpacingOption
{
    Compact,
    Normal,
    Relaxed,
}

public enum BookTheme
{
    Light,
    Dark,
    Sepia,
    MatchAppSkin,
}

/// <summary>
/// Font/theme sheet state (docs/superpowers/specs/2026-08-09-novels-epub-pdf-support-design.md
/// §5) - all four confirmed in scope for v1. Not persisted to the database yet (out of the
/// approved Phase 2 scope); resets to defaults each session. An <see cref="ObservableObject"/>
/// (not a plain POCO) so the XAML-bound <c>Settings.FontSize</c>-style nested paths in
/// BookReaderScreen.axaml actually re-render when a setting changes.
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

    public FontFamily ResolvedFontFamily => FontFamilyOption switch
    {
        BookFontFamilyOption.Sans => FontFamily.Parse("Segoe UI,Arial,sans-serif"),
        BookFontFamilyOption.Mono => FontFamily.Parse("Consolas,monospace"),
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
        _ => new SolidColorBrush(Color.Parse("#14161B")),
    };

    public IBrush Foreground => Theme switch
    {
        BookTheme.Light => new SolidColorBrush(Color.Parse("#1A1A1A")),
        BookTheme.Dark => new SolidColorBrush(Color.Parse("#E0E0E0")),
        BookTheme.Sepia => new SolidColorBrush(Color.Parse("#3A2F22")),
        _ => new SolidColorBrush(Color.Parse("#ECE7DB")),
    };
}
