using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="PageImageDecoder"/> against a real synthetic .cbz
/// (docs/superpowers/specs/2026-08-06-reader-canvas-alpha-design.md §9). Runs under
/// <see cref="AvaloniaTestCollection"/> so the headless Avalonia platform is bootstrapped once
/// before any test - needed because <see cref="Avalonia.Media.Imaging.Bitmap"/> construction
/// requires a registered IPlatformRenderInterface.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PageImageDecoderTests : IDisposable
{
    private readonly string _cbzPath;

    public PageImageDecoderTests()
    {
        _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_test_{Guid.NewGuid():N}.cbz");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_cbzPath)) File.Delete(_cbzPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TryOpen_ReturnsNull_ForMissingFile()
    {
        var decoder = PageImageDecoder.TryOpen(Path.Combine(Path.GetTempPath(), $"paperbunkr_does_not_exist_{Guid.NewGuid():N}.cbz"));
        Assert.Null(decoder);
    }

    [Fact]
    public void TryOpen_ReturnsDecoderWithCorrectPageCount()
    {
        CbzFixture.Create(_cbzPath, pageCount: 4);

        using var decoder = PageImageDecoder.TryOpen(_cbzPath);

        Assert.NotNull(decoder);
        Assert.Equal(4, decoder!.PageCount);
    }

    [Fact]
    public void GetPage_DecodesRealBitmapFromByteImagePath()
    {
        CbzFixture.Create(_cbzPath, pageCount: 3);
        using var decoder = PageImageDecoder.TryOpen(_cbzPath)!;

        var bitmap = decoder.GetPage(0);

        Assert.NotNull(bitmap);
        Assert.Equal(64, bitmap.PixelSize.Width);
        Assert.Equal(96, bitmap.PixelSize.Height);
    }

    [Fact]
    public void GetPage_ReturnsSameCachedInstanceOnRepeatedCallsWithinWindow()
    {
        CbzFixture.Create(_cbzPath, pageCount: 3);
        using var decoder = PageImageDecoder.TryOpen(_cbzPath)!;

        var first = decoder.GetPage(1);
        var second = decoder.GetPage(1);

        Assert.Same(first, second);
    }

    [Fact]
    public void GetPage_AcrossAllPages_DoesNotThrow()
    {
        CbzFixture.Create(_cbzPath, pageCount: 5);
        using var decoder = PageImageDecoder.TryOpen(_cbzPath)!;

        for (int i = 0; i < decoder.PageCount; i++)
        {
            var bitmap = decoder.GetPage(i);
            Assert.NotNull(bitmap);
        }
    }

    /// <summary>
    /// Regression test for a real bug: ReaderScreenViewModel.RefreshCurrentPage() calls GetPage on
    /// the UI thread for the just-opened issue's current page (almost always page 0) at the same
    /// moment its background thumbnail-generation task calls GetThumbnail for page 0 too - both
    /// hitting the same unsynchronized archive read, leaving the Reader's first thumbnail blank
    /// (the resulting exception was swallowed and rendered as no image). Fires GetPage/GetThumbnail
    /// concurrently across every page - not just page 0, since navigating while thumbnails are
    /// still generating races the same way - and asserts every decode succeeds with real pixels.
    /// </summary>
    [Fact]
    public async Task GetPageAndGetThumbnail_CalledConcurrently_BothSucceedForEveryPage()
    {
        CbzFixture.Create(_cbzPath, pageCount: 8);
        using var decoder = PageImageDecoder.TryOpen(_cbzPath)!;

        var tasks = new List<Task>();
        for (int i = 0; i < decoder.PageCount; i++)
        {
            int page = i;
            tasks.Add(Task.Run(() =>
            {
                var bitmap = decoder.GetPage(page);
                Assert.True(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0);
            }));
            tasks.Add(Task.Run(() =>
            {
                var thumb = decoder.GetThumbnail(page);
                Assert.True(thumb.PixelSize.Width > 0 && thumb.PixelSize.Height > 0);
            }));
        }

        await Task.WhenAll(tasks);
    }
}
