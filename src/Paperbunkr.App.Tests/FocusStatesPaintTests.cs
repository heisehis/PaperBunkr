using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>Hover and keyboard-focus states that have to be seen on pixels (a style that is "applied" can still draw nothing).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class FocusStatesPaintTests : IDisposable
{
    private readonly string? _original;
    private readonly string _db;

    public FocusStatesPaintTests()
    {
        _original = PaperbunkrDbContext.DatabasePathOverride;
        _db = Path.Combine(Path.GetTempPath(), $"pb_focusstates_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _db;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _original;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch (IOException) { }
    }

    private static IDisposable WithFluentTheme()
    {
        var theme = new Avalonia.Themes.Fluent.FluentTheme();
        Application.Current!.Styles.Add(theme);
        return new Release(() => Application.Current!.Styles.Remove(theme));
    }

    private sealed class Release(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private static uint PixelAt(WriteableBitmap bitmap, int x, int y)
    {
        using var frame = bitmap.Lock();
        return (uint)Marshal.ReadInt32(frame.Address, (y * frame.RowBytes) + (x * 4));
    }

    /// <summary>The number of pixels inside <paramref name="area"/> that differ between two frames.</summary>
    private static int Changed(WriteableBitmap before, WriteableBitmap after, Rect area)
    {
        int count = 0;
        for (int y = (int)area.Top; y < (int)area.Bottom; y++)
        {
            for (int x = (int)area.Left; x < (int)area.Right; x++)
            {
                if (PixelAt(before, x, y) != PixelAt(after, x, y))
                {
                    count++;
                }
            }
        }

        return count;
    }

    [Fact]
    public void ThePreferencesSidebarItems_ShowKeyboardFocus()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            // A ScrollViewer has no template without the Fluent theme, which would leave the sidebar unpainted.
            using var theme = WithFluentTheme();
            var screen = new PreferencesScreen();
            var window = new Window { Content = screen, Width = 1300, Height = 900 };
            window.Show();
            RunLayout(window);

            // An item that is not the active section, so the only change is the focus.
            var item = screen.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("prefNavItem") && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Appearance"));
            // Without a view model its command binding leaves the button disabled; the state under test is the focus, so give it a live command.
            item.Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });
            RunLayout(window);
            var origin = item.TranslatePoint(default, window)!.Value;
            var area = new Rect(origin, item.Bounds.Size);

            var before = window.CaptureRenderedFrame()!;
            item.Focus(NavigationMethod.Directional);
            RunLayout(window);
            var after = window.CaptureRenderedFrame()!;

            Assert.True(item.IsFocused, "the item took focus");
            Assert.True(Changed(before, after, area) > 100, $"focusing a sidebar item changed {Changed(before, after, area)} pixels inside it");
            window.Close();
        });
    }

    [Fact]
    public void ABooksCard_ShowsARingOutsideItsCover_OnKeyboardFocus()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            using (var context = PaperbunkrDb.CreateContext())
            {
                for (int i = 1; i <= 6; i++)
                {
                    context.Books.Add(new Book { Title = $"Book {i:00}", Format = BookFormat.Epub, FilePath = $"book{i}.epub", AddedTime = DateTime.UtcNow });
                }

                context.SaveChanges();
            }

            var vm = new BooksScreenViewModel(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { });
            var screen = new BooksScreen { DataContext = vm };
            var window = new Window { Content = screen, Width = 1200, Height = 800, Background = Avalonia.Media.Brushes.Black };
            window.Show();
            RunLayout(window);

            // The second card: its left edge has a neighbour's gutter, not the screen edge, to spread into.
            var card = screen.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).ElementAt(1);
            var cover = card.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("bookCover"));
            var origin = cover.TranslatePoint(default, window)!.Value;
            // A strip just outside the cover's left edge: nothing is drawn there until the ring shows.
            var strip = new Rect(origin.X - 3, origin.Y + 20, 2, cover.Bounds.Height - 40);

            var before = window.CaptureRenderedFrame()!;
            card.Focus(NavigationMethod.Directional);
            RunLayout(window);
            var focused = window.CaptureRenderedFrame()!;
            Assert.True(Changed(before, focused, strip) > 50, $"focus drew {Changed(before, focused, strip)} ring pixels outside the cover");
            window.Close();
        });
    }
    [Fact]
    public void ABooksCard_ShowsARingOutsideItsCover_OnHover()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            using (var context = PaperbunkrDb.CreateContext())
            {
                for (int i = 1; i <= 6; i++)
                {
                    context.Books.Add(new Book { Title = $"Book {i:00}", Format = BookFormat.Epub, FilePath = $"book{i}.epub", AddedTime = DateTime.UtcNow });
                }

                context.SaveChanges();
            }

            var vm = new BooksScreenViewModel(_ => { }, _ => { }, _ => { }, _ => { }, _ => { }, () => { });
            var screen = new BooksScreen { DataContext = vm };
            var window = new Window { Content = screen, Width = 1200, Height = 800, Background = Avalonia.Media.Brushes.Black };
            window.Show();
            RunLayout(window);

            var card = screen.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("card") && b.IsEffectivelyVisible).ElementAt(1);
            var cover = card.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("bookCover"));
            var origin = cover.TranslatePoint(default, window)!.Value;
            var strip = new Rect(origin.X - 3, origin.Y + 20, 2, cover.Bounds.Height - 40);

            window.MouseMove(new Point(0, 0), RawInputModifiers.None);
            RunLayout(window);
            var before = window.CaptureRenderedFrame()!;
            window.MouseMove(new Point(origin.X + (cover.Bounds.Width / 2), origin.Y + 60), RawInputModifiers.None);
            RunLayout(window);
            var hovered = window.CaptureRenderedFrame()!;
            Assert.True(Changed(before, hovered, strip) > 50, $"hover drew {Changed(before, hovered, strip)} ring pixels outside the cover");
            window.Close();
        });
    }
}
