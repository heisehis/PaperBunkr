using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Views.Preferences;
using Paperbunkr.Data;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Appearance gallery tile (docs/superpowers/specs/2026-11-09-theme-gallery-and-roster-design.md Q4) drawn for every built-in theme with the app styles:
/// each tile carries the theme's own canvas (a gradient where the theme has one) and its name. With PB_RENDER_OUT set it writes a contact sheet
/// of all tiles for a human look.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeGalleryTileRenderTests : IDisposable
{
    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string? _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _root;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ThemeGalleryTileRenderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_gallery_tile_{Guid.NewGuid():N}");
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        Directory.CreateDirectory(_root);
        string db = Path.Combine(_root, "tiles.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={db}").Options;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Database.EnsureCreated();
        }

        PaperbunkrDbContext.DatabasePathOverride = db;
        ThemeService.ResetPersonalizationForTests();
    }

    public void Dispose()
    {
        new ThemeService(() => new PaperbunkrDbContext(_dbOptions)).ApplyTheme("default");
        ThemeService.ResetPersonalizationForTests();
        ThemePaths.InstalledDirectory = _originalInstalledDirectory;
        ThemePaths.ExtractedDirectory = _originalExtractedDirectory;
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private (Window Window, IReadOnlyList<Paperbunkr.App.Models.ThemeSummary> Themes) OpenSheet(ThemeService service)
    {
        var themes = service.GetAvailableThemes();
        var grid = new UniformGrid { Columns = 6, Margin = new Thickness(24) };
        foreach (var theme in themes)
        {
            var button = new Button { Content = new ThemeGalleryTile { DataContext = theme }, Margin = new Thickness(0, 0, 12, 12) };
            button.Classes.Add("clickCard");
            button.Classes.Set("active", theme.IsActive);
            Avalonia.Automation.AutomationProperties.SetName(button, theme.Name);
            grid.Children.Add(button);
        }

        var window = new Window { Content = grid, Width = 1000, Height = 120 * ((themes.Count + 5) / 6) + 48 };
        window.Show();
        RunLayout(window);
        return (window, themes);
    }

    [Fact]
    public void EveryBuiltInTile_DrawsItsOwnCanvasAndName()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            var service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));
            service.ApplyTheme("default");
            var (window, themes) = OpenSheet(service);

            Assert.All(themes, t => Assert.NotNull(t.CanvasBrush));
            Assert.All(themes, t => Assert.NotNull(t.NavigationBrush));

            // A family that ships a gradient material keeps it on its tile; a legacy theme is a flat colour.
            Assert.IsType<LinearGradientBrush>(themes.Single(t => t.Key == "abyss:dark").NavigationBrush);
            Assert.IsType<SolidColorBrush>(themes.Single(t => t.Key == "default").CanvasBrush);
            Assert.Equal("Georgia", themes.Single(t => t.Key == "newsprint:paper").HeadingFontFamily.Name);
            Assert.Equal(new CornerRadius(0), themes.Single(t => t.Key == "newsprint:paper").ButtonRadius);

            var tiles = window.GetVisualDescendants().OfType<ThemeGalleryTile>().ToList();
            Assert.Equal(themes.Count, tiles.Count);
            Assert.All(tiles, tile => Assert.True(tile.Bounds.Width > 80 && tile.Bounds.Height > 80, $"tile laid out at {tile.Bounds}"));
            window.Close();
        });
    }

    [Fact]
    public void RenderContactSheet()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        foreach (string appTheme in new[] { "default", "daylight" })
        {
            WithThemeAndTokens(() =>
            {
                using var styles = AppStyles();
                using var tokens = AppResources();
                var service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));
                service.ApplyTheme(appTheme);
                var (window, _) = OpenSheet(service);
                var rtb = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
                rtb.Render(window);
                rtb.Save(Path.Combine(outDir, $"gallery-tiles-on-{appTheme}.png"));
                window.Close();
            });
        }
    }
}
