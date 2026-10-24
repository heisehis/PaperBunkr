using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Theming;
using Paperbunkr.App.Theming.Layout;
using Paperbunkr.App.Theming.Motion;
using Paperbunkr.App.Theming.Runtime;
using Paperbunkr.App.Views.Home;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests.Theming;

/// <summary>
/// The ten version 2 built-in families (Nordic Frost, Tokyo Midnight, Reading Room, Canopy, Crimson Ink, Abyss, Sunset Boulevard, Newsprint,
/// Arcade, Graphite): every variant resolves with nothing to report, reads clearly, applies, and the two Home layouts are valid.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeV2BuiltInTests : IDisposable
{
    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pb_theme_v2_builtin_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ThemeV2BuiltInTests()
    {
        TestAppBuilder.EnsureInitialized();
        Directory.CreateDirectory(_root);
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "b.db")}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            CreateService().ApplyTheme("default");
        }
        catch (InvalidOperationException)
        {
        }

        ThemeLayoutSource.Clear();
        ThemeMotionRuntime.Configure(new Dictionary<string, System.Text.Json.Nodes.JsonObject>(), ThemeEffectQuality.High, reducedMotion: false);
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

    private ThemeService CreateService() => new(() => new PaperbunkrDbContext(_dbOptions));

    public static TheoryData<string, int> Families => new()
    {
        { "nordic_frost", 2 }, { "tokyo_midnight", 2 }, { "reading_room", 2 }, { "canopy", 2 }, { "crimson_ink", 1 },
        { "abyss", 2 }, { "sunset_boulevard", 2 }, { "newsprint", 1 }, { "arcade", 1 }, { "graphite", 3 },
    };

    [Theory]
    [MemberData(nameof(Families))]
    public void EveryVariant_ResolvesWithNothingToReport_ReadsClearly_AndApplies(string family, int variants)
    {
        var service = CreateService();
        var entries = service.GetAvailableThemes().Where(t => t.FamilyKey == family).ToList();

        Assert.Equal(variants, entries.Count);
        foreach (var entry in entries)
        {
            Assert.True(entry.IsBuiltIn);
            Assert.False(entry.IsLegacy);

            service.ApplyTheme(entry.Key);
            var theme = service.Current!;

            Assert.Equal(entry.Key, theme.Document.ThemeKeyFor(theme.Variant));
            var findings = service.GetActiveDiagnostics().Where(d => d.Severity != ThemeDiagnosticSeverity.Information).Select(d => $"{entry.Key}: {d.Message}").ToList();
            Assert.True(findings.Count == 0, string.Join(" | ", findings));
            Assert.True(entry.TextContrast >= 7, $"{entry.Key}: text contrast is {entry.TextContrastText}");
        }
    }

    [Fact]
    public void TheOledVariants_AreBlack_AndKeepTheirOwnNearBlackSurfaces()
    {
        var service = CreateService();
        foreach (string key in new[] { "tokyo_midnight:oled", "abyss:oled", "graphite:oled" })
        {
            service.ApplyTheme(key);

            Assert.Equal(ThemeVariantKind.Oled, service.Current!.Variant.Kind);
            Assert.Equal(Colors.Black, service.Current.Color("surface.canvas"));
            Assert.NotEqual(Colors.Black, service.Current.Color("surface.raised"));
        }
    }

    [Theory]
    [InlineData("newsprint", "Tabs")]
    [InlineData("arcade", "Home.Search")]
    public void TheTwoHomeLayouts_AreValid_AndTheirMotionsExist(string key, string expectedNode)
    {
        var service = CreateService();
        service.ApplyTheme(key);
        var theme = service.Current!;
        var findings = new List<ThemeDiagnostic>();

        var layout = ThemeLayoutValidator.Validate(theme.Layouts["Home"], HomeLayoutContract.Instance, findings);

        Assert.True(layout is not null && findings.Count == 0, string.Join(" | ", findings.Select(f => f.Message)));
        Assert.Contains(layout!.Root.SelfAndDescendants(), n => n.Type == expectedNode);
        Assert.Empty(ThemeMotionRuntime.Diagnostics);
        foreach (string motion in layout.Root.SelfAndDescendants().Select(n => n.Text("motion")).OfType<string>())
        {
            Assert.NotNull(ThemeMotionRuntime.Find(motion));
        }

        Assert.False(ThemeLayoutSource.Enabled);            // shipping a layout does not switch layouts on
    }

    [Fact]
    public void AutoSwitch_HasALightAndADarkToMoveBetween_InEveryTwoModeFamily()
    {
        var themes = CreateService().GetAvailableThemes();
        foreach (string family in new[] { "nordic_frost", "reading_room", "canopy", "sunset_boulevard", "graphite" })
        {
            var modes = themes.Where(t => t.FamilyKey == family).Select(t => t.Mode).Distinct().ToList();
            Assert.Contains("Light", modes);
            Assert.Contains("Dark", modes);
        }
    }
}
