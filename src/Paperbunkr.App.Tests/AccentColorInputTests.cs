using Avalonia.Media;
using Paperbunkr.App.Services;

namespace Paperbunkr.App.Tests;

/// <summary>Pure helpers behind the Edit Collection dialog's custom-accent flyout.</summary>
public class AccentColorInputTests
{
    [Theory]
    [InlineData("#C9803F", 0xC9, 0x80, 0x3F)]
    [InlineData("c9803f", 0xC9, 0x80, 0x3F)]
    [InlineData("  #fff ", 0xFF, 0xFF, 0xFF)]
    [InlineData("0A0", 0x00, 0xAA, 0x00)]
    public void TryParseHex_AcceptsRgbAndRrggbbWithOrWithoutHash(string text, int r, int g, int b)
    {
        Assert.True(AccentColorInput.TryParseHex(text, out var color));
        Assert.Equal(Color.FromRgb((byte)r, (byte)g, (byte)b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    [InlineData("#80C9803F")]
    public void TryParseHex_RejectsEverythingElse(string? text)
    {
        Assert.False(AccentColorInput.TryParseHex(text, out _));
    }

    [Fact]
    public void ToHex_IsUpperCaseOpaqueRrggbb()
    {
        Assert.Equal("#5FA889", AccentColorInput.ToHex(Color.FromRgb(0x5F, 0xA8, 0x89)));
    }

    [Theory]
    [InlineData(0xC9, 0x80, 0x3F)]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    [InlineData(0x7C, 0x93, 0xC9)]
    [InlineData(0x40, 0x40, 0x40)]
    public void Hsv_RoundTripsWithinOneStep(int r, int g, int b)
    {
        var original = Color.FromRgb((byte)r, (byte)g, (byte)b);
        var (h, s, v) = AccentColorInput.ToHsv(original);
        var back = AccentColorInput.FromHsv(h, s, v);

        Assert.InRange(back.R, original.R - 1, original.R + 1);
        Assert.InRange(back.G, original.G - 1, original.G + 1);
        Assert.InRange(back.B, original.B - 1, original.B + 1);
    }

    [Fact]
    public void FromHsv_PrimaryHues()
    {
        Assert.Equal(Color.FromRgb(255, 0, 0), AccentColorInput.FromHsv(0, 1, 1));
        Assert.Equal(Color.FromRgb(0, 255, 0), AccentColorInput.FromHsv(120, 1, 1));
        Assert.Equal(Color.FromRgb(0, 0, 255), AccentColorInput.FromHsv(240, 1, 1));
        Assert.Equal(Color.FromRgb(255, 0, 0), AccentColorInput.FromHsv(360, 1, 1));
    }

    [Fact]
    public void IsLowContrast_FlagsNearSurfaceColoursOnly()
    {
        var darkSurface = Color.FromRgb(0x1E, 0x1A, 0x22);
        Assert.True(AccentColorInput.IsLowContrast(Color.FromRgb(0x2A, 0x24, 0x30), darkSurface));
        Assert.False(AccentColorInput.IsLowContrast(Color.FromRgb(0xC9, 0x80, 0x3F), darkSurface));

        var lightSurface = Color.FromRgb(0xF5, 0xF1, 0xEA);
        Assert.True(AccentColorInput.IsLowContrast(Color.FromRgb(0xF0, 0xEC, 0xE0), lightSurface));
        Assert.False(AccentColorInput.IsLowContrast(Color.FromRgb(0x30, 0x30, 0x30), lightSurface));
    }
}
