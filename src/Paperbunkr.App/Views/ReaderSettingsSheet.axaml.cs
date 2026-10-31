using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly OverlayWindowController _overlayController;

    public ReaderSettingsSheet()
    {
        InitializeComponent();

        // OverlayRoot (the scrim+panel Grid declared in XAML) is only nominally this control's own
        // Content - detach it immediately so it never renders inline, then let the controller
        // re-parent it into/out of a real owned Window as IsOpen changes (see OverlayHostWindow's
        // own doc comment for why a real Window instead of a Popup).
        _overlayController = new OverlayWindowController(OverlayRoot, () => OverlayReference);
        Content = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsOpenProperty)
        {
            _overlayController.SetOpen(change.GetNewValue<bool>());
        }
        else if (change.Property == SheetBackgroundProperty || change.Property == SheetForegroundProperty)
        {
            // Books redesign §5.3: the sheet is a card of the page; its tokens follow the book theme (opaque page colour, not the chrome tint).
            var opaque = SheetBackground is ISolidColorBrush tint ? new SolidColorBrush(Color.FromRgb(tint.Color.R, tint.Color.G, tint.Color.B)) : null;
            Reader.BookThemePanelResources.Apply(SheetCard.Resources, opaque, SheetForeground);
        }
    }

    /// <summary>Belt-and-braces: see ReaderListDrawer's identical override for why.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _overlayController.SetOpen(false);
    }

    /// <summary>EPUB's full theme set, for binding <see cref="ThemeOptions"/> via <c>{x:Static}</c> - Avalonia's XAML compiler doesn't resolve a plain <c>x:Array</c> the way WPF's does, so a static field is simpler than fighting that markup extension. Declared as <see cref="IEnumerable{T}"/> (not <c>BookTheme[]</c>) to match <see cref="ThemeOptionsProperty"/>'s exact type - the compiled-bindings XAML compiler didn't accept the array-to-interface conversion implicitly.</summary>
    public static readonly IEnumerable<BookTheme> AllThemes = new[]
    {
        BookTheme.Light, BookTheme.Dark, BookTheme.Sepia,
        BookTheme.MatchAppSkin, BookTheme.OledBlack, BookTheme.HighContrast,
    };

    private static IReadOnlyList<string>? s_installedFonts;

    /// <summary>The families installed on this machine, for the "Font from this machine" box (computed on first use; the font manager needs a running app).</summary>
    public static IReadOnlyList<string> InstalledFonts =>
        s_installedFonts ??= FontManager.Current.SystemFonts.Select(f => f.Name).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>PDF's theme set (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-design.md) - no MatchAppSkin/OledBlack/HighContrast, since those were designed around EPUB's reflowable content theming.</summary>
    public static readonly IEnumerable<BookTheme> PdfThemes = new[] { BookTheme.Light, BookTheme.Dark, BookTheme.Sepia };

    /// <summary>The host screen's own real, content-bearing root Grid (e.g. <c>{Binding #RootGrid}</c>) - used only to size the scrim/panel correctly (see ReaderSettingsSheet.axaml's own comment, same fix and reason as ReaderListDrawer.OverlayReference).</summary>
    public static readonly StyledProperty<Layoutable?> OverlayReferenceProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, Layoutable?>(nameof(OverlayReference));

    public Layoutable? OverlayReference
    {
        get => GetValue(OverlayReferenceProperty);
        set => SetValue(OverlayReferenceProperty, value);
    }

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

    /// <summary>EPUB-family reflow reader only - the line ruler and auto-scroll act on the reading page, which the PDF reader does not have (docs/superpowers/specs/2026-10-27-books-slice3b-reader-typography-aids-design.md §4).</summary>
    public static readonly StyledProperty<bool> HasReadingAidsProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(HasReadingAids));

    public bool HasReadingAids
    {
        get => GetValue(HasReadingAidsProperty);
        set => SetValue(HasReadingAidsProperty, value);
    }

    public static readonly StyledProperty<bool> RulerOnProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(RulerOn), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool RulerOn
    {
        get => GetValue(RulerOnProperty);
        set => SetValue(RulerOnProperty, value);
    }

    /// <summary>False hides the read-aloud card (no speech voices on this machine).</summary>
    public static readonly StyledProperty<bool> HasSpeechProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(HasSpeech));

    public bool HasSpeech
    {
        get => GetValue(HasSpeechProperty);
        set => SetValue(HasSpeechProperty, value);
    }

    public static readonly StyledProperty<bool> SpeakOnProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(SpeakOn), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool SpeakOn
    {
        get => GetValue(SpeakOnProperty);
        set => SetValue(SpeakOnProperty, value);
    }

    public static readonly StyledProperty<string?> SpeechVoiceProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, string?>(nameof(SpeechVoice), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string? SpeechVoice
    {
        get => GetValue(SpeechVoiceProperty);
        set => SetValue(SpeechVoiceProperty, value);
    }

    public static readonly StyledProperty<IEnumerable<string>?> SpeechVoicesProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, IEnumerable<string>?>(nameof(SpeechVoices));

    public IEnumerable<string>? SpeechVoices
    {
        get => GetValue(SpeechVoicesProperty);
        set => SetValue(SpeechVoicesProperty, value);
    }

    public static readonly StyledProperty<bool> LookUpOnlineProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(LookUpOnline), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool LookUpOnline
    {
        get => GetValue(LookUpOnlineProperty);
        set => SetValue(LookUpOnlineProperty, value);
    }

    /// <summary>True while the reader pages (paged/scroll spec §5); picks the active segment and disables auto-scroll.</summary>
    public static readonly StyledProperty<bool> LayoutPagedProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(LayoutPaged));

    public bool LayoutPaged
    {
        get => GetValue(LayoutPagedProperty);
        set => SetValue(LayoutPagedProperty, value);
    }

    public static readonly StyledProperty<ICommand?> SetLayoutCommandProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, ICommand?>(nameof(SetLayoutCommand));

    public ICommand? SetLayoutCommand
    {
        get => GetValue(SetLayoutCommandProperty);
        set => SetValue(SetLayoutCommandProperty, value);
    }

    public static readonly StyledProperty<bool> AutoScrollOnProperty =
        AvaloniaProperty.Register<ReaderSettingsSheet, bool>(nameof(AutoScrollOn), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public bool AutoScrollOn
    {
        get => GetValue(AutoScrollOnProperty);
        set => SetValue(AutoScrollOnProperty, value);
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
