using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paperbunkr.Data.ComicVine.Scraping;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Scraper;

/// <summary>
/// Attached property that loads a ComicVine cover thumbnail into an <see cref="Image"/> via
/// <see cref="RemoteCoverCache"/> (docs/superpowers/specs/2026-09-13-cluster-scraper-ui-
/// redesign-design.md §2). Deliberately simpler than the host's own <c>AsyncCoverImage</c> - the
/// candidate lists this binds against are short (a handful of search results, never virtualized), so
/// there's no recycling to guard against beyond a plain per-<see cref="Image"/> generation token for
/// "the URL changed again before the previous load finished."
/// </summary>
public static class RemoteCoverImage
{
    public static readonly AttachedProperty<string?> SourceUrlProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("SourceUrl", typeof(RemoteCoverImage));

    private static readonly AttachedProperty<long> GenerationProperty =
        AvaloniaProperty.RegisterAttached<Image, long>("Generation", typeof(RemoteCoverImage));

    static RemoteCoverImage()
    {
        SourceUrlProperty.Changed.AddClassHandler<Image>(OnSourceUrlChanged);
    }

    public static string? GetSourceUrl(Image image) => image.GetValue(SourceUrlProperty);

    public static void SetSourceUrl(Image image, string? value) => image.SetValue(SourceUrlProperty, value);

    private static void OnSourceUrlChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        long generation = image.GetValue(GenerationProperty) + 1;
        image.SetValue(GenerationProperty, generation);
        image.Source = null;

        string? url = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _ = LoadAsync(image, url, generation);
    }

    private static async Task LoadAsync(Image image, string url, long generation)
    {
        Bitmap? bitmap = await RemoteCoverCache.GetAsync(url, CancellationToken.None).ConfigureAwait(true);
        if (image.GetValue(GenerationProperty) != generation)
        {
            return; // SourceUrl changed again (or the image was recycled) while this load was in flight.
        }

        image.Source = bitmap;
    }
}
