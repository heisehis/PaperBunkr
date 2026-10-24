using Avalonia.Media;
using Paperbunkr.App.Services.Reader;
using Xunit;

namespace Paperbunkr.App.Tests;

public class PageEdgeColorsTests
{
    private static uint[] Solid(int w, int h, uint argb) => Enumerable.Repeat(argb, w * h).ToArray();

    [Fact]
    public void Solid_page_gives_that_colour_on_every_edge()
    {
        var edges = PageEdgeColors.FromPixels(Solid(20, 30, 0xFF336699), 20, 30)!.Value;
        Assert.Equal(Color.FromRgb(0x33, 0x66, 0x99), edges.Top);
        Assert.Equal(edges.Top, edges.Bottom);
        Assert.Equal(edges.Top, edges.Left);
        Assert.Equal(edges.Top, edges.Right);
    }

    [Fact]
    public void Top_and_bottom_halves_are_told_apart()
    {
        var pixels = new uint[10 * 10];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = i < 50 ? 0xFFFF0000 : 0xFF0000FF;
        }

        var edges = PageEdgeColors.FromPixels(pixels, 10, 10)!.Value;
        Assert.Equal(Color.FromRgb(255, 0, 0), edges.Top);
        Assert.Equal(Color.FromRgb(0, 0, 255), edges.Bottom);
    }

    [Fact]
    public void White_border_around_a_dark_page_samples_white()
    {
        const int w = 40, h = 40;
        var pixels = Solid(w, h, 0xFFFFFFFF);
        for (int y = 5; y < h - 5; y++)
        {
            for (int x = 5; x < w - 5; x++)
            {
                pixels[(y * w) + x] = 0xFF000000;
            }
        }

        var edges = PageEdgeColors.FromPixels(pixels, w, h)!.Value;
        Assert.Equal(Colors.White, edges.Top);
        Assert.Equal(Colors.White, edges.Left);
    }

    [Fact]
    public void Wrong_sized_or_empty_buffer_gives_null()
    {
        Assert.Null(PageEdgeColors.FromPixels(new uint[3], 10, 10));
        Assert.Null(PageEdgeColors.FromPixels(default, 0, 0));
    }

    [Fact]
    public void Softened_colour_is_never_brighter_than_the_source()
    {
        var soft = PageEdgeColors.Soften(Colors.White);
        Assert.True(soft.R < 255 && soft.G < 255 && soft.B < 255);
    }

    [Fact]
    public void Brush_has_exactly_three_stops_so_transitions_can_interpolate()
    {
        var edges = PageEdgeColors.FromPixels(Solid(10, 10, 0xFF808080), 10, 10)!.Value;
        var brush = Assert.IsAssignableFrom<IGradientBrush>(PageEdgeColors.BuildBrush(edges));
        Assert.Equal(3, brush.GradientStops.Count);
    }
}
