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

    [Fact]
    public void TheRingOfAControlScrolledOutOfItsList_IsNotDrawnOverTheRestOfTheScreen()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var theme = new Avalonia.Themes.Fluent.FluentTheme();
            // A header row above a scrolling list; the focused button is then scrolled up out of the list, to where the header is.
            var content = new StackPanel { Spacing = 20 };
            Button? target = null;
            for (int i = 0; i < 12; i++)
            {
                var button = new Button { Content = $"Row {i}", Width = 120, Margin = new Thickness(40, 0, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                target ??= button;
                content.Children.Add(button);
            }

            var scroller = new ScrollViewer { Content = content, Height = 140, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden };
            var root = new DockPanel();
            var header = new Border { Height = 120, Background = Avalonia.Media.Brushes.Black };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            root.Children.Add(scroller);
            Application.Current!.Styles.Add(theme);
            try
            {
                var window = new Window { Content = root, Width = 300, Height = 300, Background = Avalonia.Media.Brushes.Black };
                window.Show();
                RunLayout(window);

                target!.Focus(NavigationMethod.Tab);
                RunLayout(window);
                var inView = window.CaptureRenderedFrame()!;
                var spot = target.TranslatePoint(default, window)!.Value;
                int x = (int)spot.X - 2;
                int y = (int)(spot.Y + (target.Bounds.Height / 2));
                Assert.NotEqual(0xFF000000u, PixelAt(inView, x, y));            // sanity: the ring is there while the button is on screen

                // Scroll the list until the button is half above it. The part of the ring that falls over the header must not be drawn.
                scroller.Offset = new Vector(0, 15);
                RunLayout(window);
                var partlyOut = window.CaptureRenderedFrame()!;
                var now = target.TranslatePoint(default, window)!.Value;
                double listTop = scroller.TranslatePoint(default, window)!.Value.Y;
                Assert.True(now.Y < listTop && now.Y + target.Bounds.Height > listTop, $"the button is partly above the list (button top {now.Y}, list top {listTop})");
                // Just above the list's top edge, level with the button's left stroke: header territory.
                Assert.Equal(0xFF000000u, PixelAt(partlyOut, Math.Max(0, (int)now.X - 2), (int)listTop - 6));
                // And the part inside the list is still ringed.
                Assert.NotEqual(0xFF000000u, PixelAt(partlyOut, Math.Max(0, (int)now.X - 2), (int)listTop + 10));
                window.Close();
            }
            finally
            {
                Application.Current!.Styles.Remove(theme);
            }
        });
    }
}
