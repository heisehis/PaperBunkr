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
}
