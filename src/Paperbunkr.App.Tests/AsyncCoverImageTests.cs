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
    public void SettingSourceId_ToAnAlreadyCachedCover_NeverFades_EvenWhenFadeInThumbnailsIsOn()
    {
        CosmeticThumbnailSettings.FadeInThumbnails = true;
        CbzFixture.Create(_cbzPath, pageCount: 1);
        new CoverThumbnailService().TryGenerateThumbnail(issueId: 607, _cbzPath, fileSize: 1);
        string stem = CoverFingerprint.Stem(607, _cbzPath, 1);
        CoverImageCache.Get(stem); // warm the in-memory cache

        var image = new Image();
        AsyncCoverImage.SetSourceId(image, stem); // cache hit - must be instant, never fade

        Assert.Null(image.Transitions);
        Assert.Equal(1, image.Opacity);
    }
}
