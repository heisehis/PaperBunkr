using System;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services.Reader;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The pinned reference page (docs/superpowers/specs/2026-09-26-comic-reader-inreader-reference-design.md #29): one page kept as a small floating panel while reading on. The pin is a scaled copy of the page as it was
/// displayed, taken when it is pinned, so the pipeline's eviction, later crop or levels changes and turning pages cannot affect it. It survives moving to another issue during the visit (the recap-page use case) and is
/// cleared by unpinning or by leaving the reader. Not persisted: a reading aid, not data.
/// </summary>
public partial class ReaderScreenViewModel
{
    /// <summary>The scaled copy of the pinned page, or null when nothing is pinned. Never disposed by the view model: the render thread may still hold it (the reader's rule for bitmaps).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPin), nameof(IsPinShown))]
    private Bitmap? _pinnedImage;

    [ObservableProperty]
    private string? _pinnedCaption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinWidth))]
    private PinSize _pinSize = PinSize.Medium;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinHorizontalAlignment), nameof(PinVerticalAlignment), nameof(PinMargin))]
    private PinCorner _pinCorner = PinCorner.TopRight;

    public bool HasPin => PinnedImage is not null;

    /// <summary>The pin is showing: it is hidden while the info panel is open (both want the same side of the page).</summary>
    public bool IsPinShown => HasPin && !Info.IsOpen;

    public double PinWidth => ReaderPinMath.WidthFor(PinSize);

    public HorizontalAlignment PinHorizontalAlignment => PinCorner is PinCorner.TopLeft or PinCorner.BottomLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;

    public VerticalAlignment PinVerticalAlignment => PinCorner is PinCorner.TopLeft or PinCorner.TopRight ? VerticalAlignment.Top : VerticalAlignment.Bottom;

    public Thickness PinMargin => ReaderPinMath.MarginFor(PinCorner);

    /// <summary>The pin's own gesture, for tooltips.</summary>
    [ObservableProperty]
    private System.Collections.Generic.IReadOnlyList<Avalonia.Input.KeyGesture> _pinPageKey = [new(Avalonia.Input.Key.P, Avalonia.Input.KeyModifiers.Shift)];

    private void HookPinToInfoPanel() => Info.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(ReaderInfoPanelViewModel.IsOpen))
        {
            OnPropertyChanged(nameof(IsPinShown));
        }
    };

    /// <summary>Pins the page on screen (replacing any earlier pin): a copy at most <see cref="ReaderPinMath.CopyLongSide"/> pixels long.</summary>
    [RelayCommand]
    private void PinCurrentPage()
    {
        if (_decoder is null || _loadedIssueId is null)
        {
            return;
        }

        try
        {
            var source = _decoder.GetPage(_currentPageIndex);
            var size = ReaderPinMath.CopySize(source.PixelSize);
            // A copy of its own (also when the page is already small): the pipeline may drop its bitmap at any time.
            PinnedImage = source.CreateScaledBitmap(size, BitmapInterpolationMode.HighQuality);
            string series = Info.Model?.SeriesLine ?? "Page";
            PinnedCaption = $"{series} · page {_currentPageIndex + 1}";
            ToastRequested?.Invoke(new ToastRequest("Page pinned", "It stays in the corner while you read. Click its X to unpin."));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or ArgumentException)
        {
            ToastRequested?.Invoke(new ToastRequest("Couldn't pin this page", "The page isn't ready yet - try again in a moment.", ToastSeverity.Warning));
        }
    }

    [RelayCommand]
    private void UnpinPage()
    {
        PinnedImage = null;   // a dropped reference, never a Dispose
        PinnedCaption = null;
    }

    /// <summary>Palette and the pin's size button: small, medium, large, small.</summary>
    [RelayCommand]
    private void CyclePinSize() => PinSize = ReaderPinMath.Next(PinSize);

    /// <summary>The pin was dragged and released at <paramref name="corner"/>.</summary>
    internal void SetPinCorner(PinCorner corner) => PinCorner = corner;

    /// <summary>The mouse wheel over the pin: a step bigger or smaller.</summary>
    internal void StepPinSize(bool up) => PinSize = ReaderPinMath.Step(PinSize, up);

    /// <summary>Leaving the reader ends the visit's pin (called from <c>GoBack</c>).</summary>
    private void ClearPin()
    {
        PinnedImage = null;
        PinnedCaption = null;
    }
}
