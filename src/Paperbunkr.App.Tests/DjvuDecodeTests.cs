using cYo.Projects.ComicRack.Engine.IO.Provider;
using cYo.Projects.ComicRack.Engine.IO.Provider.Readers;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>
/// DjVu pages decode through DjVuLibre's command-line tools, which the engine (ported as-is from
/// CE) runs out of <c>Resources\</c> next to the Engine assembly: <c>djvm.exe</c> lists the pages
/// (<see cref="DjvuComicProvider"/>, whose <c>IsValid</c> gates provider registration),
/// <c>ddjvu.exe</c> renders one (<see cref="DjVuImage.GetBitmap"/>), <c>c44.exe</c> encodes one
/// (<see cref="DjVuImage.SaveDjVu"/>). These tests open a real .djvu through the same
/// <see cref="PageDecodeCore"/> path the reader and cover generation use.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DjvuDecodeTests : IDisposable
{
    private readonly string _djvuPath = Path.Combine(Path.GetTempPath(), $"pb_djvu_test_{Guid.NewGuid():N}.djvu");

    private static string EngineResourcesDir =>
        Path.Combine(Path.GetDirectoryName(typeof(DjVuImage).Assembly.Location)!, "Resources");

    public void Dispose()
    {
        try { if (File.Exists(_djvuPath)) File.Delete(_djvuPath); } catch (IOException) { }
    }

    [Theory]
    [InlineData("ddjvu.exe")]
    [InlineData("djvm.exe")]
    [InlineData("c44.exe")]
    public void DjVuLibreTool_ShipsInEngineResourcesFolder(string exe)
    {
        Assert.True(File.Exists(Path.Combine(EngineResourcesDir, exe)), $"missing {Path.Combine(EngineResourcesDir, exe)}");
    }

    [Fact]
    public void DjvuProvider_IsRegisteredForDjvuExtension()
    {
        Assert.Equal(typeof(DjvuComicProvider), Providers.Readers.GetSourceProviderType(_djvuPath));
    }

    [Fact]
    public void DjvuFile_OpensWithAllPages()
    {
        DjvuFixture.Create(_djvuPath, pageCount: 3);

        using var provider = PageDecodeCore.TryOpenProvider(_djvuPath);

        Assert.IsType<DjvuComicProvider>(provider);
        Assert.Equal(3, provider!.Count);
    }

    [Fact]
    public void DjvuFile_DecodesFirstPage()
    {
        DjvuFixture.Create(_djvuPath, pageCount: 2);

        using var bitmap = PageDecodeCore.DecodeSinglePage(_djvuPath, 0);

        Assert.NotNull(bitmap);
        Assert.Equal(ExpectedRenderSize, new System.Drawing.Size(bitmap!.PixelSize.Width, bitmap.PixelSize.Height));
    }

    [Fact]
    public void DjVuImage_GetBitmap_RendersRequestedPage()
    {
        DjvuFixture.Create(_djvuPath, pageCount: 3);

        using var bitmap = DjVuImage.GetBitmap(_djvuPath, 1);

        Assert.Equal(ExpectedRenderSize, bitmap.Size);
        // Page 2 of the fixture is SteelBlue (70,130,180); c44 is lossy, so allow some drift.
        var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        Assert.InRange(center.R, 50, 90);
        Assert.InRange(center.G, 110, 150);
        Assert.InRange(center.B, 160, 200);
    }

    /// <summary>
    /// ddjvu's <c>-size=WxH</c> (CE's <c>EngineConfiguration.DjVuSizeLimit</c>, default 2000x2000)
    /// scales the page to fit that box, keeping its aspect ratio - up as well as down.
    /// </summary>
    private static System.Drawing.Size ExpectedRenderSize
    {
        get
        {
            var limit = cYo.Projects.ComicRack.Engine.EngineConfiguration.Default.DjVuSizeLimit;
            double scale = Math.Min((double)limit.Width / DjvuFixture.Width, (double)limit.Height / DjvuFixture.Height);
            return new System.Drawing.Size((int)Math.Round(DjvuFixture.Width * scale), (int)Math.Round(DjvuFixture.Height * scale));
        }
    }
}
