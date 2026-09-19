using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Paperbunkr.App.Views;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Ground-truth render check for <see cref="MatrixRainOverlay"/>: mounts it in a real Skia-backed
/// headless window over a black background and reads the last rendered frame's pixels. The rain
/// shipped invisible (never rendered anywhere in the running app) because MainWindow.axaml marked
/// both full-area screen hosts as opaque backplates, clipping the whole overlay away - no unit test
/// could see that, only an actual rendered frame can. Two mounts are covered: visible from the start,
/// and the real-world path (mounted invisible while another theme is active, flipped visible on a
/// live switch to Matrix).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class MatrixRainOverlayRenderTests
{
    private static int CountGreenPixelsAfterRunning(Window window, int maxMilliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(maxMilliseconds);
        int green = 0;
        while (DateTime.UtcNow < deadline && green == 0)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(4);
            Thread.Sleep(40);
            green = CountGreenPixels(window);
        }

        return green;
    }

    private static int CountGreenPixels(Window window)
    {
        var frame = window.GetLastRenderedFrame();
        if (frame is not WriteableBitmap bitmap)
        {
            return 0;
        }

        using var fb = bitmap.Lock();
        int width = fb.Size.Width;
        int height = fb.Size.Height;
        var row = new byte[fb.RowBytes];
        int count = 0;
        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(fb.Address + (y * fb.RowBytes), row, 0, fb.RowBytes);
            for (int x = 0; x < width; x++)
            {
                // Rendered in BGRA/RGBA depending on platform - the rain is green on black, so "the
                // green channel clearly dominates" holds for either channel order.
                byte b0 = row[x * 4];
                byte g = row[(x * 4) + 1];
                byte r2 = row[(x * 4) + 2];
                if (g > 40 && g > b0 + 15 && g > r2 + 15)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static Window Mount(bool initiallyVisible, out MatrixRainOverlay overlay)
    {
        overlay = new MatrixRainOverlay { IsVisible = initiallyVisible };
        var window = new Window
        {
            Width = 480,
            Height = 360,
            Background = Brushes.Black,
            Content = overlay,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        return window;
    }

    [Fact]
    public void Overlay_VisibleFromMount_RendersGreenGlyphs()
    {
        var window = Mount(initiallyVisible: true, out _);
        try
        {
            int green = CountGreenPixelsAfterRunning(window, 6000);
            Assert.True(green > 0, "MatrixRainOverlay rendered no green pixels over a black background.");
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Overlay_MountedInvisible_ThenFlippedVisible_RendersGreenGlyphs()
    {
        var window = Mount(initiallyVisible: false, out var overlay);
        try
        {
            Assert.Equal(0, CountGreenPixels(window));

            overlay.IsVisible = true;
            Dispatcher.UIThread.RunJobs();

            int green = CountGreenPixelsAfterRunning(window, 6000);
            Assert.True(green > 0, "MatrixRainOverlay flipped visible after mounting invisible rendered no green pixels.");
        }
        finally
        {
            window.Close();
        }
    }
}
