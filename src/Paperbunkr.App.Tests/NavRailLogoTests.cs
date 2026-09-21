using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Paperbunkr.App.Tests;

/// <summary>The nav rail's top slot shows the Paperbunkr mark instead of the empty outlined box it used to hold.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class NavRailLogoTests
{
    private static readonly Uri RailLogo = new("avares://Paperbunkr.App/Assets/paperbunkr-logo-rail.png");

    [Fact]
    public void RailLogoAsset_IsBundled_AndDecodesToAnActualMark()
    {
        Assert.True(AssetLoader.Exists(RailLogo), "paperbunkr-logo-rail.png must ship as an Avalonia resource");

        using var stream = AssetLoader.Open(RailLogo);
        using var bitmap = new Bitmap(stream);

        Assert.Equal(new PixelSize(96, 96), bitmap.PixelSize);
    }

    [Fact]
    public void MainWindowRail_ShowsTheLogo_AndNoLongerTheEmptyOutlinedBox()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "src", "Paperbunkr.App", "Views", "MainWindow.axaml")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        string xaml = File.ReadAllText(Path.Combine(dir!, "src", "Paperbunkr.App", "Views", "MainWindow.axaml"));

        Assert.Contains("avares://Paperbunkr.App/Assets/paperbunkr-logo-rail.png", xaml);
        // The old placeholder: a 26x26 bordered square in the accent colour with no content.
        Assert.DoesNotContain("<Border Width=\"26\" Height=\"26\" CornerRadius=\"6\" BorderThickness=\"1.5\"", xaml);
    }
}
