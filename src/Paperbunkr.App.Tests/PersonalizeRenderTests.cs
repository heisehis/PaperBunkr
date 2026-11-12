using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views.Preferences;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.FocusTestHarness;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Appearance > Personalize rendered with the app styles (docs/superpowers/specs/2026-11-06-personalization-design.md): the controls are there and named,
/// and with PB_RENDER_OUT set it writes PNGs per theme and at 110 % text for a human look.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class PersonalizeRenderTests : IDisposable
{
    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string? _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _root;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public PersonalizeRenderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_personalize_render_{Guid.NewGuid():N}");
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        Directory.CreateDirectory(_root);
        string db = Path.Combine(_root, "render.db");
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
        var service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));
        service.ApplyPersonalization(s => { s.FontScale = 100; s.SidebarStyle = SidebarStyle.Standard; });
        service.ApplyTheme("default");
        ThemeService.ResetPersonalizationForTests();
        ThemePaths.InstalledDirectory = _originalInstalledDirectory;
        ThemePaths.ExtractedDirectory = _originalExtractedDirectory;
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }


    /// <summary>The app's form-control and typography styles (SuggestBox template, type classes), which <c>AppStyles()</c> does not load.</summary>
    private static IDisposable FormStyles()
    {
        var includes = new[] { "Typography", "FormControls" }
            .Select(name => new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://Paperbunkr.App/")) { Source = new Uri($"avares://Paperbunkr.App/Styles/{name}.axaml") })
            .ToList();
        foreach (var include in includes)
        {
            Application.Current!.Styles.Add(include);
        }

        return new Disposer(() =>
        {
            foreach (var include in includes)
            {
                Application.Current!.Styles.Remove(include);
            }
        });
    }

    private sealed class Disposer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
    private (Window Window, PersonalizeViewModel Vm) Open(ThemeService service)
    {
        string? accent = null;
        string body = "System Default", ring = "Normal";
        var glow = Avalonia.Media.Color.Parse("#E0995A");
        var samples = new[] { "Off", "Subtle", "Normal", "Vivid" }
            .Select((label, tier) => new Paperbunkr.App.Models.GlowTierSample(tier, label, ThemeService.BuildGlowRing(glow, tier)) { IsActive = tier == 2 })
            .ToList();
        var links = new PersonalizeLinks(() => body, v => body = v, () => ring, v => ring = v, new[] { "Off", "Subtle", "Normal", "Vivid" }, samples);
        var vm = new PersonalizeViewModel(service, () => accent, v => { accent = v; service.SetAccentOverrideHex(v); }, new[] { "System Default", "Consolas", "Georgia" }, run => run(), links);
        var window = new Window
        {
            Content = new ScrollViewer { Content = new PersonalizeGroup { DataContext = vm, Margin = new Thickness(24) } },
            Width = 900,
            Height = 980,
        };
        window.Show();
        RunLayout(window);
        return (window, vm);
    }

    [Fact]
    public void TheGroup_ShowsEveryControl_WithNames()
    {
        WithThemeAndTokens(() =>
        {
            using var styles = AppStyles();
            using var tokens = AppResources();
            using var forms = FormStyles();
            var service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));
            service.ApplyTheme("default");
            var (window, vm) = Open(service);

            var names = window.GetVisualDescendants().OfType<Control>()
                .Select(c => Avalonia.Automation.AutomationProperties.GetName(c))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet();
            foreach (string expected in new[] { "Corners", "Text size", "Heading font", "Card depth", "Sidebar selection", "Focus ring", "Custom colour", "Use the theme's own accent", "Body font", "Ring strength" })
            {
                Assert.Contains(expected, names);
            }

            Assert.Equal(6, window.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("swatch") && !b.Classes.Contains("plus") && b.IsEffectivelyVisible));
            window.Close();
        });
    }

    [Fact]
    public void RenderGroup()
    {
        string? outDir = Environment.GetEnvironmentVariable("PB_RENDER_OUT");
        if (string.IsNullOrEmpty(outDir))
        {
            return;
        }

        Directory.CreateDirectory(outDir);
        foreach (var (theme, scale, sidebar) in new[] { ("default", 100, SidebarStyle.Standard), ("daylight", 100, SidebarStyle.Pill), ("matrix", 100, SidebarStyle.Tint), ("default", 110, SidebarStyle.Standard) })
        {
            WithThemeAndTokens(() =>
            {
                using var styles = AppStyles();
                using var tokens = AppResources();
                using var forms = FormStyles();
                var service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));
                service.ApplyTheme(theme);
                service.ApplyPersonalization(s => { s.FontScale = scale; s.SidebarStyle = sidebar; });
                var (window, _) = Open(service);
                var rtb = new RenderTargetBitmap(new PixelSize(900, 980));
                rtb.Render(window);
                rtb.Save(Path.Combine(outDir, $"personalize-{theme}-{scale}.png"));
                window.Close();
            });
        }
    }
}
