using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Controls;

/// <summary>The four dialog widths (docs/superpowers/specs/2026-10-19-ui-polish-dialogs-design.md, Q3).</summary>
public enum DialogSize
{
    Small,
    Medium,
    Large,
    XLarge,
}

/// <summary>
/// The one dialog anatomy (docs/superpowers/specs/2026-10-19-ui-polish-dialogs-design.md): a card with a display title and optional subtitle,
/// the close button inside the card, a scrolling body and a footer with a divider (Cancel + primary on the right, Back/secondary on the
/// left). It caps its own height at the window height minus 48 so a tall dialog scrolls instead of running off screen, and inside an
/// <see cref="OverlayShell"/> it takes over the shell's close button so there is only ever one, on the card.
/// Template: the implicit ControlTheme in Styles/Overlays.axaml.
/// </summary>
public class DialogCard : ContentControl
{
    /// <summary>Space kept between a dialog and the window's top and bottom edges.</summary>
    public const double WindowMargin = 48;

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<DialogCard, string?>(nameof(Subtitle));

    public static readonly StyledProperty<DialogSize> SizeProperty =
        AvaloniaProperty.Register<DialogCard, DialogSize>(nameof(Size), DialogSize.Medium);

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<DialogCard, object?>(nameof(Footer));

    public static readonly StyledProperty<object?> FooterStartProperty =
        AvaloniaProperty.Register<DialogCard, object?>(nameof(FooterStart));

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<DialogCard, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<bool> IsBodyScrollableProperty =
        AvaloniaProperty.Register<DialogCard, bool>(nameof(IsBodyScrollable), defaultValue: true);

    public static readonly DirectProperty<DialogCard, bool> HasFooterProperty =
        AvaloniaProperty.RegisterDirect<DialogCard, bool>(nameof(HasFooter), o => o.HasFooter);

    public static readonly DirectProperty<DialogCard, ICommand?> EffectiveCloseCommandProperty =
        AvaloniaProperty.RegisterDirect<DialogCard, ICommand?>(nameof(EffectiveCloseCommand), o => o.EffectiveCloseCommand);

    private IDisposable? _sizeSubscription;
    private OverlayShell? _shell;
    private bool _hasFooter;
    private bool _shellShowedClose;
    private ICommand? _effectiveCloseCommand;

    static DialogCard()
    {
        SizeProperty.Changed.AddClassHandler<DialogCard>((card, _) => card.ApplySize());
        FooterProperty.Changed.AddClassHandler<DialogCard>((card, _) => card.UpdateFooter());
        FooterStartProperty.Changed.AddClassHandler<DialogCard>((card, _) => card.UpdateFooter());
        CloseCommandProperty.Changed.AddClassHandler<DialogCard>((card, _) => card.UpdateCloseCommand());
    }

    public DialogCard() => ApplySize();

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }

    public DialogSize Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    public object? FooterStart { get => GetValue(FooterStartProperty); set => SetValue(FooterStartProperty, value); }

    /// <summary>Runs from the card's ✕. When unset, the enclosing <see cref="OverlayShell"/>'s CloseCommand is used.</summary>
    public ICommand? CloseCommand { get => GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }

    /// <summary>False for a dialog whose body hosts its own scroller (a gallery, a compare table).</summary>
    public bool IsBodyScrollable { get => GetValue(IsBodyScrollableProperty); set => SetValue(IsBodyScrollableProperty, value); }

    public bool HasFooter { get => _hasFooter; private set => SetAndRaise(HasFooterProperty, ref _hasFooter, value); }

    /// <summary>What the ✕ runs: <see cref="CloseCommand"/>, else the shell's. Null hides the ✕.</summary>
    public ICommand? EffectiveCloseCommand { get => _effectiveCloseCommand; private set => SetAndRaise(EffectiveCloseCommandProperty, ref _effectiveCloseCommand, value); }

    public static double WidthFor(DialogSize size) => size switch
    {
        DialogSize.Small => 420,
        DialogSize.Large => 760,
        DialogSize.XLarge => 960,
        _ => 560,
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (TopLevel.GetTopLevel(this) is { } topLevel)
        {
            _sizeSubscription = topLevel.GetObservable(TopLevel.ClientSizeProperty)
                .Subscribe(new SizeObserver(size => MaxHeight = Math.Max(160, size.Height - WindowMargin)));
        }

        _shell = this.FindAncestorOfType<OverlayShell>();
        if (_shell is not null)
        {
            _shellShowedClose = _shell.ShowCloseButton;
            _shell.SetCurrentValue(OverlayShell.ShowCloseButtonProperty, false);
        }

        UpdateCloseCommand();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _sizeSubscription?.Dispose();
        _sizeSubscription = null;
        _shell?.SetCurrentValue(OverlayShell.ShowCloseButtonProperty, _shellShowedClose);
        _shell = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>The card is its size's width, or narrower when its host is. A fixed Width overflowed a smaller host (a plugin's
    /// result window, a narrow overlay): centred, it was clipped on both sides.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = Math.Min(WidthFor(Size), availableSize.Width);
        Size desired = base.MeasureOverride(availableSize.WithWidth(width));
        return new Size(width, desired.Height);
    }

    private void ApplySize() => InvalidateMeasure();

    private void UpdateFooter() => HasFooter = Footer is not null || FooterStart is not null;

    private void UpdateCloseCommand() => EffectiveCloseCommand = CloseCommand ?? _shell?.CloseCommand;

    private sealed class SizeObserver(Action<Size> onNext) : IObserver<Size>
    {
        public void OnCompleted() { }

        public void OnError(Exception error) { }

        public void OnNext(Size value) => onNext(value);
    }
}

/// <summary>Converters for the DialogCard template.</summary>
public static class DialogCardConverters
{
    public static readonly Avalonia.Data.Converters.IValueConverter ScrollBar =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Controls.Primitives.ScrollBarVisibility>(
            scrollable => scrollable ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
}
