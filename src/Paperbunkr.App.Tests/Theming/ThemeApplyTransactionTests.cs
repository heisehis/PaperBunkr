using Avalonia;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Runtime;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// Applying a theme is all-or-nothing and recoverable (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 1d;
/// design doc sections 10.1 and 13.3): a failed commit restores what it overwrote, the selection is saved only after a successful apply, and a
/// missing or broken active theme falls back to the built-in default instead of stopping startup.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeApplyTransactionTests : IDisposable
{
    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pb_theme_tx_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ThemeApplyTransactionTests()
    {
        Directory.CreateDirectory(_root);
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "tx.db")}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        ThemePaths.InstalledDirectory = _originalInstalledDirectory;
        ThemePaths.ExtractedDirectory = _originalExtractedDirectory;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private ThemeService CreateService(IThemeOverrideStore? store = null) => new(() => new PaperbunkrDbContext(_dbOptions), null, store);

    private string ActiveKey()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.GetOrCreateAppSettings().ActiveThemeKey;
    }

    private void SetActiveKey(string key)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.GetOrCreateAppSettings().ActiveThemeKey = key;
        context.SaveChanges();
    }

    private static Color Resource(string key) => (Color)Application.Current!.Resources[key]!;

    private void InstallExtracted(string key, string themeJson)
    {
        string folder = Path.Combine(ThemePaths.ExtractedDirectory, key);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "theme.json"), themeJson);
    }

    [Fact]
    public void AFailedCommit_RestoresEveryResourceItHadAlreadyWritten()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        var bgBefore = Resource("PbBgColor");
        var accentBefore = Resource("PbAccentColor");
        var variantBefore = Application.Current!.RequestedThemeVariant;

        var daylight = ThemeResolver.Resolve(service.Catalog.Load("daylight")!, null);
        var plan = ThemeRuntimeApplier.BuildPlan(daylight);
        int writes = 0;

        var ex = Assert.Throws<ThemeApplyException>(() => ThemeRuntimeApplier.Commit(plan, _ =>
        {
            if (++writes == 12)
            {
                throw new InvalidOperationException("simulated failure part-way through");
            }
        }));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.True(writes >= 12);                                               // it really did get part-way
        Assert.Equal(bgBefore, Resource("PbBgColor"));
        Assert.Equal(accentBefore, Resource("PbAccentColor"));
        Assert.Equal(variantBefore, Application.Current.RequestedThemeVariant);
    }

    [Fact]
    public void Commit_SkipsValuesThatDidNotChange()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        var brushBefore = Application.Current!.Resources["PbBgBrush"];

        int writes = 0;
        ThemeRuntimeApplier.Commit(ThemeRuntimeApplier.BuildPlan(ThemeResolver.Resolve(service.Catalog.Load("default")!, null)), _ => writes++);

        Assert.Equal(0, writes);
        Assert.Same(brushBefore, Application.Current.Resources["PbBgBrush"]);     // not even replaced by an equal brush
    }

    [Fact]
    public void ApplyingAThemeThatDoesNotExist_Throws_AndKeepsTheActiveOne()
    {
        var service = CreateService();
        service.ApplyTheme("daylight");
        var bg = Resource("PbBgColor");

        Assert.Throws<InvalidOperationException>(() => service.ApplyTheme("no_such_theme"));

        Assert.Equal("daylight", ActiveKey());
        Assert.Equal(bg, Resource("PbBgColor"));
    }

    [Fact]
    public void Startup_WithAnActiveThemeThatIsGone_FallsBackToTheDefault_AndRecordsIt()
    {
        SetActiveKey("uninstalled_theme");
        var service = CreateService();

        service.ApplyPersistedSettings();

        Assert.Equal(ThemeService.DefaultThemeKey, ActiveKey());
        Assert.Equal(Color.Parse("#0A0B0D"), Resource("PbBgColor"));
        Assert.Equal("default", service.Current!.ThemeKey);
    }

    [Fact]
    public void Startup_WithAnActiveThemeWhoseFileIsCorrupt_FallsBackToTheDefault()
    {
        InstallExtracted("mangled", "{ this is not json");
        SetActiveKey("mangled");
        var service = CreateService();

        service.ApplyPersistedSettings();

        Assert.Equal(ThemeService.DefaultThemeKey, ActiveKey());
        Assert.Equal(Color.Parse("#0A0B0D"), Resource("PbBgColor"));
    }

    [Fact]
    public void ReaderSuspend_OnlyRewritesTheSurfaces_NotTheWholeTheme()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        service.SetTrueBlackDark(true);
        Assert.Equal(Colors.Black, Resource("PbBgColor"));
        var accentBrush = Application.Current!.Resources["PbAccentBrush"];
        var textBrush = Application.Current.Resources["PbTextBrush"];

        service.SetTrueBlackReaderSuspend(true);

        Assert.Equal(Color.Parse("#0A0B0D"), Resource("PbBgColor"));
        Assert.Same(accentBrush, Application.Current.Resources["PbAccentBrush"]);     // untouched: the same object, not a re-created equal one
        Assert.Same(textBrush, Application.Current.Resources["PbTextBrush"]);

        service.SetTrueBlackReaderSuspend(false);
        Assert.Equal(Colors.Black, Resource("PbBgColor"));
    }

    [Fact]
    public void AV2Family_ListsOneEntryPerVariant_AppliesByComposedKey_AndPersistsIt()
    {
        InstallExtracted("paper", """
            { "schemaVersion": 2, "id": "org.test.paper", "name": "Paper",
              "base": { "palette": { "canvas": "#F7F3EC", "ink": "#2A2620", "accent": "#985A27" } },
              "variants": { "light": { "mode": "Light" }, "dark": { "mode": "Dark", "palette": { "canvas": "#12100D", "ink": "#EDE0C8" } } } }
            """);
        var service = CreateService();

        var entries = service.GetAvailableThemes().Where(t => t.FamilyKey == "paper").ToList();
        Assert.Equal(new[] { "paper:light", "paper:dark" }, entries.Select(t => t.Key));
        Assert.Equal(new[] { "Paper · Light", "Paper · Dark" }, entries.Select(t => t.Name));
        Assert.All(entries, e => Assert.Equal(2, e.VariantCount));

        service.ApplyTheme("paper:dark");

        Assert.Equal("paper:dark", ActiveKey());
        Assert.Equal(Color.Parse("#12100D"), Resource("PbBgColor"));
        Assert.Equal(Avalonia.Styling.ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        Assert.True(service.GetAvailableThemes().Single(t => t.Key == "paper:dark").IsActive);
        Assert.Equal("Paper · Dark", service.LoadTheme("paper:dark").Name);
    }

    [Fact]
    public void TrueBlack_UsesTheFamilysDesignedOledVariant_WhenItHasOne()
    {
        InstallExtracted("ink", """
            { "schemaVersion": 2, "id": "org.test.ink", "name": "Ink",
              "base": { "palette": { "canvas": "#14161A", "ink": "#EEEEEE", "accent": "#55AAFF" } },
              "variants": { "dark": { "mode": "Dark" }, "oled": { "mode": "Dark", "palette": { "canvas": "#000000" }, "semantic": { "surface.raised": "#0B0B0B" } } } }
            """);
        var service = CreateService();
        service.ApplyTheme("ink:dark");

        service.SetTrueBlackDark(true);

        Assert.Equal("ink:dark", ActiveKey());                                   // the persisted choice does not change
        Assert.Equal("oled", service.Current!.VariantId);
        Assert.Equal(Colors.Black, Resource("PbBgColor"));
        Assert.Equal(Color.Parse("#0B0B0B"), Resource("PbSurface2Color"));       // the designed hierarchy, not black-everything

        service.SetTrueBlackDark(false);
        Assert.Equal("dark", service.Current!.VariantId);
        Assert.Equal(Color.Parse("#14161A"), Resource("PbBgColor"));
    }
}
