using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// docs/superpowers/specs/2026-09-21-cosmetics-pitch-design.md #1/#2 - the progress math on the two
/// Library row models, and ground-truth rendering of <see cref="TileCosmeticsOverlay"/> (spine edge,
/// progress bar rules) via a real Skia-backed headless frame, the same approach
/// <see cref="MatrixRainOverlayRenderTests"/> uses because a control that draws nothing passes every
/// non-rendering unit test.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class TileCosmeticsOverlayTests
{
    private const int W = 100;
    private const int H = 150;

    private sealed class FakeSource : ITileProgressSource
    {
        public double ReadFraction { get; init; }
        public bool IsFinished { get; init; }
        public bool IsRightToLeft { get; init; }
    }

    [Fact]
    public void IssueRow_ReadFraction_UsesPercentage_AndIsReadMeansFull()
    {
        var half = new IssueListRow { SeriesName = "S", Title = "T", CoverBrush = Brushes.Black, ReadPercentage = 50 };
        Assert.Equal(0.5, half.ReadFraction, 3);
        Assert.False(half.IsFinished);

        var read = new IssueListRow { SeriesName = "S", Title = "T", CoverBrush = Brushes.Black, IsRead = true, ReadPercentage = 96 };
        Assert.Equal(1.0, read.ReadFraction);
        Assert.True(read.IsFinished);

        // Out-of-range data can't overdraw the ring.
        var over = new IssueListRow { SeriesName = "S", Title = "T", CoverBrush = Brushes.Black, ReadPercentage = 140 };
        Assert.Equal(1.0, over.ReadFraction);
    }

    [Fact]
    public void IssueRow_IsRightToLeft_OnlyForRightToLeftDirection()
    {
        Assert.True(new IssueListRow { SeriesName = "S", Title = "T", CoverBrush = Brushes.Black, ReadingDirectionLabel = "RightToLeft" }.IsRightToLeft);
        Assert.False(new IssueListRow { SeriesName = "S", Title = "T", CoverBrush = Brushes.Black, ReadingDirectionLabel = "LeftToRight" }.IsRightToLeft);
        Assert.False(new IssueListRow { SeriesName = "S", Title = "T", CoverBrush = Brushes.Black }.IsRightToLeft);
    }

    [Theory]
    [InlineData(10, 0, 1.0, true)]
    [InlineData(10, 4, 0.6, false)]
    [InlineData(10, 10, 0.0, false)]
    [InlineData(0, 0, 0.0, false)] // an empty series is not "finished"
    public void SeriesCard_ReadFraction_IsIssuesReadOverTotal(int issueCount, int unread, double fraction, bool finished)
    {
        var card = new SeriesCardSample
        {
            Title = "T", Name = "N", Sub = "S", ContentTypeLabel = "Comic", CoverBrush = Brushes.Black,
            IssueCount = issueCount, UnreadCount = unread,
            RepresentativeRow = new IssueListRow { SeriesName = "N", Title = "T", CoverBrush = Brushes.Black },
        };

        Assert.Equal(fraction, card.ReadFraction, 3);
        Assert.Equal(finished, card.IsFinished);
    }

    [Fact]
    public void Spine_DarkensLeftEdge_ForLeftToRight_AndRightEdge_ForRightToLeft()
    {
        WithSettings(spine: true, ring: true, () =>
        {
            var ltr = Render(new FakeSource { ReadFraction = 0.3 });
            Assert.True(Brightness(ltr, 1, 40) < Brightness(ltr, W - 2, 40), "LTR spine should darken the left edge only");
            Assert.True(Brightness(ltr, 1, 40) < Brightness(ltr, W / 2, 40));

            var rtl = Render(new FakeSource { ReadFraction = 0.3, IsRightToLeft = true });
            Assert.True(Brightness(rtl, W - 2, 40) < Brightness(rtl, 1, 40), "RTL spine should darken the right edge only");
        });
    }

    [Fact]
    public void Spine_Off_LeavesTheCoverUntouched()
    {
        WithSettings(spine: false, ring: false, () =>
        {
            var frame = Render(new FakeSource { ReadFraction = 0.3 });
            Assert.Equal(255, Brightness(frame, 1, 40));
        });
    }

    [Theory]
    [InlineData(0.0, false, false)]   // nothing read: no bar
    [InlineData(0.4, false, true)]    // in progress: bar
    [InlineData(1.0, false, false)]   // everything opened: no bar either, the badge speaks
    [InlineData(1.0, true, false)]    // finished
    [InlineData(0.4, true, false)]
    public void ProgressBar_ShowsOnlyWhileInProgress(double fraction, bool finished, bool expected)
    {
        Assert.Equal(expected, TileCosmeticsOverlay.ShowsProgressBar(fraction, finished));
    }

    [Fact]
    public void ProgressBar_RunsAlongTheBottomEdge_FilledFromTheLeft_AtRest()
    {
        WithSettings(spine: false, ring: true, () =>
        {
            int barY = H - 2;
            var frame = Render(new FakeSource { ReadFraction = 0.4 });

            Assert.True(Brightness(frame, 10, barY) < 250, "the read part of the bar is drawn");
            Assert.True(Brightness(frame, W - 10, barY) < 200, "the unread part shows the dark track");
            Assert.NotEqual(Brightness(frame, 10, barY), Brightness(frame, W - 10, barY));
            Assert.Equal(255, Brightness(frame, W / 2, H - 20));
        });
    }

    [Fact]
    public void ProgressBar_FillsFromTheRight_ForARightToLeftBook()
    {
        WithSettings(spine: false, ring: true, () =>
        {
            int barY = H - 2;
            var ltr = Render(new FakeSource { ReadFraction = 0.4 });
            var rtl = Render(new FakeSource { ReadFraction = 0.4, IsRightToLeft = true });

            Assert.Equal(Brightness(ltr, 10, barY), Brightness(rtl, W - 10, barY));
            Assert.Equal(Brightness(ltr, W - 10, barY), Brightness(rtl, 10, barY));
        });
    }

    [Fact]
    public void ProgressBar_NotDrawn_WhenUnread_Finished_OrSwitchedOff()
    {
        int barY = H - 2;
        WithSettings(spine: false, ring: true, () =>
        {
            Assert.Equal(255, Brightness(Render(new FakeSource { ReadFraction = 0.0 }), 10, barY));
            Assert.Equal(255, Brightness(Render(new FakeSource { ReadFraction = 1.0, IsFinished = true }), 10, barY));
        });

        WithSettings(spine: false, ring: false, () =>
        {
            Assert.Equal(255, Brightness(Render(new FakeSource { ReadFraction = 0.4 }), 10, barY));
        });
    }

    private static void Pump(Window window)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        }
    }

    private static void WithSettings(bool spine, bool ring, Action body) => WithSettings(spine, ring, checkbox: false, body);

    private static void WithSettings(bool spine, bool ring, bool checkbox, Action body)
    {
        bool oldSpine = CosmeticThumbnailSettings.BindingSpine;
        bool oldRing = CosmeticThumbnailSettings.ProgressRing;
        bool oldCheckbox = CosmeticThumbnailSettings.ShowSelectionCheckbox;
        CosmeticThumbnailSettings.BindingSpine = spine;
        CosmeticThumbnailSettings.ProgressRing = ring;
        CosmeticThumbnailSettings.ShowSelectionCheckbox = checkbox;
        try
        {
            body();
        }
        finally
        {
            CosmeticThumbnailSettings.BindingSpine = oldSpine;
            CosmeticThumbnailSettings.ProgressRing = oldRing;
            CosmeticThumbnailSettings.ShowSelectionCheckbox = oldCheckbox;
        }
    }

    private static WriteableBitmap Render(ITileProgressSource source)
    {
        var overlay = new TileCosmeticsOverlay { DataContext = source };
        var window = new Window
        {
            Width = W, Height = H, SizeToContent = SizeToContent.Manual,
            Content = new Grid { Background = Brushes.White, Children = { overlay } },
        };
        window.Show();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
            }

            return (WriteableBitmap)window.GetLastRenderedFrame()!;
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Average of the three colour channels at (x, y) - channel-order independent, white = 255.</summary>
    private static int Brightness(WriteableBitmap bitmap, int x, int y)
    {
        using var fb = bitmap.Lock();
        var px = new byte[4];
        Marshal.Copy(fb.Address + (y * fb.RowBytes) + (x * 4), px, 0, 4);
        return (px[0] + px[1] + px[2]) / 3;
    }
}
