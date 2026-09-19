using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Paperbunkr.App.Views;

/// <summary>
/// Shared left-drawer scaffold for both Books readers (docs/superpowers/specs/2026-09-03-books-
/// reader-hud-redesign-design.md) - scrim + header + optional pinned search box + a content slot for
/// the actual list. Used for TOC/Bookmarks/Highlights/Search (EPUB) and Captures (PDF). Each
/// consumer's item shape is unrelated (<c>BookChapterSummary</c>/<c>BookBookmarkSummary</c>/
/// <c>BookHighlightSummary</c>/<c>BookSearchResult</c>/<c>BookAnnotationImageSummary</c>), so only
/// this scaffold is shared - the host supplies its own <c>ScrollViewer</c>/<c>ItemsControl</c> as
/// <see cref="DrawerContent"/>, same as setting any other object-typed property.
/// </summary>
public partial class ReaderListDrawer : UserControl
{
    private readonly OverlayWindowController _overlayController;

    public ReaderListDrawer()
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
    }

    /// <summary>Belt-and-braces: if this control is ever torn out of the visual tree (screen
    /// navigated away from, reader closed) while its drawer is open, close the floating overlay
    /// window too - it's a real separate OS window now, not anchored inside BookReaderScreen's own
    /// visual tree, so it would otherwise keep floating on top of whatever screen comes next.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _overlayController.SetOpen(false);
    }

    /// <summary>The host screen's own real, content-bearing root Grid (e.g. <c>{Binding #RootGrid}</c>) - used only to size the scrim/panel correctly (see ReaderListDrawer.axaml's own comment on why this control's own Bounds isn't a reliable source).</summary>
    public static readonly StyledProperty<Layoutable?> OverlayReferenceProperty =
        AvaloniaProperty.Register<ReaderListDrawer, Layoutable?>(nameof(OverlayReference));

    public Layoutable? OverlayReference
    {
        get => GetValue(OverlayReferenceProperty);
        set => SetValue(OverlayReferenceProperty, value);
    }

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<ReaderListDrawer, bool>(nameof(IsOpen));

    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public static readonly StyledProperty<string?> HeaderTextProperty =
        AvaloniaProperty.Register<ReaderListDrawer, string?>(nameof(HeaderText));

    public string? HeaderText
    {
        get => GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public static readonly StyledProperty<double> DrawerWidthProperty =
        AvaloniaProperty.Register<ReaderListDrawer, double>(nameof(DrawerWidth), defaultValue: 300);

    public double DrawerWidth
    {
        get => GetValue(DrawerWidthProperty);
        set => SetValue(DrawerWidthProperty, value);
    }

    /// <summary>True for Search's drawer only - pins a search TextBox above <see cref="DrawerContent"/> instead of a plain static header, matching the design's "Search moves from a top dropdown to a left drawer, search box pinned at the top" decision.</summary>
    public static readonly StyledProperty<bool> HasSearchBoxProperty =
        AvaloniaProperty.Register<ReaderListDrawer, bool>(nameof(HasSearchBox));

    public bool HasSearchBox
    {
        get => GetValue(HasSearchBoxProperty);
        set => SetValue(HasSearchBoxProperty, value);
    }

    public static readonly StyledProperty<string?> SearchBoxTextProperty =
        AvaloniaProperty.Register<ReaderListDrawer, string?>(nameof(SearchBoxText), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string? SearchBoxText
    {
        get => GetValue(SearchBoxTextProperty);
        set => SetValue(SearchBoxTextProperty, value);
    }

    public static readonly StyledProperty<string?> SearchBoxWatermarkProperty =
        AvaloniaProperty.Register<ReaderListDrawer, string?>(nameof(SearchBoxWatermark));

    public string? SearchBoxWatermark
    {
        get => GetValue(SearchBoxWatermarkProperty);
        set => SetValue(SearchBoxWatermarkProperty, value);
    }

    /// <summary>The drawer's scrollable body - the host's own <c>ScrollViewer</c>/<c>ItemsControl</c> (or any other content), set the same way any other object-typed Avalonia property is set from XAML (<c>&lt;views:ReaderListDrawer.DrawerContent&gt;</c>).</summary>
    public static readonly StyledProperty<object?> DrawerContentProperty =
        AvaloniaProperty.Register<ReaderListDrawer, object?>(nameof(DrawerContent));

    public object? DrawerContent
    {
        get => GetValue(DrawerContentProperty);
        set => SetValue(DrawerContentProperty, value);
    }

    /// <summary>Fires when the dimmed backdrop behind this drawer is tapped - hosts subscribe the same shared scrim-close handler every drawer already used before this control existed (closes every open drawer/sheet, not just this one, same as before).</summary>
    public event EventHandler<PointerPressedEventArgs>? ScrimPressed;

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ScrimPressed?.Invoke(this, e);
        e.Handled = true;
    }
}
