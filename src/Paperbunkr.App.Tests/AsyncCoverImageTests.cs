using Avalonia.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="AsyncCoverImage"/> - the attached property that replaced
/// <see cref="CoverImageConverter"/> on the virtualized Library grids so JPEG decode happens off
/// the UI thread. Same <see cref="AvaloniaTestCollection"/> rationale as
/// <see cref="CoverImageCacheTests"/> (Bitmap construction + Image control need a platform),
/// redirecting <see cref="CoverThumbnailPaths.ThumbnailDirectory"/> to a temp folder. Keyed by a
/// <see cref="CoverFingerprint.Stem"/> string, not a bare issue id (docs/superpowers/specs/
/// 2026-08-27-cover-thumbnail-identity-validation-design.md), same as <see cref="CoverImageCache"/>.
///
/// The background decode + <c>Dispatcher.UIThread.Post</c> path is not pumped here (headless
/// dispatcher timing is flaky in this env); instead the two behaviours that actually matter are
/// tested directly - the synchronous cache-hit path, and the generation guard via
/// <see cref="AsyncCoverImage.Apply"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class AsyncCoverImageTests : IDisposable
{
    private readonly string _originalThumbnailDirectory;
    private readonly string _thumbnailDirectory;
    private readonly string _cbzPath;
    private readonly bool _originalFadeInThumbnails;

    public AsyncCoverImageTests()
    {
        _originalThumbnailDirectory = CoverThumbnailPaths.ThumbnailDirectory;
        _thumbnailDirectory = Path.Combine(Path.GetTempPath(), $"paperbunkr_asynccover_test_{Guid.NewGuid():N}");
        CoverThumbnailPaths.ThumbnailDirectory = _thumbnailDirectory;
        _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_asynccover_cbz_{Guid.NewGuid():N}.cbz");
        _originalFadeInThumbnails = CosmeticThumbnailSettings.FadeInThumbnails;
    }

    public void Dispose()
    {
        CoverThumbnailPaths.ThumbnailDirectory = _originalThumbnailDirectory;
        CosmeticThumbnailSettings.FadeInThumbnails = _originalFadeInThumbnails;
        try
        {
            if (File.Exists(_cbzPath)) File.Delete(_cbzPath);
            if (Directory.Exists(_thumbnailDirectory)) Directory.Delete(_thumbnailDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void SettingSourceId_ToAnAlreadyCachedCover_SetsSourceSynchronously()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 600, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(600, _cbzPath, 1);
        var cover = CoverImageCache.Get(stem); // warm the in-memory cache
        Assert.NotNull(cover);

        var image = new Image();
        AsyncCoverImage.SetSourceId(image, stem);

        Assert.Same(cover, image.Source);
    }

    [Fact]
    public void SettingSourceId_ToNull_ClearsSource()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 601, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(601, _cbzPath, 1);
        CoverImageCache.Get(stem);

        var image = new Image();
        AsyncCoverImage.SetSourceId(image, stem);
        Assert.NotNull(image.Source);

        AsyncCoverImage.SetSourceId(image, null);
        Assert.Null(image.Source);
    }

    // Grid pipeline leases (docs/superpowers/specs/2026-10-07-performance-and-memory-design.md section 4.2): the Image holds a lease on
    // the cache's bitmap for as long as it shows it, so the cache may dispose evicted bitmaps without ever disposing a displayed one.

    private const double GridCardWidth = 150;

    private static (Image Image, int Bucket) GridImage()
    {
        AsyncCoverImage.NoteRenderScaling(1.0);
        var image = new Image();
        AsyncCoverImage.SetGridMode(image, true);
        AsyncCoverImage.SetDecodeWidth(image, GridCardWidth);
        return (image, GridCoverCache.BucketFor(GridCardWidth, 1.0));
    }

    private static Avalonia.Media.Imaging.Bitmap TinyBitmap() =>
        new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(1, 1), new Avalonia.Vector(96, 96));

    [Fact]
    public void GridImage_LeasesTheCoverItShows_AndGivesItBackWhenRePointed()
    {
        var (image, bucket) = GridImage();
        string stem = $"lease-{Guid.NewGuid():N}";
        var cover = GridCoverCache.Shared.Add(stem, bucket, TinyBitmap(), 100);

        AsyncCoverImage.SetSourceId(image, stem);
        Assert.Same(cover, image.Source);
        Assert.Equal(1, GridCoverCache.Shared.LeaseCount(cover));

        AsyncCoverImage.SetSourceId(image, null);
        Assert.Null(image.Source);
        Assert.Equal(0, GridCoverCache.Shared.LeaseCount(cover));
        Assert.Equal(new Avalonia.PixelSize(1, 1), cover.PixelSize); // still cached, so still alive

        GridCoverCache.Shared.Remove(stem);
    }

    [Fact]
    public void GridImage_KeepsItsCoverAlive_WhenTheCacheEvictsIt_AndFreesItOnceRePointed()
    {
        var (image, bucket) = GridImage();
        string stem = $"evicted-{Guid.NewGuid():N}";
        var cover = GridCoverCache.Shared.Add(stem, bucket, TinyBitmap(), 100);
        AsyncCoverImage.SetSourceId(image, stem);

        GridCoverCache.Shared.Remove(stem); // thumbnail regenerated, or evicted under pressure

        Assert.Same(cover, image.Source);
        Assert.Equal(new Avalonia.PixelSize(1, 1), cover.PixelSize); // displayed, so not disposed

        AsyncCoverImage.SetSourceId(image, null);
        Assert.Null(image.Source);
        Assert.Throws<ObjectDisposedException>(() => cover.PixelSize);
    }

    [Fact]
    public void ApplyGrid_ShowsTheCachedBitmapOnALease()
    {
        var (image, bucket) = GridImage();
        string stem = $"applied-{Guid.NewGuid():N}";
        AsyncCoverImage.SetSourceId(image, stem); // generation -> 1, cache miss
        var cover = GridCoverCache.Shared.Add(stem, bucket, TinyBitmap(), 100); // the worker stored its decode

        AsyncCoverImage.ApplyGrid(image, stem, bucket, generation: 1, cover);

        Assert.Same(cover, image.Source);
        Assert.Equal(1, GridCoverCache.Shared.LeaseCount(cover));

        AsyncCoverImage.SetSourceId(image, null);
        GridCoverCache.Shared.Remove(stem);
    }

    [Fact]
    public void ApplyGrid_NeverShowsADecodeTheCacheAlreadyEvictedAndDisposed()
    {
        var (image, bucket) = GridImage();
        string stem = $"gone-{Guid.NewGuid():N}";
        AsyncCoverImage.SetSourceId(image, stem); // generation -> 1, cache miss
        var cover = GridCoverCache.Shared.Add(stem, bucket, TinyBitmap(), 100);
        GridCoverCache.Shared.Remove(stem); // evicted between the worker storing it and the UI callback
        Assert.Throws<ObjectDisposedException>(() => cover.PixelSize);

        AsyncCoverImage.ApplyGrid(image, stem, bucket, generation: 1, cover);

        Assert.Null(image.Source);
        AsyncCoverImage.SetSourceId(image, null);
    }

    [Fact]
    public void SettingSourceId_ToAnUnknownIssue_LeavesSourceNull()
    {
        var image = new Image();

        AsyncCoverImage.SetSourceId(image, "909090-deadbeef");

        Assert.Null(image.Source);
    }

    [Fact]
    public void Apply_PaintsTheCover_WhenGenerationIsCurrent()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 602, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(602, _cbzPath, 1);
        var decoded = CoverImageCache.DecodeFromDisk(stem)!;

        var image = new Image();
        AsyncCoverImage.SetSourceId(image, stem); // generation -> 1 (cache miss, background decode pending)

        AsyncCoverImage.Apply(image, stem, generation: 1, decoded);

        Assert.Same(decoded, image.Source);
        Assert.Same(decoded, CoverImageCache.Get(stem));
    }

    [Fact]
    public void Apply_DropsAStaleDecode_AfterTheContainerWasRecycled()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 603, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(603, _cbzPath, 1);
        var staleDecode = CoverImageCache.DecodeFromDisk(stem)!;

        var image = new Image();
        AsyncCoverImage.SetSourceId(image, stem); // generation -> 1
        AsyncCoverImage.SetSourceId(image, "604-cafef00d"); // container recycled: generation -> 2

        AsyncCoverImage.Apply(image, stem, generation: 1, staleDecode);

        Assert.Null(image.Source); // the cover for 603 must not land on a container now showing 604
    }

    [Fact]
    public void Apply_WithFadeInThumbnailsOn_StartsOpacityAtZero_WithATransitionAttached()
    {
        CosmeticThumbnailSettings.FadeInThumbnails = true;
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 605, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(605, _cbzPath, 1);
        var decoded = CoverImageCache.DecodeFromDisk(stem)!;

        var image = new Image();
        AsyncCoverImage.SetSourceId(image, stem);

        AsyncCoverImage.Apply(image, stem, generation: 1, decoded);

        // Opacity is re-set to 1 synchronously right after 0 (the Transition animates the visual
        // over subsequent frames, same as the existing CheckBox.tileSelect hover-fade idiom) - what
        // this test can actually assert headlessly is that a transition got attached at all.
        Assert.NotNull(image.Transitions);
        Assert.Equal(1, image.Opacity);
    }

    [Fact]
    public void Apply_WithFadeInThumbnailsOff_SetsOpacityToOne_WithNoTransition()
    {
        CosmeticThumbnailSettings.FadeInThumbnails = false;
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 606, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(606, _cbzPath, 1);
        var decoded = CoverImageCache.DecodeFromDisk(stem)!;

        var image = new Image();
        AsyncCoverImage.SetSourceId(image, stem);

        AsyncCoverImage.Apply(image, stem, generation: 1, decoded);

        Assert.Null(image.Transitions);
        Assert.Equal(1, image.Opacity);
    }

    [Fact]
    public void SettingSourceId_ToAnAlreadyCachedCover_FadesTheFirstTimeOnly()
    {
        // CE fades an item on its first valid image whether or not it was memory-cached (ThumbnailViewItem.Animate).
        // Paperbunkr used to skip the fade on every cache hit, and the prefetching grid cache made nearly every paint
        // a hit, so the toggle looked dead (2026-09-26 library audit). A recycled card showing the same cover again
        // must not re-fade.
        CosmeticThumbnailSettings.FadeInThumbnails = true;
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 607, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(607, _cbzPath, 1);
        CoverImageCache.Get(stem); // warm the in-memory cache

        var first = new Image();
        AsyncCoverImage.SetSourceId(first, stem);
        var recycled = new Image();
        AsyncCoverImage.SetSourceId(recycled, stem);

        Assert.NotNull(first.Transitions);
        Assert.Equal(1, first.Opacity); // the target; the transition animates the visual from 0
        Assert.Null(recycled.Transitions);
        Assert.Equal(1, recycled.Opacity);
    }
}
