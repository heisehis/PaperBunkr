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

    /// <summary>
    /// Card-cover width in device-independent pixels. When set (> 0) the cover goes through the Library grid pipeline
    /// (docs/superpowers/specs/2026-09-19-library-scroll-smoothness-design.md §3): a display-size bitmap from
    /// <see cref="GridCoverCache"/>, decoded by the newest-first <see cref="CoverDecodeQueue"/>, and the queued request is
    /// dequeued if this <see cref="Image"/> is recycled to another cover first. Left at 0 (every other screen) the original
    /// full-size shared-cache path runs unchanged. Set it <b>before</b> <see cref="SourceIdProperty"/> in XAML; if the order is
    /// reversed the change of width simply re-resolves the cover.
    /// </summary>
    public static readonly AttachedProperty<double> DecodeWidthProperty =
        AvaloniaProperty.RegisterAttached<AsyncCoverImage, Image, double>("DecodeWidth");

    /// <summary>
    /// Literal (not a binding) switch that puts an <see cref="Image"/> on the grid pipeline. It is applied when the element is
    /// created, i.e. <b>before</b> the <see cref="SourceIdProperty"/> and <see cref="DecodeWidthProperty"/> bindings resolve, in
    /// whatever order they do. Without it a cover bound before its width arrived took the legacy full-size path first, wasting
    /// one decode per cover (measured: decodes started = 2x covers, half of them wasted). A grid-mode image with no width yet
    /// simply waits; the width arriving re-resolves the cover.
    /// </summary>
    public static readonly AttachedProperty<bool> GridModeProperty =
        AvaloniaProperty.RegisterAttached<AsyncCoverImage, Image, bool>("GridMode");

    /// <summary>The outstanding <see cref="CoverDecodeQueue"/> request for this <see cref="Image"/>, cancelled when it is recycled.</summary>
    private static readonly AttachedProperty<CoverDecodeQueue.Ticket?> TicketProperty =
        AvaloniaProperty.RegisterAttached<AsyncCoverImage, Image, CoverDecodeQueue.Ticket?>("Ticket");

    /// <summary>Last render scaling seen on an attached top level; used for images not attached yet when their cover is first bound.</summary>
    private static double s_lastRenderScaling = 1.0;

    /// <summary>Records the current window's render scaling so bucket selection is right even for a container bound before it is attached.</summary>
    public static void NoteRenderScaling(double? scaling)
    {
        if (scaling is > 0)
        {
            s_lastRenderScaling = scaling.Value;
        }
    }

    static AsyncCoverImage()
    {
        SourceIdProperty.Changed.AddClassHandler<Image>((image, e) => Refresh(image, e.NewValue as string));
        DecodeWidthProperty.Changed.AddClassHandler<Image>((image, _) =>
        {
            if (GetSourceId(image) is { } stem)
            {
                Refresh(image, stem);
            }
        });
    }

    public static void SetGridMode(Image target, bool value) => target.SetValue(GridModeProperty, value);

    public static bool GetGridMode(Image target) => target.GetValue(GridModeProperty);

    public static void SetDecodeWidth(Image target, double value) => target.SetValue(DecodeWidthProperty, value);

    public static double GetDecodeWidth(Image target) => target.GetValue(DecodeWidthProperty);

    public static void SetSourceId(Image target, string? value) => target.SetValue(SourceIdProperty, value);

    public static string? GetSourceId(Image target) => target.GetValue(SourceIdProperty);

    private static void Refresh(Image image, string? newStem)
    {
        long generation = image.GetValue(GenerationProperty) + 1;
        image.SetValue(GenerationProperty, generation);

        // A recycled container no longer wants whatever it had queued: take it out of the decode queue.
        if (image.GetValue(TicketProperty) is { } oldTicket)
        {
            oldTicket.Cancel();
            image.SetValue(TicketProperty, null);
        }

        if (newStem is not string stem)
        {
            image.Source = null;
            return;
        }

        double decodeWidth = GetDecodeWidth(image);
        if (!CoverPipelineStats.ForceLegacyCoverPath && (GetGridMode(image) || decodeWidth > 0))
        {
            if (decodeWidth > 0)
            {
                RefreshFromGridCache(image, stem, generation, decodeWidth);
            }
            else
            {
                // Grid mode but the width binding has not landed yet: show the placeholder; the width re-resolves the cover.
                image.Source = null;
            }

            return;
        }

        if (CoverImageCache.TryGetCached(stem, out var cached))
        {
            CoverPipelineStats.CacheHit();
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
        CoverPipelineStats.CacheMiss();

        var decode = s_inflight.GetOrAdd(stem, static key => Task.Run(() =>
        {
            CoverPipelineStats.DecodeStarted();
            return CoverImageCache.DecodeFromDisk(key);
        }));
        decode.ContinueWith(
            t =>
            {
                s_inflight.TryRemove(stem, out _);
                var result = t.IsCompletedSuccessfully ? t.Result : null;
                Dispatcher.UIThread.Post(() => Apply(image, stem, generation, result));
            },
            TaskScheduler.Default);
    }

    private static void RefreshFromGridCache(Image image, string stem, long generation, double decodeWidth)
    {
        double scaling = TopLevel.GetTopLevel(image)?.RenderScaling ?? s_lastRenderScaling;
        int bucket = GridCoverCache.BucketFor(decodeWidth, scaling);

        if (GridCoverCache.Shared.TryGet(stem, bucket, out var cached))
        {
            CoverPipelineStats.CacheHit();
            image.Transitions = null;
            image.Opacity = 1;
            image.Source = cached;
            if (cached is not null && CoverFingerprint.TryGetId(stem, out int cachedId))
            {
                CoverAspectRatioStore.Report(cachedId, cached.PixelSize.Width, cached.PixelSize.Height);
            }

            return;
        }

        image.Source = null;
        CoverPipelineStats.CacheMiss();

        CoverPipelineStats.GridRequested();
        var ticket = CoverDecodeQueue.Shared.Request(
            stem,
            bucket,
            CoverDecodeQueue.Priority.Visible,
            decoded => Dispatcher.UIThread.Post(() => ApplyGrid(image, stem, generation, decoded)));
        image.SetValue(TicketProperty, ticket);
    }

    /// <summary>Paints a finished grid-pipeline decode (already stored in <see cref="GridCoverCache"/> by the worker), unless the container was recycled meanwhile.</summary>
    internal static void ApplyGrid(Image image, string stem, long generation, Bitmap? decoded)
    {
        if (image.GetValue(GenerationProperty) != generation)
        {
            CoverPipelineStats.DecodeWasted();
            CoverPipelineStats.WastedBecauseStale();
            return;
        }

        if (decoded is null)
        {
            CoverPipelineStats.DecodeWasted();
            CoverPipelineStats.WastedBecauseEmpty();
            return;
        }

        CoverPipelineStats.DecodeApplied();
        image.SetValue(TicketProperty, null);
        PaintNewlyDecoded(image, stem, decoded);
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
            CoverPipelineStats.DecodeWasted();
            return;
        }

        CoverPipelineStats.DecodeApplied();

        var source = CoverImageCache.StoreIfAbsent(stem, decoded);
        PaintNewlyDecoded(image, stem, source);
    }

    private static void PaintNewlyDecoded(Image image, string stem, Bitmap source)
    {
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
            CoverAspectRatioStore.Report(decodedId, source.PixelSize.Width, source.PixelSize.Height);
        }
    }
}
