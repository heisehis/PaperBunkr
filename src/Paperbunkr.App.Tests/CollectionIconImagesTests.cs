using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>The "My icons" folder (docs/superpowers/specs/2026-11-06-collection-icons-and-slim-banner-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class CollectionIconImagesTests : IDisposable
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 100\"><rect width=\"200\" height=\"100\" fill=\"#c00\"/></svg>";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pb-collection-icons-" + Guid.NewGuid().ToString("N"));

    public CollectionIconImagesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private void WritePng(string name, int width, int height)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        bitmap.Save(Path.Combine(_folder, name));
    }

    [Fact]
    public void Scan_FindsSvgAndPng_LabelledByFileName_AndSkipsTheRest()
    {
        File.WriteAllText(Path.Combine(_folder, "Batman.svg"), Svg);
        WritePng("Wolverine.png", 300, 150);
        File.WriteAllText(Path.Combine(_folder, "Broken.svg"), "not an svg");
        File.WriteAllBytes(Path.Combine(_folder, "Broken.png"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(_folder, "Huge.png"), new byte[CollectionIconImages.MaxFileBytes + 1]);
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "ignored, not counted");

        var scan = CollectionIconImages.Scan(_folder);

        Assert.Equal(new[] { "Batman", "Wolverine" }, scan.Files.Select(f => f.Label));
        Assert.Equal(new[] { "Batman.svg", "Wolverine.png" }, scan.Files.Select(f => f.FileName));
        Assert.Equal(3, scan.Skipped);
    }

    [Fact]
    public void Get_FitsTheLongestSideIntoTheBox()
    {
        File.WriteAllText(Path.Combine(_folder, "Wide.svg"), Svg);
        WritePng("Tall.png", 50, 400);

        var svg = CollectionIconImages.Get("Wide.svg", 32, _folder);
        var png = CollectionIconImages.Get("Tall.png", 32, _folder);

        Assert.NotNull(svg);
        Assert.Equal(new PixelSize(32, 16), svg!.PixelSize);
        Assert.NotNull(png);
        Assert.Equal(32, png!.PixelSize.Height);
        Assert.Equal(4, png.PixelSize.Width);
    }

    [Theory]
    [InlineData("Missing.svg")]
    [InlineData(@"..\Escape.svg")]
    [InlineData("notes.txt")]
    [InlineData("")]
    public void Get_IsNullForMissingUnsafeOrUnsupportedNames(string name) => Assert.Null(CollectionIconImages.Get(name, 32, _folder));

    [Fact]
    public void Scan_OfAMissingFolder_IsEmpty()
    {
        var scan = CollectionIconImages.Scan(Path.Combine(_folder, "nope"));
        Assert.Empty(scan.Files);
        Assert.Equal(0, scan.Skipped);
    }
}
