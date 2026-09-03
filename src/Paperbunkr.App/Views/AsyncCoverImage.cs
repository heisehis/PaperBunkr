using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// Attached property that loads an <see cref="Paperbunkr.Data.Entities.Issue"/>'s cover thumbnail
/// into an <see cref="Image"/> without ever decoding the JPEG on the UI thread. Replaces
/// <see cref="CoverImageConverter"/> on the virtualized Library grids.
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

    public static readonly AttachedProperty<int?> SourceIdProperty =
        AvaloniaProperty.RegisterAttached<AsyncCoverImage, Image, int?>("SourceId");

    /// <summary>Per-<see cref="Image"/> monotonic token, bumped on every <see cref="SourceIdProperty"/>
    /// change so a decode completing after a recycle can tell it is stale.</summary>
    private static readonly AttachedProperty<long> GenerationProperty =
        AvaloniaProperty.RegisterAttached<AsyncCoverImage, Image, long>("Generation");

    /// <summary>One decode per cover id even when several recycled containers ask at once.</summary>
    private static readonly ConcurrentDictionary<int, Task<Bitmap?>> s_inflight = new();

    static AsyncCoverImage()
    {
        SourceIdProperty.Changed.AddClassHandler<Image>(OnSourceIdChanged);
    }

    public static void SetSourceId(Image target, int? value) => target.SetValue(SourceIdProperty, value);

    public static int? GetSourceId(Image target) => target.GetValue(SourceIdProperty);

    private static void OnSourceIdChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        long generation = image.GetValue(GenerationProperty) + 1;
        image.SetValue(GenerationProperty, generation);

        if (e.NewValue is not int issueId)
        {
            image.Source = null;
            return;
        }

        if (CoverImageCache.TryGetCached(issueId, out var cached))
        {
            image.Source = cached;
            if (cached is not null)
            {
                CoverAspectRatioStore.Report(issueId, cached.PixelSize.Width, cached.PixelSize.Height);
            }

            return;
        }

        // Clear whatever cover the recycled container was showing, then decode off-thread.
        image.Source = null;

        var decode = s_inflight.GetOrAdd(issueId, static id => Task.Run(() => CoverImageCache.DecodeFromDisk(id)));
        decode.ContinueWith(
            t =>
            {
                s_inflight.TryRemove(issueId, out _);
                var result = t.IsCompletedSuccessfully ? t.Result : null;
                Dispatcher.UIThread.Post(() => Apply(image, issueId, generation, result));
            },
            TaskScheduler.Default);
    }

    /// <summary>Paints <paramref name="decoded"/> onto <paramref name="image"/> unless its container
    /// has since been recycled to a different issue (<paramref name="generation"/> stale) or the
    /// decode came back empty. Internal for direct testing of the generation guard.</summary>
    internal static void Apply(Image image, int issueId, long generation, Bitmap? decoded)
    {
        if (image.GetValue(GenerationProperty) != generation || decoded is null)
        {
            return;
        }

        image.Source = CoverImageCache.StoreIfAbsent(issueId, decoded);
        CoverAspectRatioStore.Report(issueId, decoded.PixelSize.Width, decoded.PixelSize.Height);
    }
}
