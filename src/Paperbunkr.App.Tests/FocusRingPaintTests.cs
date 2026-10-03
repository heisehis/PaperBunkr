using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The app-wide keyboard focus ring really paints outside a plain Button. A Button clips to its own bounds and Avalonia clips an adorner to its adorned element's clip, so the ring (drawn outside the
/// control) used to shrink to a corner speck on every plain button, such as the Insights tabs and the Home carousel. Checked on pixels, because the ring is invisible to every layout-level check.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class FocusRingPaintTests
{
    private static uint PixelAt(WriteableBitmap bitmap, int x, int y)
    {
        using var frame = bitmap.Lock();
        return (uint)Marshal.ReadInt32(frame.Address, (y * frame.RowBytes) + (x * 4));
    }

    [Fact]
    public void AKeyboardFocusedPlainButton_HasItsRingDrawnOutsideItsBounds()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var button = new Button { Content = "Trends", Margin = new Thickness(40), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            var window = new Window { Content = button, Width = 300, Height = 200, Background = Avalonia.Media.Brushes.Black };
            window.Show();
            RunLayout(window);
            var bounds = button.Bounds;

            var before = window.CaptureRenderedFrame()!;
            button.Focus(NavigationMethod.Tab);
            RunLayout(window);
            var after = window.CaptureRenderedFrame()!;

            // Two pixels outside the left edge, halfway down: black before focus, ring colour after.
            int x = (int)bounds.X - 2;
            int y = (int)(bounds.Y + (bounds.Height / 2));
            Assert.NotEqual(PixelAt(before, x, y), PixelAt(after, x, y));
            // And above the top edge, so the ring is not just a side strip.
            int topX = (int)(bounds.X + (bounds.Width / 2));
            Assert.NotEqual(PixelAt(before, topX, (int)bounds.Y - 2), PixelAt(after, topX, (int)bounds.Y - 2));
            window.Close();
        });
    }
}
