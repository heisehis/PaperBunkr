using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Controls;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests.Controls;

/// <summary>
/// The sliding tab underline lives in the window's adorner layer, not under its tab strip, and Avalonia only takes an adorner away when
/// the adorned control is detached. A closed overlay (OverlayShell/Reveal) or a hidden panel is only <c>IsVisible=false</c>, so its strip's
/// bar used to stay painted where the strip last was: the stray 2 px accent line left over the Detail screen after closing
/// "Detail list options". Checked on pixels, because the adorner is outside the hidden subtree and every layout-level check of the strip
/// says it is hidden.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class TabIndicatorHiddenStripTests
{
    private static uint PixelAt(WriteableBitmap bitmap, int x, int y)
    {
        using var frame = bitmap.Lock();
        return (uint)Marshal.ReadInt32(frame.Address, (y * frame.RowBytes) + (x * 4));
    }

    private sealed record Scene(Window Window, Border Overlay, StackPanel Strip, Button First, Button Second)
    {
        /// <summary>The colour on the strip's bottom row under the middle of <paramref name="tab"/>, where the bar draws.</summary>
        public uint UnderlineUnder(Button tab)
        {
            var origin = tab.TranslatePoint(default, Window)!.Value;
            var stripOrigin = Strip.TranslatePoint(default, Window)!.Value;
            int x = (int)(origin.X + (tab.Bounds.Width / 2));
            int y = (int)(stripOrigin.Y + Strip.Bounds.Height) - 1;
            return PixelAt(Window.CaptureRenderedFrame()!, x, y);
        }

        public uint Background => PixelAt(Window.CaptureRenderedFrame()!, 2, 2);
    }

    private static void WithStrip(Action<Scene> body)
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            TabIndicator.Install();
            var first = new Button { Classes = { "tab", "active" }, Content = "Poster", Width = 60 };
            var second = new Button { Classes = { "tab" }, Content = "List", Width = 60 };
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { first, second } };
            var overlay = new Border
            {
                Child = strip,
                Margin = new Thickness(40),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var window = new Window { Content = new Panel { Children = { overlay } }, Width = 300, Height = 200, Background = Brushes.Black };
            window.Show();
            RunLayout(window);
            try
            {
                body(new Scene(window, overlay, strip, first, second));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void ClosingTheOverlayThatHoldsAStrip_TakesItsBarAway_AndReopeningPutsItBack()
    {
        WithStrip(scene =>
        {
            uint background = scene.Background;
            Assert.NotEqual(background, scene.UnderlineUnder(scene.First));        // drawn while the overlay is open

            scene.Overlay.IsVisible = false;                                         // what OverlayShell's close does
            RunLayout(scene.Window);
            Assert.Equal(background, scene.UnderlineUnder(scene.First));

            scene.Overlay.IsVisible = true;
            RunLayout(scene.Window);
            Assert.NotEqual(background, scene.UnderlineUnder(scene.First));
        });
    }

    [Fact]
    public void ATabSwitchedWhileTheStripIsHidden_ShowsTheBarUnderTheNewTabOnReopen()
    {
        WithStrip(scene =>
        {
            uint background = scene.Background;
            scene.Overlay.IsVisible = false;
            RunLayout(scene.Window);

            scene.First.Classes.Remove("active");                                    // a closed dialog's view model resets its tab
            scene.Second.Classes.Add("active");
            RunLayout(scene.Window);
            Assert.Equal(background, scene.UnderlineUnder(scene.First));

            scene.Overlay.IsVisible = true;
            RunLayout(scene.Window);
            Assert.NotEqual(background, scene.UnderlineUnder(scene.Second));
            Assert.Equal(background, scene.UnderlineUnder(scene.First));
        });
    }

    [Fact]
    public void HidingTheActiveTab_OrLeavingNoTabActive_TakesTheBarAway()
    {
        WithStrip(scene =>
        {
            uint background = scene.Background;
            scene.First.IsVisible = false;                                            // e.g. Insights' Recap tab when Recap stops being available
            RunLayout(scene.Window);
            Assert.Equal(background, scene.UnderlineUnder(scene.Second));
            Assert.Equal(background, scene.UnderlineUnder(scene.First));

            scene.First.IsVisible = true;
            RunLayout(scene.Window);
            Assert.NotEqual(background, scene.UnderlineUnder(scene.First));

            scene.First.Classes.Remove("active");                                    // no tab active at all
            RunLayout(scene.Window);
            Assert.Equal(background, scene.UnderlineUnder(scene.First));
        });
    }
}
