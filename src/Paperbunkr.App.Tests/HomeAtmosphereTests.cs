using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Paperbunkr.App.Controls;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.App.Views.Home;
using Paperbunkr.Data;
using SkiaSharp;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Home's atmosphere. The blurred spotlight cover used to be an image inside the spotlight band - a 300px, rounded, clipped box with an
/// opaque fill and a wash on top - so it showed as a dark rectangle behind the carousel. These tests pin down that cause (a rendered
/// reproduction of the old band has a hard edge) and the fix (one page-level backdrop, masked to nothing, with no edge anywhere).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class HomeAtmosphereTests : IDisposable
{
    private static readonly Color DarkPage = Color.Parse("#14181D");
    private static readonly Color LightPage = Color.Parse("#F4F1EA");

    private readonly string? _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pb_home_atmosphere_{Guid.NewGuid():N}");

    public HomeAtmosphereTests()
    {
        Directory.CreateDirectory(_root);
        PaperbunkrDbContext.DatabasePathOverride = Path.Combine(_root, "home.db");
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A flat, saturated image: any place it shows through is unmistakable against a grey page.</summary>
    private static WriteableBitmap SolidImage(Color color, int width = 64, int height = 32)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }

        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        return bitmap;
    }

    private static SKBitmap Render(Window window)
    {
        RunLayout(window);
        var target = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        target.Render(window);
        using var stream = new MemoryStream();
        target.Save(stream);
        return SKBitmap.Decode(stream.ToArray());
    }

    private static int Distance(SKColor a, Color b) => Math.Abs(a.Red - b.R) + Math.Abs(a.Green - b.G) + Math.Abs(a.Blue - b.B);

    private static int Distance(SKColor a, SKColor b) => Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);

    // ===================== The cause =====================

    [Fact]
    public void TheOldSpotlightBand_AnImageInAClippedOpaqueBox_HasAHardEdgeOnTheTopAndSides()
    {
        WithThemeAndTokens(() =>
        {
            // What SpotlightSection.axaml used to hold: the band (300px, clipped, filled) with the blurred cover and a wash inside it.
            var band = new Border
            {
                Height = 300,
                Margin = new Thickness(60, 80, 60, 0),
                VerticalAlignment = VerticalAlignment.Top,
                ClipToBounds = true,
                Background = new SolidColorBrush(DarkPage),
                Child = new Panel
                {
                    Children =
                    {
                        new Image { Source = SolidImage(Colors.Red), Stretch = Stretch.UniformToFill },
                        new Border
                        {
                            Background = new LinearGradientBrush
                            {
                                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                                GradientStops = { new GradientStop(Color.Parse("#59000000"), 0), new GradientStop(DarkPage, 1) },
                            },
                        },
                    },
                },
            };
            var window = new Window { Width = 800, Height = 500, Background = new SolidColorBrush(DarkPage), Content = band };
            window.Show();
            try
            {
                using var pixels = Render(window);

                // One pixel outside the box is the page; a few pixels inside it is the image. That jump is the rectangle.
                Assert.True(Distance(pixels.GetPixel(400, 76), DarkPage) < 6);
                Assert.True(Distance(pixels.GetPixel(400, 84), pixels.GetPixel(400, 76)) > 80, "expected a hard top edge");
                Assert.True(Distance(pixels.GetPixel(64, 130), pixels.GetPixel(56, 130)) > 60, "expected a hard left edge");
                Assert.True(Distance(pixels.GetPixel(736, 130), pixels.GetPixel(744, 130)) > 60, "expected a hard right edge");
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ===================== The fix =====================

    [Theory]
    [InlineData(true, 0.6)]
    [InlineData(false, 0.38)]
    public void TheAtmosphere_IsStrongestAtTheTop_FadesSmoothlyToThePage_AndHasNoEdge(bool dark, double strength)
    {
        var page = dark ? DarkPage : LightPage;
        WithThemeAndTokens(() =>
        {
            var atmosphere = new AtmosphereBackdrop { Source = SolidImage(Colors.Red), Strength = strength, FadeStart = 0.3, Height = 400, VerticalAlignment = VerticalAlignment.Top };
            var window = new Window { Width = 800, Height = 500, Background = new SolidColorBrush(page), Content = atmosphere };
            window.Show();
            try
            {
                using var pixels = Render(window);

                // Strongest at the very top, and it is a blend with the page, never the bare image.
                int top = Distance(pixels.GetPixel(400, 2), page);
                Assert.True(top > 100, $"the top should clearly show the image (distance {top})");
                Assert.True(Distance(pixels.GetPixel(400, 2), Colors.Red) > 60, "the page should show through even at full strength");

                // Full width: the left edge, the middle and the right edge of a row are the same.
                foreach (int y in new[] { 2, 150, 300 })
                {
                    Assert.True(Distance(pixels.GetPixel(1, y), pixels.GetPixel(400, y)) < 6);
                    Assert.True(Distance(pixels.GetPixel(798, y), pixels.GetPixel(400, y)) < 6);
                }

                // Down a column: never brighter than the row above, never a visible step, and at the bottom edge it is the page.
                int previous = top;
                for (int y = 3; y < 400; y++)
                {
                    int here = Distance(pixels.GetPixel(400, y), page);
                    Assert.True(here <= previous + 2, $"row {y} is stronger than the row above it ({here} > {previous})");
                    Assert.True(previous - here <= 8, $"row {y} steps by {previous - here}: that would read as an edge");
                    previous = here;
                }

                Assert.True(Distance(pixels.GetPixel(400, 398), page) < 8, "the bottom of the atmosphere should already be the page");
                Assert.True(Distance(pixels.GetPixel(400, 404), page) < 4, "below it there is only the page");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheMask_EndsFullyTransparent_AndNeverExceedsItsStrength()
    {
        var mask = AtmosphereBackdrop.BuildMask(0.6, 0.3);

        Assert.Equal(0, mask.GradientStops[^1].Color.A);
        Assert.Equal(1, mask.GradientStops[^1].Offset);
        Assert.Equal((byte)153, mask.GradientStops[0].Color.A);
        Assert.All(mask.GradientStops, stop => Assert.True(stop.Color.A <= 153));
        Assert.Equal(mask.GradientStops.Select(s => s.Offset).OrderBy(o => o), mask.GradientStops.Select(s => s.Offset));
        Assert.Equal(mask.GradientStops.Select(s => s.Color.A).OrderByDescending(a => a), mask.GradientStops.Select(s => s.Color.A));
    }

    [Fact]
    public void WithNoImage_OrNoStrength_NothingIsDrawn()
    {
        WithThemeAndTokens(() =>
        {
            var panel = new Panel
            {
                Children =
                {
                    new AtmosphereBackdrop { Height = 200, VerticalAlignment = VerticalAlignment.Top },
                    new AtmosphereBackdrop { Source = SolidImage(Colors.Red), Strength = 0, Height = 200, VerticalAlignment = VerticalAlignment.Top },
                },
            };
            var window = new Window { Width = 300, Height = 300, Background = new SolidColorBrush(DarkPage), Content = panel };
            window.Show();
            try
            {
                using var pixels = Render(window);
                Assert.True(Distance(pixels.GetPixel(150, 5), DarkPage) < 4);
            }
            finally
            {
                window.Close();
            }
        });
    }

    // ===================== On the real Home screen =====================

    [Fact]
    public void Home_HasOnePageLevelAtmosphere_BehindTheMastheadAndTheSections_NotInsideTheCarousel()
    {
        WithThemeAndTokens(() =>
        {
            using var resources = AppResources();
            using var styles = AppStyles();
            var screen = new HomeScreen { DataContext = new HomeScreenViewModel(_ => { }, _ => { }, _ => { }, (_, _) => { }, (_, _) => { }) };
            var window = new Window { Content = screen, Width = 1300, Height = 900 };
            window.Show();
            try
            {
                RunLayout(window);
                TestDispatcher.Drain();
                RunLayout(window);

                var atmosphere = Assert.Single(screen.GetVisualDescendants().OfType<AtmosphereBackdrop>());
                var masthead = Assert.Single(screen.GetVisualDescendants().OfType<HomeMasthead>());
                var sections = Assert.Single(screen.GetVisualDescendants().OfType<HomeSections>());

                // Page level: a sibling of the content, not something inside the sections or the masthead.
                Assert.DoesNotContain(atmosphere, sections.GetVisualDescendants());
                Assert.DoesNotContain(atmosphere, masthead.GetVisualDescendants());
                Assert.False(atmosphere.IsHitTestVisible);

                // It starts at the very top of the page, spans its whole width, and runs well past the masthead into the sections.
                var scroller = screen.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "PageScroller");
                var origin = atmosphere.TranslatePoint(default, masthead)!.Value;
                Assert.Equal(0, origin.X, 1);
                Assert.Equal(0, origin.Y, 1);
                Assert.Equal(masthead.Bounds.Width, atmosphere.Bounds.Width, 1);
                Assert.True(atmosphere.Bounds.Height > masthead.Bounds.Height + 300, "it should reach past the spotlight band");
                Assert.True(atmosphere.Bounds.Width >= scroller.Viewport.Width - 1);

                // Drawn before (so behind) the content it sits under.
                var parent = (Panel)atmosphere.GetVisualParent()!;
                Assert.Equal(0, parent.Children.IndexOf(atmosphere));

                // The masthead fades out through a mask, so it has no opaque bottom edge laid over the atmosphere.
                var masked = masthead.GetVisualDescendants().OfType<Control>().Where(c => c.OpacityMask is LinearGradientBrush).ToList();
                Assert.Equal(2, masked.Count);                              // the cover wall (dark themes) and the sky (light themes)
                Assert.All(masked, c => Assert.Equal(0, ((LinearGradientBrush)c.OpacityMask!).GradientStops[^1].Color.A));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheSpotlightBand_HasNoFillOfItsOwn_ButStillTakesThePointer()
    {
        WithThemeAndTokens(() =>
        {
            using var resources = AppResources();
            var homeStyles = new StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri("avares://Paperbunkr.App/Views/Home/HomeStyles.axaml") };
            var band = new Border { Classes = { "spotlightBand" } };
            var window = new Window { Width = 400, Height = 400, Content = band };
            window.Styles.Add(homeStyles);
            window.Show();
            try
            {
                RunLayout(window);

                var fill = Assert.IsAssignableFrom<ISolidColorBrush>(band.Background);     // not null: a null background would not hit-test (hover-to-pause)
                Assert.Equal(0, fill.Color.A);
                Assert.Equal(300, band.Bounds.Height);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
