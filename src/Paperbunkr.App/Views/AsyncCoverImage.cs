using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// Attached property that loads a cover thumbnail into an <see cref="Image"/> without ever
/// decoding the JPEG on the UI thread. Replaces <see cref="CoverImageConverter"/> on the
/// virtualized Library grids.
///
/// The converter did <c>new Bitmap(path)</c> synchronously inside the binding/layout pass, so
/// every row newly realized during a scroll decoded N JPEGs on the UI thread - a visible stutter
/// on a large library (the in-memory <see cref="CoverImageCache"/> only spares you covers you have
/// already scrolled past once). Here:
/// <list type="bullet">
///   <item>a cache hit still sets <see cref="Image.Source"/> synchronously - no flicker, no fade,
///   identical to the converter for already-seen covers;</item>
///   <item>a miss sets <see cref="Image.Source"/> to null (the card's own <c>CoverBrush</c> border
///   shows through) and kicks a threadpool decode that posts the <c>Bitmap</c> back when done.</item>
/// </list>
///
/// Keyed by a <see cref="CoverFingerprint.Stem"/> string, not a bare issue id (docs/superpowers/
/// specs/2026-08-27-cover-thumbnail-identity-validation-design.md and docs/superpowers/specs/
/// 2026-08-30-cover-thumbnail-content-verification-design.md) - callers bind a card's
/// <c>CoverKey</c>, computed from the entity's current file identity, so a library rebuild that
/// reassigns ids can't serve this control a stale cover for a reused id.
///
/// Virtualization still drives everything: the property is only bound on realized containers, and
/// <c>VirtualizingWrapPanel</c>'s recycle clears the binding (<c>SourceId</c> -&gt; null -&gt;
/// <see cref="Image.Source"/> null), releasing the reference exactly as the converter path did. A
/// per-<see cref="Image"/> generation token means a slow decode that finishes after its container
/// was recycled to a different issue is dropped, not painted as a stale cover.
/// </summary>
public sealed class AsyncCoverImage
{
    private AsyncCoverImage()
    {
    }

    public static readonly AttachedProperty<string?> SourceIdProperty =
        AvaloniaProperty.RegisterAttached<AsyncCoverImage, Image, string?>("SourceId");

    /// <summary>Per-<see cref="Image"/> monotonic token, bumped on every <see cref="SourceIdProperty"/>
    /// change so a decode completing after a recycle can tell it is stale.</summary>
    private static readonly AttachedProperty<long> GenerationProperty =
        AvaloniaProperty.RegisterAttached<AsyncCoverImage, Image, long>("Generation");

    /// <summary>One decode per cover stem even when several recycled containers ask at once.</summary>
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> s_inflight = new();

    static AsyncCoverImage()
    {
        SourceIdProperty.Changed.AddClassHandler<Image>(OnSourceIdChanged);
    }

    public static void SetSourceId(Image target, string? value) => target.SetValue(SourceIdProperty, value);

    public static string? GetSourceId(Image target) => target.GetValue(SourceIdProperty);

    private static void OnSourceIdChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        long generation = image.GetValue(GenerationProperty) + 1;
        image.SetValue(GenerationProperty, generation);

        if (e.NewValue is not string stem)
        {
            image.Source = null;
            return;
        }

        if (CoverImageCache.TryGetCached(stem, out var cached))
        {
            // A cache hit is always instant - no fade, matching CE (FadeInThumbnails only gates a
            // genuine new load, below). Clear any transition a prior fade attached to this recycled
            // Image so this Opacity assignment doesn't itself animate.
            image.Transitions = null;
            image.Opacity = 1;
            image.Source = cached;
            if (cached is not null && CoverFingerprint.TryGetId(stem, out int cachedId))
            {
                CoverAspectRatioStore.Report(cachedId, cached.PixelSize.Width, cached.PixelSize.Height);
            }

            return;
        }

        // Clear whatever cover the recycled container was showing, then decode off-thread.
        image.Source = null;

        var decode = s_inflight.GetOrAdd(stem, static key => Task.Run(() => CoverImageCache.DecodeFromDisk(key)));
        decode.ContinueWith(
            t =>
            {
                s_inflight.TryRemove(stem, out _);
                var result = t.IsCompletedSuccessfully ? t.Result : null;
                Dispatcher.UIThread.Post(() => Apply(image, stem, generation, result));
            },
            TaskScheduler.Default);
    }

    /// <summary>One-shot 0→1 opacity fade (docs/superpowers/specs/2026-09-13-preferences-cosmetic-
    /// toggles-design.md) - CE's <c>FadeInThumbnails</c>, ~120ms matching this codebase's existing
    /// <c>CheckBox.tileSelect</c> hover-fade idiom (<c>LibraryScreen.axaml</c>). Shared instance since
    /// <see cref="Transitions"/> only needs its property/duration/easing set once per <see cref="Image"/>.</summary>
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(120);

    private static Transitions BuildFadeTransitions() => new()
    {
        new DoubleTransition { Property = Visual.OpacityProperty, Duration = FadeDuration, Easing = new CubicEaseOut() },
    };

    /// <summary>Paints <paramref name="decoded"/> onto <paramref name="image"/> unless its container
    /// has since been recycled to a different issue (<paramref name="generation"/> stale) or the
    /// decode came back empty. Internal for direct testing of the generation guard.</summary>
    internal static void Apply(Image image, string stem, long generation, Bitmap? decoded)
    {
        if (image.GetValue(GenerationProperty) != generation || decoded is null)
        {
            return;
        }

        var source = CoverImageCache.StoreIfAbsent(stem, decoded);

        // CE only fades a genuine first load, never a cache-hit repaint (OnSourceIdChanged's own
        // cache-hit branch above never calls this method). Attach the transition once, before the
        // 0->1 flip, so the transition system actually animates the change rather than snapping.
        if (CosmeticThumbnailSettings.FadeInThumbnails)
        {
            image.Transitions ??= BuildFadeTransitions();
            image.Opacity = 0;
            image.Source = source;
            image.Opacity = 1;
        }
        else
        {
            image.Source = source;
            image.Opacity = 1;
        }

        if (CoverFingerprint.TryGetId(stem, out int decodedId))
        {
            CoverAspectRatioStore.Report(decodedId, decoded.PixelSize.Width, decoded.PixelSize.Height);
        }
    }
}
