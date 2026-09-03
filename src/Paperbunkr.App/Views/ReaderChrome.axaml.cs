using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Paperbunkr.App.Views;

/// <summary>
/// Which reader hosts this chrome (docs/superpowers/specs/2026-09-03-books-reader-hud-redesign-
/// design.md) - EPUB's chrome starts hidden and taps to reveal (all of that state/timer logic lives
/// in <c>BookReaderScreenViewModel</c>, not here); PDF's stays fixed. <see cref="ReaderChrome"/>
/// itself has no branching logic keyed off this today - both readers simply bind (or don't bind)
/// <see cref="ReaderChrome.IsChromeVisible"/> themselves - but it's kept as a real, explicit property
/// per the approved design rather than an implicit per-screen difference, and is the natural place to
/// hang future mode-specific behavior if one ever needs it.
/// </summary>
public enum ReaderChromeMode
{
    TapToHide,
    AlwaysVisible,
}

/// <summary>
/// Shared top/bottom chrome bar for both Books readers (docs/superpowers/specs/2026-09-03-books-
/// reader-hud-redesign-design.md) - one control, feature-gated: every possible top-bar button is a
/// nullable <see cref="ICommand"/> property that hides itself when unbound (see
/// <c>ReaderChrome.axaml</c>'s <c>IsVisible</c> bindings via <c>ObjectConverters.IsNotNull</c>), so
/// EPUB's screen binds 7 of them and PDF's binds 4 without either screen needing a different control.
/// Previous/Next stay real bindable commands too, but a host that needs to intercept the click first
/// (EPUB's WebView-scroll-then-VM-fallback logic in <c>BookReaderScreen.axaml.cs</c>) can instead
/// subscribe <see cref="PreviousRequested"/>/<see cref="NextRequested"/>, which fire on every click
/// regardless of whether a command is bound.
/// </summary>
public partial class ReaderChrome : UserControl
{
    public ReaderChrome()
    {
        InitializeComponent();
    }

    public static readonly StyledProperty<ReaderChromeMode> ModeProperty =
        AvaloniaProperty.Register<ReaderChrome, ReaderChromeMode>(nameof(Mode), defaultValue: ReaderChromeMode.AlwaysVisible);

    public ReaderChromeMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ReaderChrome, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly StyledProperty<bool> IsChromeVisibleProperty =
        AvaloniaProperty.Register<ReaderChrome, bool>(nameof(IsChromeVisible), defaultValue: true);

    public bool IsChromeVisible
    {
        get => GetValue(IsChromeVisibleProperty);
        set => SetValue(IsChromeVisibleProperty, value);
    }

    public static readonly StyledProperty<IBrush?> ChromeBackgroundProperty =
        AvaloniaProperty.Register<ReaderChrome, IBrush?>(nameof(ChromeBackground));

    public IBrush? ChromeBackground
    {
        get => GetValue(ChromeBackgroundProperty);
        set => SetValue(ChromeBackgroundProperty, value);
    }

    public static readonly StyledProperty<IBrush?> ChromeForegroundProperty =
        AvaloniaProperty.Register<ReaderChrome, IBrush?>(nameof(ChromeForeground));

    public IBrush? ChromeForeground
    {
        get => GetValue(ChromeForegroundProperty);
        set => SetValue(ChromeForegroundProperty, value);
    }

