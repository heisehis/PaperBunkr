using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>docs/superpowers/specs/2026-09-21-cosmetics-pitch-2-design.md #18 - typographic placeholder for a missing cover.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PlaceholderCoverTests
{
    private sealed class FakeSource : IPlaceholderCoverSource
    {
        public string? CoverTitle { get; init; }
    }

    [Theory]
    [InlineData("Absolute Batman", "AB")]
    [InlineData("The Amazing Spider-Man", "ASM")]      // leading "The" skipped, hyphen splits, capped at 3
    [InlineData("batman", "B")]
    [InlineData("One Piece Omnibus Edition", "OPO")]
    [InlineData("  ", "?")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    [InlineData("The", "T")]                            // a lone "The" is the name, not a prefix
    public void InitialsFor_TakesUpToThreeWordInitials(string? name, string expected)
        => Assert.Equal(expected, PlaceholderCoverText.InitialsFor(name));

    [Fact]
    public void InitialsFor_IsDeterministic()
        => Assert.Equal(PlaceholderCoverText.InitialsFor("Absolute Carnage"), PlaceholderCoverText.InitialsFor("Absolute Carnage"));

    [Fact]
    public void StartsHidden_AndOnlyShowsWhenToldTheCoverIsMissing()
    {
        var placeholder = new PlaceholderCoverText();
        Assert.False(placeholder.IsVisible);

        placeholder.SetMissing(true);
        Assert.True(placeholder.IsVisible);

        placeholder.SetMissing(false);
        Assert.False(placeholder.IsVisible);
    }

    [Fact]
    public void AsyncCoverImage_ShowsThePlaceholder_OnlyAfterAnEmptyDecode_AndHidesItOnRepoint()
    {
        var image = new Image();
        var placeholder = new PlaceholderCoverText();
        var host = new Grid { Children = { image, placeholder } };
        Assert.NotNull(host);

        AsyncCoverImage.SetSourceId(image, "7001-aaaaaaaa"); // generation 1: decode pending - NOT a missing cover
        Assert.False(placeholder.IsVisible, "still loading must not flash placeholder text");

        AsyncCoverImage.Apply(image, "7001-aaaaaaaa", generation: 1, decoded: null); // empty decode: genuinely no cover
        Assert.True(placeholder.IsVisible);

        AsyncCoverImage.SetSourceId(image, "7002-bbbbbbbb"); // container recycled to another issue
        Assert.False(placeholder.IsVisible);
    }

    [Fact]
    public void AsyncCoverImage_StaleEmptyDecode_DoesNotShowThePlaceholder()
    {
        var image = new Image();
        var placeholder = new PlaceholderCoverText();
        _ = new Grid { Children = { image, placeholder } };

        AsyncCoverImage.SetSourceId(image, "7001-aaaaaaaa"); // generation 1
        AsyncCoverImage.SetSourceId(image, "7002-bbbbbbbb"); // generation 2 (recycled)

        AsyncCoverImage.Apply(image, "7001-aaaaaaaa", generation: 1, decoded: null); // late result for the OLD issue

        Assert.False(placeholder.IsVisible);
    }

    [Fact]
    public void Render_DrawsText_OnlyWhenVisible_AndDoesNothingForAnUnrelatedDataContext()
    {
        int lit = LitPixels(new FakeSource { CoverTitle = "Absolute Batman" }, missing: true);
        Assert.True(lit > 40, $"expected visible initials + title over the dark cover, found {lit} lit pixels");

        Assert.Equal(0, LitPixels(new FakeSource { CoverTitle = "Absolute Batman" }, missing: false));
        Assert.Equal(0, LitPixels(new object(), missing: true));
    }

    private static int LitPixels(object dataContext, bool missing)
    {
        var placeholder = new PlaceholderCoverText { DataContext = dataContext };
        placeholder.SetMissing(missing);
        var window = new Window
        {
            Width = 140, Height = 210, SizeToContent = SizeToContent.Manual,
            Content = new Grid { Background = Brushes.Black, Children = { placeholder } },
        };
        window.Show();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            }

            var frame = (WriteableBitmap)window.GetLastRenderedFrame()!;
            using var fb = frame.Lock();
            var row = new byte[fb.RowBytes];
            int lit = 0;
            for (int y = 0; y < fb.Size.Height; y++)
            {
                Marshal.Copy(fb.Address + (y * fb.RowBytes), row, 0, fb.RowBytes);
                for (int x = 0; x < fb.Size.Width; x++)
                {
                    if (row[x * 4] + row[(x * 4) + 1] + row[(x * 4) + 2] > 240)
                    {
                        lit++;
                    }
                }
            }

            return lit;
        }
        finally
        {
            window.Close();
        }
    }
}
