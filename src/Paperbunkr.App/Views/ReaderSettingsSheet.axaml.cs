using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Paperbunkr.App.Models;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Views;

/// <summary>
/// Shared bottom-sheet scaffold for both Books readers' Font &amp; Theme settings (docs/superpowers/
/// specs/2026-09-03-books-reader-hud-redesign-design.md) - redesigned, not reskinned: today's 8 flat
/// stacked sections become 2-3 grouped mini-cards (Typography / Spacing &amp; margins / Theme).
/// <see cref="HasTypographyControls"/> gates the first two cards (<c>True</c> for EPUB, <c>False</c>
/// for PDF, which has no reflowable text to restyle). <see cref="ThemeOptions"/> lets each reader
/// offer its own swatch set off one shared template (EPUB's 6 vs. PDF's 3).
/// </summary>
public partial class ReaderSettingsSheet : UserControl
{
    public ReaderSettingsSheet()
    {
        InitializeComponent();
    }

    /// <summary>EPUB's full theme set, for binding <see cref="ThemeOptions"/> via <c>{x:Static}</c> - Avalonia's XAML compiler doesn't resolve a plain <c>x:Array</c> the way WPF's does, so a static field is simpler than fighting that markup extension. Declared as <see cref="IEnumerable{T}"/> (not <c>BookTheme[]</c>) to match <see cref="ThemeOptionsProperty"/>'s exact type - the compiled-bindings XAML compiler didn't accept the array-to-interface conversion implicitly.</summary>
    public static readonly IEnumerable<BookTheme> AllThemes = new[]
    {
        BookTheme.Light, BookTheme.Dark, BookTheme.Sepia,
        BookTheme.MatchAppSkin, BookTheme.OledBlack, BookTheme.HighContrast,
    };

    /// <summary>PDF's theme set (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md) - no MatchAppSkin/OledBlack/HighContrast, since those were designed around EPUB's reflowable content theming.</summary>
    public static readonly IEnumerable<BookTheme> PdfThemes = new[] { BookTheme.Light, BookTheme.Dark, BookTheme.Sepia };

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(IsOpen));

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly StyledProperty<bool> HasTypographyControlsProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(HasTypographyControls), defaultValue: true);

    public bool HasTypographyControls
    {
        get => GetValue(HasTypographyControlsProperty);
        set => SetValue(HasTypographyControlsProperty, value);
    }

    public static readonly StyledProperty<BookReaderSettings?> SettingsProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, BookReaderSettings?>(nameof(Settings));

    public BookReaderSettings? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<BookTheme>?> ThemeOptionsProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, IEnumerable<BookTheme>?>(nameof(ThemeOptions));

    /// <summary>Which theme swatches to render - EPUB passes all 6 (Light/Dark/Sepia/MatchAppSkin/OledBlack/HighContrast); PDF passes only Light/Dark/Sepia (design's PDF-theme decision).</summary>
    public IEnumerable<BookTheme>? ThemeOptions
    {
        get => GetValue(ThemeOptionsProperty);
        set => SetValue(ThemeOptionsProperty, value);
    }

    public static readonly StyledProperty<ICommand?> SetFontFamilyCommandProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, ICommand?>(nameof(SetFontFamilyCommand));

    public ICommand? SetFontFamilyCommand
    {
        get => GetValue(SetFontFamilyCommandProperty);
        set => SetValue(SetFontFamilyCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> SetLineSpacingCommandProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, ICommand?>(nameof(SetLineSpacingCommand));

    public ICommand? SetLineSpacingCommand
    {
        get => GetValue(SetLineSpacingCommandProperty);
        set => SetValue(SetLineSpacingCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> SetThemeCommandProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, ICommand?>(nameof(SetThemeCommand));

    public ICommand? SetThemeCommand
    {
        get => GetValue(SetThemeCommandProperty);
        set => SetValue(SetThemeCommandProperty, value);
    }

    /// <summary>EPUB only - PDF's chrome never auto-hides, so the toggle would be meaningless there.</summary>
    public static readonly StyledProperty<bool> HasAutoHideToggleProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(HasAutoHideToggle));

    public bool HasAutoHideToggle
    {
        get => GetValue(HasAutoHideToggleProperty);
        set => SetValue(HasAutoHideToggleProperty, value);
    }

    public static readonly StyledProperty<bool> AutoHideToggleProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(AutoHideToggle), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool AutoHideToggle
    {
        get => GetValue(AutoHideToggleProperty);
        set => SetValue(AutoHideToggleProperty, value);
    }

    public static readonly StyledProperty<IBrush?> SheetBackgroundProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, IBrush?>(nameof(SheetBackground));

    public IBrush? SheetBackground
    {
        get => GetValue(SheetBackgroundProperty);
        set => SetValue(SheetBackgroundProperty, value);
    }

    public static readonly StyledProperty<IBrush?> SheetForegroundProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, IBrush?>(nameof(SheetForeground));

    public IBrush? SheetForeground
    {
        get => GetValue(SheetForegroundProperty);
        set => SetValue(SheetForegroundProperty, value);
    }

    public event EventHandler<PointerPressedEventArgs>? ScrimPressed;

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ScrimPressed?.Invoke(this, e);
        e.Handled = true;
    }
}