    public static readonly StyledProperty<ICommand?> TocCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(TocCommand));

    public ICommand? TocCommand
    {
        get => GetValue(TocCommandProperty);
        set => SetValue(TocCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> SearchCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(SearchCommand));

    public ICommand? SearchCommand
    {
        get => GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> BookmarksCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(BookmarksCommand));

    public ICommand? BookmarksCommand
    {
        get => GetValue(BookmarksCommandProperty);
        set => SetValue(BookmarksCommandProperty, value);
    }

    public static readonly StyledProperty<bool> IsBookmarkedProperty =
        AvaloniaProperty.Register<ReaderChrome, bool>(nameof(IsBookmarked));

    public bool IsBookmarked
    {
        get => GetValue(IsBookmarkedProperty);
        set => SetValue(IsBookmarkedProperty, value);
    }

    public static readonly StyledProperty<ICommand?> HighlightsCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(HighlightsCommand));

    public ICommand? HighlightsCommand
    {
        get => GetValue(HighlightsCommandProperty);
        set => SetValue(HighlightsCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> ExportCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(ExportCommand));

    public ICommand? ExportCommand
    {
        get => GetValue(ExportCommandProperty);
        set => SetValue(ExportCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> CapturesCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(CapturesCommand));

    public ICommand? CapturesCommand
    {
        get => GetValue(CapturesCommandProperty);
        set => SetValue(CapturesCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> CaptureToggleCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(CaptureToggleCommand));

    public ICommand? CaptureToggleCommand
    {
        get => GetValue(CaptureToggleCommandProperty);
        set => SetValue(CaptureToggleCommandProperty, value);
    }

    public static readonly StyledProperty<bool> IsCaptureModeProperty =
        AvaloniaProperty.Register<ReaderChrome, bool>(nameof(IsCaptureMode));

    public bool IsCaptureMode
    {
        get => GetValue(IsCaptureModeProperty);
        set => SetValue(IsCaptureModeProperty, value);
    }

    public static readonly StyledProperty<ICommand?> FontThemeCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(FontThemeCommand));

    public ICommand? FontThemeCommand
    {
        get => GetValue(FontThemeCommandProperty);
        set => SetValue(FontThemeCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(CloseCommand));

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> PreviousCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(PreviousCommand));

    public ICommand? PreviousCommand
    {
        get => GetValue(PreviousCommandProperty);
        set => SetValue(PreviousCommandProperty, value);
    }

    public static readonly StyledProperty<ICommand?> NextCommandProperty =
        AvaloniaProperty.Register<ReaderChrome, ICommand?>(nameof(NextCommand));

    public ICommand? NextCommand
    {
        get => GetValue(NextCommandProperty);
        set => SetValue(NextCommandProperty, value);
    }

    /// <summary>EPUB binds this to <c>CanGoPrevious</c> (its chapter-history stack); PDF leaves it unbound (always true - matches PDF's pre-redesign Previous button, which had no disabled state).</summary>
    public static readonly StyledProperty<bool> IsPreviousEnabledProperty =
        AvaloniaProperty.Register<ReaderChrome, bool>(nameof(IsPreviousEnabled), defaultValue: true);

    public bool IsPreviousEnabled
    {
        get => GetValue(IsPreviousEnabledProperty);
        set => SetValue(IsPreviousEnabledProperty, value);
    }

    public static readonly StyledProperty<double> ProgressFractionProperty =
        AvaloniaProperty.Register<ReaderChrome, double>(nameof(ProgressFraction));

    public double ProgressFraction
    {
        get => GetValue(ProgressFractionProperty);
        set => SetValue(ProgressFractionProperty, value);
    }

    public static readonly StyledProperty<string?> ProgressLabelProperty =
        AvaloniaProperty.Register<ReaderChrome, string?>(nameof(ProgressLabel));

    public string? ProgressLabel
    {
        get => GetValue(ProgressLabelProperty);
        set => SetValue(ProgressLabelProperty, value);
    }

    /// <summary>Fires on every Previous-button click, whether or not <see cref="PreviousCommand"/> is bound - lets a host (EPUB) intercept the click for its own WebView-scroll-first logic instead of relying purely on the command.</summary>
    public event EventHandler? PreviousRequested;

    /// <summary>See <see cref="PreviousRequested"/>.</summary>
    public event EventHandler? NextRequested;

    /// <summary>internal, not private: lets <c>ReaderChromeTests</c> exercise the click-vs-command split directly (see that test class's own doc comment for why - a real headless Window is unreliable at full-suite scale in this environment).</summary>
    internal void OnPreviousButtonClick(object? sender, RoutedEventArgs e)
    {
        PreviousRequested?.Invoke(this, EventArgs.Empty);
        if (PreviousCommand?.CanExecute(null) == true)
        {
            PreviousCommand.Execute(null);
        }
    }

    internal void OnNextButtonClick(object? sender, RoutedEventArgs e)
    {
        NextRequested?.Invoke(this, EventArgs.Empty);
        if (NextCommand?.CanExecute(null) == true)
        {
            NextCommand.Execute(null);
        }
    }
}
