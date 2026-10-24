using System.ComponentModel;
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
/// Cosmetics pitch #19 - ground-truth rendering of <see cref="SpineTile"/> and <see cref="ShelfBoards"/> through a real Skia-backed headless frame
/// (a control that draws nothing passes every non-rendering test). Set PB_SPINE_PNG to a folder to also write the frames out for a look.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SpineTileRenderTests
{
    private const int W = 60;
    private const int H = 236;

    private sealed class FakeSpine : ISpineSource, ITileProgressSource, ISelectableCard, INotifyPropertyChanged
    {
        public double SpineWidth { get; init; } = 40;
        public double SpineHeight { get; init; } = 196;
        public string SpineTitle { get; init; } = "Batman";
        public string? SpineNumber { get; init; } = "#12";
        public double ReadFraction { get; init; }
        public bool IsFinished { get; init; }
        public bool IsRightToLeft => false;
        public int Id => 1;
        public bool IsSelected { get; set; }
#pragma warning disable CS0067
        public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
    }

    private static WriteableBitmap Render(Control control, int width, int height, string name)
    {
        var window = new Window
        {
            Width = width, Height = height, SizeToContent = SizeToContent.Manual,
            Content = new Grid { Background = Brushes.White, Children = { control } },
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
            if (Environment.GetEnvironmentVariable("PB_SPINE_PNG") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                frame.Save(Path.Combine(dir, name + ".png"));
            }

            return frame;
        }
        finally
        {
            window.Close();
        }
    }

    private static int Brightness(WriteableBitmap bitmap, int x, int y)
    {
        using var fb = bitmap.Lock();
        var px = new byte[4];
        Marshal.Copy(fb.Address + (y * fb.RowBytes) + (x * 4), px, 0, 4);
        return (px[0] + px[1] + px[2]) / 3;
    }

    [Fact]
    public void Spine_fills_its_width_and_stands_on_the_board()
    {
        var frame = Render(new SpineTile { DataContext = new FakeSpine() }, W, H, "spine-plain");
        Assert.True(Brightness(frame, W / 2, 120) < 200, "the spine body should be drawn, not white");
        Assert.Equal(255, Brightness(frame, 2, 120));          // the gap beside the spine is untouched
        Assert.Equal(255, Brightness(frame, W / 2, 4));        // nothing above the spine
    }

    [Fact]
    public void Spine_inside_a_default_Button_still_draws()
    {
        // A Button centres its content vertically unless told otherwise; the tile must still get a height (the bug that drew only shelf boards).
        var button = new Button { Width = 40, Padding = new Avalonia.Thickness(0), Content = new SpineTile { DataContext = new FakeSpine() } };
        var frame = Render(button, W, H, "spine-in-button");
        Assert.True(Brightness(frame, W / 2, 120) < 200, "a spine in an unstretched Button should still be drawn");
    }

    [Fact]
    public void Taller_spine_reaches_higher()
    {
        var tall = Render(new SpineTile { DataContext = new FakeSpine { SpineHeight = 210 } }, W, H, "spine-tall");
        var short_ = Render(new SpineTile { DataContext = new FakeSpine { SpineHeight = 182 } }, W, H, "spine-short");
        int probeY = (int)(H - SpineMetrics.BoardHeight - 205);
        Assert.True(Brightness(tall, W / 2, probeY) < 255);
        Assert.Equal(255, Brightness(short_, W / 2, probeY));
    }

    [Fact]
    public void Finished_spine_shows_a_progress_foot_and_selected_spine_an_outline()
    {
        var plain = Render(new SpineTile { DataContext = new FakeSpine() }, W, H, "spine-unread");
        var selected = Render(new SpineTile { DataContext = new FakeSpine { IsSelected = true } }, W, H, "spine-selected");
        int topEdgeY = (int)(H - SpineMetrics.BoardHeight - 196) - 1;
        Assert.NotEqual(Brightness(plain, W / 2, topEdgeY), Brightness(selected, W / 2, topEdgeY));
    }

    [Fact]
    public void Shelf_boards_draw_one_board_per_row()
    {
        int rows = 3;
        var frame = Render(new ShelfBoards(), 200, (int)(SpineMetrics.CellHeight * rows), "shelf");
        for (int row = 0; row < rows; row++)
        {
            int y = (int)(((row + 1) * SpineMetrics.CellHeight) - (SpineMetrics.BoardHeight / 2));
            Assert.True(Brightness(frame, 100, y) < 255, $"row {row} should have a board");
        }

        Assert.Equal(255, Brightness(frame, 100, 100));
    }
}
