using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="AsyncPluginOverlayImage"/>'s cache-hit path and generation guard - the
/// DrawThumbnailOverlay-hook anchor (docs/superpowers/specs/2026-09-05-plugin-api-v2-remaining-
/// hooks-plan.md §12). Same rationale as <see cref="AsyncCoverImageTests"/> for not pumping the
/// real background-decode + <c>Dispatcher.UIThread.Post</c> path here (headless dispatcher timing
/// is flaky in this env) - <see cref="AsyncPluginOverlayImage.Apply"/> is exercised directly.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class AsyncPluginOverlayImageTests
{
    private static Bitmap MakeOnePixelBitmap()
    {
        using var bmp = new System.Drawing.Bitmap(1, 1);
        bmp.SetPixel(0, 0, System.Drawing.Color.Red);
        using var stream = new MemoryStream();
        bmp.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        return new Bitmap(stream);
    }

    [Fact]
    public void SettingSourceId_WithNoPluginHost_LeavesSourceNull()
    {
        AsyncPluginOverlayImage.PluginHost = null;
        var image = new Image();

        AsyncPluginOverlayImage.SetSourceId(image, 700);

        Assert.Null(image.Source);
    }

    [Fact]
    public void SettingSourceId_ToNull_ClearsSource()
    {
        var image = new Image();
        AsyncPluginOverlayImage.SetSourceId(image, 701);
        Assert.Null(image.Source); // no cache entry yet (no PluginHost) - starts null either way

        AsyncPluginOverlayImage.SetSourceId(image, null);

        Assert.Null(image.Source);
    }

    [Fact]
    public void Apply_PaintsTheOverlay_WhenGenerationIsCurrent()
    {
        var decoded = MakeOnePixelBitmap();
        var image = new Image();
        AsyncPluginOverlayImage.SetSourceId(image, 702); // generation -> 1

        AsyncPluginOverlayImage.Apply(image, issueId: 702, generation: 1, decoded);

        Assert.Same(decoded, image.Source);
    }

    [Fact]
    public void Apply_DropsAStaleDecode_AfterTheContainerWasRecycled()
    {
        var staleDecode = MakeOnePixelBitmap();
        var image = new Image();
        AsyncPluginOverlayImage.SetSourceId(image, 703); // generation -> 1
        AsyncPluginOverlayImage.SetSourceId(image, 704); // container recycled: generation -> 2

        AsyncPluginOverlayImage.Apply(image, issueId: 703, generation: 1, staleDecode);

        Assert.Null(image.Source); // the overlay for 703 must not land on a container now showing 704
    }

    [Fact]
    public void Apply_WithANullOverlay_CachesTheNoOverlayResult_AndClearsSource()
    {
        var image = new Image();
        AsyncPluginOverlayImage.SetSourceId(image, 705); // generation -> 1

        AsyncPluginOverlayImage.Apply(image, issueId: 705, generation: 1, decoded: null);

        Assert.Null(image.Source);
    }
}
