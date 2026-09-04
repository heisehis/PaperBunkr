using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.VisualTree;

namespace Paperbunkr.App.Services;

/// <summary>
/// Default <see cref="ISharedElementTransitionService"/>. <see cref="Shared"/> is the single
/// app-wide instance the <see cref="Paperbunkr.App.Controls.SharedElement"/> attached properties
/// register against - same "Shared is only the default, constructor-inject the interface where
/// testability matters" convention as <see cref="MetadataEditHistoryService"/>.
/// </summary>
public sealed class SharedElementTransitionService : ISharedElementTransitionService
{
    public static readonly SharedElementTransitionService Shared = new();

    private static readonly TimeSpan PollBudget = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(16);

    private readonly Dictionary<string, (Visual Element, Func<IImage?> ImageAccessor)> _registrations = new();

    private Canvas? _overlayHost;
    private PendingCapture? _pending;
    private Border? _activeClone;
    private CancellationTokenSource? _activeCts;

    private readonly record struct PendingCapture(Rect SourceRect, CornerRadius SourceRadius, IImage? Image);

    public void RegisterOverlayHost(Canvas host) => _overlayHost = host;

    public void Register(string key, Visual element, Func<IImage?> imageAccessor) => _registrations[key] = (element, imageAccessor);

    public void Unregister(string key, Visual element)
    {
        // Only remove if this element is still the one on file for `key` - a newer registration
        // (e.g. the incoming screen's own copy already attached) must not be evicted by the
        // outgoing screen's later detach.
        if (_registrations.TryGetValue(key, out var current) && ReferenceEquals(current.Element, element))
        {
            _registrations.Remove(key);
        }
    }

    public void CaptureOutgoing(string key)
    {
        _pending = null;

        if (_overlayHost is null || !_registrations.TryGetValue(key, out var reg))
        {
            return;
        }

        if (TryGetOverlayRect(reg.Element, out var rect))
        {
            var radius = (reg.Element as Border)?.CornerRadius ?? default;
            _pending = new PendingCapture(rect, radius, reg.ImageAccessor());
        }
    }

    public async Task<bool> FlyToIncomingAsync(string key, TimeSpan duration, Easing easing, CancellationToken cancellationToken)
    {
        var capture = _pending;
        _pending = null;

        if (capture is not { } pending || _overlayHost is null || pending.Image is null)
        {
            return false;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeCts = linkedCts;

        try
        {
            var destination = await WaitForDestinationAsync(key, linkedCts.Token).ConfigureAwait(true);
            if (destination is not { } destRect)
            {
                return false;
            }

            var flight = SharedElementFlightMath.ComputeFlight(pending.SourceRect, destRect.Rect, pending.SourceRadius, destRect.Radius);
            if (flight.IsNoOp)
            {
                return false;
            }

            await RunFlightAsync(pending, flight, duration, easing, linkedCts.Token).ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            RemoveActiveClone();
            if (ReferenceEquals(_activeCts, linkedCts))
            {
                _activeCts = null;
            }
        }
    }

    public void Cancel()
    {
        _activeCts?.Cancel();
        RemoveActiveClone();
        _pending = null;
    }

    private async Task<(Rect Rect, CornerRadius Radius)?> WaitForDestinationAsync(string key, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + PollBudget;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (_registrations.TryGetValue(key, out var reg) &&
                TryGetOverlayRect(reg.Element, out var rect))
            {
                var radius = (reg.Element as Border)?.CornerRadius ?? default;
                return (rect, radius);
            }

            await Task.Delay(PollInterval, ct).ConfigureAwait(true);
        }

        return null;
    }

    private async Task RunFlightAsync(PendingCapture source, SharedElementFlight flight, TimeSpan duration, Easing easing, CancellationToken ct)
    {
        var clone = new Border
        {
            Width = source.SourceRect.Width,
            Height = source.SourceRect.Height,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ClipToBounds = true,
            CornerRadius = flight.StartRadius,
            RenderTransformOrigin = RelativePoint.TopLeft,
            RenderTransform = TransformOperations.Identity,
            Child = new Image { Source = source.Image, Stretch = Stretch.UniformToFill },
        };
        Canvas.SetLeft(clone, source.SourceRect.X);
        Canvas.SetTop(clone, source.SourceRect.Y);

        _overlayHost!.Children.Add(clone);
        _activeClone = clone;
        ct.ThrowIfCancellationRequested();

        // Transitions animate any SetValue-driven change from the property's actual old value to
        // its new one, independent of whether a frame has rendered in between - no dispatcher yield
        // needed between attaching Transitions and changing RenderTransform/CornerRadius below. Kept
        // headless-testable this way; see the design doc's standing "no unattended GUI automation"
        // caveat for confirming the eased motion itself (vs. an instant snap) on real screen.
        clone.Transitions =
        [
            new TransformOperationsTransition { Property = Border.RenderTransformProperty, Duration = duration, Easing = easing },
            new CornerRadiusTransition { Property = Border.CornerRadiusProperty, Duration = duration, Easing = easing },
        ];

        clone.RenderTransform = TransformOperations.Parse(
            $"translate({flight.TranslateX}px,{flight.TranslateY}px) scale({flight.ScaleX},{flight.ScaleY})");
        clone.CornerRadius = flight.EndRadius;

        await Task.Delay(duration, ct).ConfigureAwait(true);
    }

    private void RemoveActiveClone()
    {
        if (_activeClone is { } clone)
        {
            _overlayHost?.Children.Remove(clone);
            _activeClone = null;
        }
    }

    private bool TryGetOverlayRect(Visual element, out Rect rect)
    {
        rect = default;

        if (_overlayHost is not { } host || !element.IsAttachedToVisualTree())
        {
            return false;
        }

        var bounds = element.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        var matrix = element.TransformToVisual(host);
        if (matrix is not { } m)
        {
            return false;
        }

        rect = new Rect(bounds.Size).TransformToAABB(m);
        return true;
    }
}
