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
/// ring visibility rules) via a real Skia-backed headless frame, the same approach
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
            var ltr = Render(new FakeSource { ReadFraction = 0.3 }, hover: false);
            Assert.True(Brightness(ltr, 1, 40) < Brightness(ltr, W - 2, 40), "LTR spine should darken the left edge only");
            Assert.True(Brightness(ltr, 1, 40) < Brightness(ltr, W / 2, 40));

            var rtl = Render(new FakeSource { ReadFraction = 0.3, IsRightToLeft = true }, hover: false);
            Assert.True(Brightness(rtl, W - 2, 40) < Brightness(rtl, 1, 40), "RTL spine should darken the right edge only");
        });
    }

    [Fact]
    public void Spine_Off_LeavesTheCoverUntouched()
    {
        WithSettings(spine: false, ring: false, () =>
        {
            var frame = Render(new FakeSource { ReadFraction = 0.3 }, hover: false);
            Assert.Equal(255, Brightness(frame, 1, 40));
        });
    }

    [Fact]
    public void Ring_HiddenAtRest_ShownOnHover_AndShownAtRestWhenFinished()
    {
        WithSettings(spine: false, ring: true, checkbox: false, () =>
        {
            // With the selection checkbox off the ring sits bottom-RIGHT; sample its backdrop (a dark disc over the white cover).
            int ringY = H - 7 - 17;
            int ringX = (int)TileCosmeticsOverlay.RingCenterX(W, checkboxOwnsBottomRight: false);

            var rest = Render(new FakeSource { ReadFraction = 0.4 }, hover: false);
            Assert.Equal(255, Brightness(rest, ringX, ringY));

            var hovered = Render(new FakeSource { ReadFraction = 0.4 }, hover: true);
            Assert.True(Brightness(hovered, ringX - 12, ringY) < 200, "hover should draw the ring backdrop");

            var finished = Render(new FakeSource { ReadFraction = 1.0, IsFinished = true }, hover: false);
            Assert.True(Brightness(finished, ringX - 12, ringY) < 200, "a finished tile shows its ring at rest");
        });
    }

    [Fact]
    public void Ring_IsBottomRight_WhenNoCheckbox_AndBottomCentre_WhenTheCheckboxOwnsTheCorner()
    {
        int ringY = H - 7 - 17;
        int rightX = (int)TileCosmeticsOverlay.RingCenterX(W, checkboxOwnsBottomRight: false);
        int centreX = (int)TileCosmeticsOverlay.RingCenterX(W, checkboxOwnsBottomRight: true);
        Assert.True(rightX > centreX + 15, "the two positions must actually differ");

        WithSettings(spine: false, ring: true, checkbox: false, () =>
        {
            var frame = Render(new FakeSource { ReadFraction = 0.4 }, hover: true);
            Assert.True(Brightness(frame, rightX + 10, ringY) < 200, "checkbox off: ring in the bottom-right");
            Assert.Equal(255, Brightness(frame, centreX - 10, ringY));
        });

        // Setting on: the hover checkbox lives bottom-right, so the ring steps aside to the centre.
        WithSettings(spine: false, ring: true, checkbox: true, () =>
        {
            var frame = Render(new FakeSource { ReadFraction = 0.4 }, hover: true);
            Assert.True(Brightness(frame, centreX - 10, ringY) < 200, "checkbox on: ring in the bottom-centre");
            Assert.Equal(255, Brightness(frame, rightX + 10, ringY));
        });

        // A selected tile always shows its checked box, even with the setting off - so its ring also steps aside.
        WithSettings(spine: false, ring: true, checkbox: false, () =>
        {
            var frame = Render(new SelectedSource { ReadFraction = 0.4, IsSelected = true }, hover: true);
            Assert.True(Brightness(frame, centreX - 10, ringY) < 200, "selected tile: ring in the bottom-centre");
            Assert.Equal(255, Brightness(frame, rightX + 10, ringY));
        });
    }

    [Fact]
    public void Ring_MovesWhenTheTileIsSelectedWhileItsRingShows()
    {
        // The row raises IsSelected while the pointer is over it; the overlay must repaint, not wait for the next hover change.
        WithSettings(spine: false, ring: true, checkbox: false, () =>
        {
            var source = new SelectedSource { ReadFraction = 0.4 };
            var overlay = new TileCosmeticsOverlay { DataContext = source, HoverRing = true };
            var window = new Window
            {
                Width = W, Height = H, SizeToContent = SizeToContent.Manual,
                Content = new Grid { Background = Brushes.White, Children = { overlay } },
            };
            window.Show();
            try
            {
                Pump(window);
                int ringY = H - 7 - 17;
                int rightX = (int)TileCosmeticsOverlay.RingCenterX(W, false);
                int centreX = (int)TileCosmeticsOverlay.RingCenterX(W, true);
                Assert.True(Brightness((WriteableBitmap)window.GetLastRenderedFrame()!, rightX + 10, ringY) < 200);

                source.IsSelected = true;
                Pump(window);

                var after = (WriteableBitmap)window.GetLastRenderedFrame()!;
                Assert.True(Brightness(after, centreX - 10, ringY) < 200, "ring moved to the centre once selected");
                Assert.Equal(255, Brightness(after, rightX + 10, ringY));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Ring_Off_NeverDraws_EvenWhenFinishedOrHovered()
    {
        WithSettings(spine: false, ring: false, checkbox: false, () =>
        {
            var frame = Render(new FakeSource { ReadFraction = 1.0, IsFinished = true }, hover: true);
            int x = (int)TileCosmeticsOverlay.RingCenterX(W, false) - 12;
            Assert.Equal(255, Brightness(frame, x, H - 7 - 17));
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

    /// <summary>A tile source that also reports selection (and raises IsSelected), like the real row models.</summary>
    private sealed class SelectedSource : ITileProgressSource, ISelectableCard, System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public double ReadFraction { get; init; }
        public bool IsFinished { get; init; }
        public bool IsRightToLeft { get; init; }
        public int Id => 1;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
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

    private static WriteableBitmap Render(ITileProgressSource source, bool hover)
    {
        var overlay = new TileCosmeticsOverlay { DataContext = source, HoverRing = hover };
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
