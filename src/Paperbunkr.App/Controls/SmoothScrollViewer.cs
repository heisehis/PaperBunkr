using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Controls;

/// <summary>
/// A <see cref="ScrollViewer"/> with eased mouse-wheel scrolling (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-
/// design.md §6).
///
/// Only discrete mouse-wheel notches are handled (<see cref="WheelEasingMath.IsMouseWheelNotch"/>): each adds
/// <see cref="WheelEasingMath.StockNotchPixels"/> to a target offset and the offset eases toward it every animation frame
/// (<see cref="TopLevel.RequestAnimationFrame"/>, not a timer). Touchpad and precise deltas, scrollbar dragging, keyboard
/// scrolling, modifier-wheel (zoom/horizontal) and touch (which has its own inertia) fall through to the stock handling. Off when
/// the "Smooth scrolling" preference is off or the OS asks for reduced motion, and the animation stands down the moment anything
/// else moves the offset.
///
/// Why this shape (each alternative was tried and measured with <c>Paperbunkr.ScrollHarness --wheel-probe</c>): <c>PointerWheelChanged</c>
/// is a bubble-only event, so a tunnel handler is never invoked, and the <c>ScrollContentPresenter</c> consumes it in its own class
/// handler, which runs before any override or instance handler on the presenter, the <c>ScrollViewer</c> or anything above. The only
/// place to get in first is the scroll viewer's <b>content</b>, which the notch bubbles through before it reaches the presenter, so this
/// hooks whatever <see cref="ContentControl.Content"/> currently is.
/// The eased offset changes go through the same panel realization path as any scroll, which is why this comes after the panel and
/// cover pipeline work.
/// </summary>
public class SmoothScrollViewer : ScrollViewer
{
    private readonly WheelScrollController _controller = new();
    private bool _frameRequested;
    private TimeSpan _lastFrame;

    // Keep the stock ScrollViewer control theme: without this, Avalonia looks the theme up by this subclass's own type and finds none.
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    private InputElement? _hookedContent;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ContentProperty)
        {
            _hookedContent?.RemoveHandler(PointerWheelChangedEvent, OnContentWheel);
            _hookedContent = change.NewValue as InputElement;
            _hookedContent?.AddHandler(PointerWheelChangedEvent, OnContentWheel);
        }
    }

    private void OnContentWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled
            || !SmoothScrollSettings.Enabled
            || MotionTokens.IsReducedMotion()
            || e.KeyModifiers != KeyModifiers.None
            || !WheelEasingMath.IsMouseWheelNotch(e.Delta.X, e.Delta.Y))
        {
            return; // falls through to the stock ScrollViewer handling
        }

        double maxOffset = Math.Max(0, Extent.Height - Viewport.Height);
        if (maxOffset <= 0)
        {
            return; // nothing to scroll: let the stock handling (and any parent) deal with it
        }

        _controller.AddNotches(e.Delta.Y, Offset.Y, maxOffset);
        e.Handled = true;
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (_frameRequested || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        _frameRequested = true;
        _lastFrame = TimeSpan.Zero;
        topLevel.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan frameTime)
    {
        _frameRequested = false;

        // First frame after a notch has no previous timestamp: assume one 60 Hz frame so the first step is not zero.
        double dt = _lastFrame == TimeSpan.Zero ? 1.0 / 60.0 : (frameTime - _lastFrame).TotalSeconds;
        _lastFrame = frameTime;

        double maxOffset = Math.Max(0, Extent.Height - Viewport.Height);
        if (_controller.Tick(Offset.Y, dt, maxOffset) is { } next)
        {
            Offset = new Vector(Offset.X, next);
        }

        if (_controller.IsAnimating && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            _frameRequested = true;
            topLevel.RequestAnimationFrame(OnFrame);
        }
    }
}
