using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Views;

/// <summary>
/// Attached property that loads an external-provider cover thumbnail into an <see cref="Image"/>
/// by URL (the "From External Provider" cover-picker tab, docs/superpowers/specs/2026-09-18-
/// external-metadata-full-extraction-design.md §2) - a small, non-virtualized picker grid, unlike
/// <see cref="AsyncCoverImage"/>'s library-scale generation-token/fade machinery, so a plain
/// fire-and-forget fetch-and-set is enough here.
/// </summary>
public sealed class RemoteThumbnailImage
{
    private RemoteThumbnailImage()
    {
    }

    public static readonly AttachedProperty<string?> SourceUrlProperty =
        AvaloniaProperty.RegisterAttached<RemoteThumbnailImage, Image, string?>("SourceUrl");

    static RemoteThumbnailImage()
    {
        SourceUrlProperty.Changed.AddClassHandler<Image>(OnSourceUrlChanged);
    }

    public static void SetSourceUrl(Image target, string? value) => target.SetValue(SourceUrlProperty, value);

    public static string? GetSourceUrl(Image target) => target.GetValue(SourceUrlProperty);

    private static void OnSourceUrlChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        image.Source = null;
        if (e.NewValue is not string url || string.IsNullOrEmpty(url))
        {
            return;
        }

        _ = LoadAsync(image, url);
    }

    private static async System.Threading.Tasks.Task LoadAsync(Image image, string url)
    {
        var bitmap = await ProviderCoverCandidateCache.FetchAsync(url, System.Threading.CancellationToken.None);
        if (bitmap is null || GetSourceUrl(image) != url)
        {
            return; // stale by the time the fetch completed, or the fetch failed
        }

        Dispatcher.UIThread.Post(() => image.Source = bitmap);
    }
}
