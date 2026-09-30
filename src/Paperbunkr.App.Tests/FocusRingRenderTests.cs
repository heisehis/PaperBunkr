using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Views;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>The keyboard focus ring on Home is Paperbunkr's glow ring, drawn outside the control (docs/superpowers/specs/
/// 2026-09-29-keyboard-focus-reclaim-detail-home-smart-design.md). Renders a real frame with the app's own style files and looks for glow
/// pixels just outside the focused control: the tile ring element was once hidden by a locally-set IsVisible, and Fluent buttons clip their
/// own shadow, so both failed silently.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class FocusRingRenderTests
{
    private static void WithAppStyles(Action body)
    {
        WithThemeAndTokens(() =>
        {
            var styles = new[]
            {
                new StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Styles/Primitives.axaml") },
            };
            foreach (var style in styles)
            {
                Application.Current!.Styles.Add(style);
            }

            try
            {
                body();
            }
            finally
            {
                foreach (var style in styles)
                {
                    Application.Current!.Styles.Remove(style);
                }
            }
        });
    }

    private static SkiaSharp.SKColor PixelOutside(Window window, Control control, double dx, double dy)
    {
        var rtb = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        rtb.Render(window);
        using var mem = new System.IO.MemoryStream();
        rtb.Save(mem);
        using var bmp = SkiaSharp.SKBitmap.Decode(mem.ToArray());
        var point = control.TranslatePoint(new Point(dx, dy), window)!.Value;
        return bmp.GetPixel((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    [Fact]
    public void FocusedTile_ShowsAGlowRingOutsideItsCover()
    {
        WithAppStyles(() =>
        {
            var tile = new PosterTile { TitleText = "T", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var window = new Window { Content = tile, Width = 500, Height = 500, Background = Brushes.Black };
            window.Show();
            RunLayout(window);
            var cover = tile.Root;
            var before = PixelOutside(window, cover, -3, 100);

            tile.Root.Focus(NavigationMethod.Directional);
            RunLayout(window);
            var after = PixelOutside(window, cover, -3, 100);

            Assert.True(after.Red > before.Red + 40, $"before R{before.Red}G{before.Green}B{before.Blue}, after R{after.Red}G{after.Green}B{after.Blue}");
            window.Close();
        });
    }

    /// <summary>The app-wide focus adorner (Primitives.axaml) replaces the theme's white/black frame with the glow ring on any control -
    /// here a plain Button on no particular screen.</summary>
    [Fact]
    public void AnyFocusedButton_ShowsTheGlowRingNotTheThemeFrame()
    {
        WithAppStyles(() =>
        {
            var button = new Button { Content = "Plain", Width = 140, Height = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var window = new Window { Content = button, Width = 500, Height = 300, Background = Brushes.Black };
            window.Show();
            RunLayout(window);
            var before = PixelOutside(window, button, -3, 20);

            Press(window, Key.Tab);
            Assert.Same(button, Focused(window));
            var after = PixelOutside(window, button, -3, 20);

            // The glow is the accent hue (red well above blue); the theme frame it replaces is white/black (red == blue).
            Assert.True(after.Red > before.Red + 40 && after.Red > after.Blue + 40, $"before R{before.Red}G{before.Green}B{before.Blue}, after R{after.Red}G{after.Green}B{after.Blue}");
            window.Close();
        });
    }

    [Fact]
    public void FocusedHomeButton_ShowsAGlowRingOutsideItself()
    {
        WithAppStyles(() =>
        {
            var button = new Button { Content = "Resume", Width = 140, Height = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var host = new Border { Child = button };
            var home = new HomeStylesHost { Content = host };
            var window = new Window { Content = home, Width = 500, Height = 300, Background = Brushes.Black };
            window.Show();
            RunLayout(window);
            var before = PixelOutside(window, button, -3, 20);

            button.Focus(NavigationMethod.Directional);
            RunLayout(window);
            var after = PixelOutside(window, button, -3, 20);

            Assert.True(after.Red > before.Red + 40, $"before R{before.Red}G{before.Green}B{before.Blue}, after R{after.Red}G{after.Green}B{after.Blue}");
            window.Close();
        });
    }

    /// <summary>Same shape as Home's shelves (Views/Home/*Section.axaml): a horizontal scroller whose first tile sits flush with its edge. The
    /// ring must still show on that tile's outer side - the scroller clips its content, so the room has to be inside the content.</summary>
    /// <summary>The Library lesson (2026-09-26): Avalonia only repaints inside a control's own bounds when its state changes, so a ring painted
    /// outside the tile shows partly or not at all - and a full-frame render test can't see that. The focused tile's ring (the tileRing
    /// overlay plus its PbGlowRing spread, with the focus pop applied) must therefore fit inside the PosterTile's own bounds.</summary>
    [Fact]
    public void FocusedTile_PaintsItsRingInsideItsOwnBounds()
    {
        WithAppStyles(() =>
        {
            var tile = new PosterTile { TitleText = "T" };
            var window = new Window { Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { tile } }, Width = 500, Height = 500 };
            window.Show();
            RunLayout(window);
            tile.Root.Focus(NavigationMethod.Directional);
            RunLayout(window);

            var ring = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(tile).OfType<Border>().First(b => b.Classes.Contains("tileRing"));
            Assert.True(ring.IsVisible);
            double spread = ring.BoxShadow.Count > 0 ? ring.BoxShadow[0].Spread : 0;
            var topLeft = ring.TranslatePoint(new Point(-spread, -spread), tile)!.Value;
            var bottomRight = ring.TranslatePoint(new Point(ring.Bounds.Width + spread, ring.Bounds.Height + spread), tile)!.Value;
            var painted = new Rect(topLeft, bottomRight);
            var own = new Rect(tile.Bounds.Size);

            Assert.True(own.Contains(painted), $"ring paints {painted}, tile's own bounds {own}");
            window.Close();
        });
    }

    [Fact]
    public void FocusedFirstTileInAShelf_ShowsItsRingOnTheOuterSide()
    {
        WithAppStyles(() =>
        {
            var shelf = ShelfAsDeclared();
            shelf.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            var tile = new PosterTile { TitleText = "first" };
            ((Panel)shelf.Content!).Children.Add(tile);
            ((Panel)shelf.Content!).Children.Add(new PosterTile { TitleText = "second" });
            var window = new Window { Content = new Border { Padding = new Thickness(40), Child = shelf }, Width = 600, Height = 400, Background = Brushes.Black };
            window.Show();
            RunLayout(window);
            var cover = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(tile).OfType<Border>().First(b => b.Classes.Contains("posterCover"));
            var coverAt = cover.TranslatePoint(default, window)!.Value;
            Assert.True(Math.Abs(coverAt.X - 40) < 0.5 && Math.Abs(coverAt.Y - 40) < 0.5, $"first cover should line up with the column edge (40,40), is at {coverAt}");
            var before = PixelOutside(window, cover, -3, 100);

            tile.Root.Focus(NavigationMethod.Directional);
            RunLayout(window);
            var left = PixelOutside(window, cover, -3, 100);
            var top = PixelOutside(window, cover, 70, -3);

            Assert.True(left.Red > before.Red + 40, $"left side: before R{before.Red}, after R{left.Red}");
            Assert.True(top.Red > before.Red + 40, $"top side: before R{before.Red}, after R{top.Red}");
            window.Close();
        });
    }

    [Fact]
    public void WalkingRightOntoATileThatScrollsIn_ShowsItsRingOnTheViewportSide()
    {
        WithAppStyles(() =>
        {
            var shelf = ShelfAsDeclared();
            var tiles = Enumerable.Range(0, 6).Select(i => new PosterTile { TitleText = $"t{i}" }).ToList();
            foreach (var tile in tiles)
            {
                ((Panel)shelf.Content!).Children.Add(tile);
            }

            var window = new Window { Content = new Border { Padding = new Thickness(40, 40, 40, 0), Child = shelf }, Width = 500, Height = 400, Background = Brushes.Black };
            window.Show();
            RunLayout(window);
            tiles[0].Root.Focus(NavigationMethod.Directional);
            RunLayout(window);

            Press(window, Key.Right);
            Press(window, Key.Right);
            Press(window, Key.Right);
            Assert.Same(tiles[3].Root, Focused(window));
            TestDispatcher.Drain();
            RunLayout(window);

            var cover = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(tiles[3]).OfType<Border>().First(b => b.Classes.Contains("posterCover"));
            var right = PixelOutside(window, cover, cover.Bounds.Width + 3, 100);
            Assert.True(right.Red > 40, $"right side of the ring R{right.Red}G{right.Green}B{right.Blue}, shelf offset {shelf.Offset}");
            window.Close();
        });
    }

    /// <summary>The shelf scroller with the Margin/Padding the Home section template actually declares (read from the .axaml, so the test
    /// follows the real markup rather than a copy of it).</summary>
    private static ScrollViewer ShelfAsDeclared()
    {
        var section = File.ReadAllText(Path.Combine(SourceDir(), "..", "Paperbunkr.App", "Views", "Home", "RecentlyAddedSection.axaml"));
        string Attr(string tagPattern, string name)
        {
            var tag = System.Text.RegularExpressions.Regex.Match(section, tagPattern).Value;
            var value = System.Text.RegularExpressions.Regex.Match(tag, $@"\s{name}=""([^""]*)""").Groups[1].Value;
            return value.Length == 0 ? "0" : value;
        }

        const string scroller = "<ScrollViewer [^>]*>";
        const string items = @"<ItemsControl ItemsSource=""\{Binding Home\.RecentlyAdded\}""[^>]*>";
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Margin = Thickness.Parse(Attr(scroller, "Margin")),
            Padding = Thickness.Parse(Attr(scroller, "Padding")),
            Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 16, Margin = Thickness.Parse(Attr(items, "Margin")) },
        };
    }

    private static string SourceDir([System.Runtime.CompilerServices.CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    /// <summary>HomeStyles.axaml reaches only what sits under a HomeScreen, so the button test hangs its button beneath one.</summary>
    private sealed class HomeStylesHost : ContentControl
    {
        public HomeStylesHost() => Styles.Add(new StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Views/Home/HomeStyles.axaml") });
    }
}
