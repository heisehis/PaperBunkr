using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
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
/// The no-editor authoring path (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 6): the sample packages in
/// <c>docs/themes/samples</c> stay installable and valid, a theme exports exactly as installed, and the user's customizations travel as
/// their own file.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeAuthoringTests : IDisposable
{
    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pb_theme_authoring_{Guid.NewGuid():N}");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ThemeAuthoringTests()
    {
        TestAppBuilder.EnsureInitialized();
        Directory.CreateDirectory(_root);
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "a.db")}").Options;
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

    /// <summary>The theme as the app would show it: applied, then read back.</summary>
    private static ResolvedTheme? Resolve(ThemeService service, string key)
    {
        service.ApplyTheme(key);
        return service.Current;
    }

    private static string SamplesDirectory([CallerFilePath] string callerPath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, "..", "..", "..", "docs", "themes", "samples"));

    /// <summary>Zips a sample folder the way an author would, into a .crpck named after it.</summary>
    private string Pack(string sample)
    {
        string package = Path.Combine(_root, sample + ".crpck");
        File.Delete(package);
        ZipFile.CreateFromDirectory(Path.Combine(SamplesDirectory(), sample), package);
        return package;
    }

    // ===================== The sample packages =====================

    [Theory]
    [InlineData("harbor", 3)]
    [InlineData("gazette", 1)]
    public void ASamplePackage_InstallsWithNothingToReport_AndEveryVariantResolves(string sample, int variants)
    {
        var service = CreateService();

        var result = service.InstallThemePackage(Pack(sample));

        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity != ThemeDiagnosticSeverity.Information));

        var installed = service.GetAvailableThemes().Where(t => t.FamilyKey == sample).ToList();
        Assert.Equal(variants, installed.Count);
        foreach (var theme in installed)
        {
            var resolved = Resolve(service, theme.Key);
            Assert.NotNull(resolved);
            Assert.Empty(resolved!.Errors);
            Assert.True(theme.TextContrastOk, $"{theme.Name}: text contrast {theme.TextContrastText}");
        }
    }

    [Fact]
    public void TheTwoSamples_ArrangeHomeDifferently_AndBothLayoutsAreValid()
    {
        var service = CreateService();
        service.InstallThemePackage(Pack("harbor"));
        service.InstallThemePackage(Pack("gazette"));

        var harbor = Validate(Resolve(service, "harbor:dark")!, out var harborFindings);
        var gazette = Validate(Resolve(service, "gazette")!, out var gazetteFindings);

        Assert.Empty(harborFindings);
        Assert.Empty(gazetteFindings);
        Assert.Equal("Dock", harbor.Root.Type);
        Assert.Contains(harbor.Root.SelfAndDescendants(), n => n.Type == HomeLayoutContract.Search);
        Assert.DoesNotContain(harbor.Root.SelfAndDescendants(), n => n.Type == "Tabs");
        Assert.Contains(gazette.Root.SelfAndDescendants(), n => n.Type == "Tabs");
        Assert.Contains(gazette.Root.SelfAndDescendants(), n => n.Type == HomeLayoutContract.Masthead);
        Assert.Equal("narrow", harbor.BreakpointFor(800)?.Name);

        // Every variant of a family gets the family's layout.
        Assert.True(Resolve(service, "harbor:light")!.Layouts.ContainsKey("Home"));
        Assert.True(Resolve(service, "harbor:oled")!.Layouts.ContainsKey("Home"));
    }

    [Fact]
    public void TheSampleMotions_AreAllAccepted_AndEveryMotionALayoutNamesExists()
    {
        var service = CreateService();
        service.InstallThemePackage(Pack("harbor"));
        service.InstallThemePackage(Pack("gazette"));

        foreach (string key in new[] { "harbor:dark", "gazette" })
        {
            var theme = Resolve(service, key)!;
            ThemeMotionRuntime.Configure(theme.Motion, ThemeEffectQuality.High, reducedMotion: false);
            Assert.Empty(ThemeMotionRuntime.Diagnostics);

            var layout = Validate(theme, out _);
            foreach (string motion in layout.Root.SelfAndDescendants().Select(n => n.Text("motion")).OfType<string>())
            {
                Assert.NotNull(ThemeMotionRuntime.Find(motion));
            }
        }

        ThemeMotionRuntime.Configure(new Dictionary<string, JsonObject>(), ThemeEffectQuality.High, reducedMotion: false);
    }

    [Fact]
    public void TheOledSample_IsUsedAsWritten_AndTrueBlackLeavesItAlone()
    {
        var service = CreateService();
        service.InstallThemePackage(Pack("harbor"));

        var theme = Resolve(service, "harbor:oled")!;

        Assert.Equal(ThemeVariantKind.Oled, theme.Variant.Kind);
        Assert.Equal(Colors.Black, theme.Color("surface.canvas"));
        Assert.Equal(Color.Parse("#0A1112"), theme.Color("surface.raised"));        // the author's own near-black, not flattened
    }

    private static ThemeLayoutDocument Validate(ResolvedTheme theme, out List<ThemeDiagnostic> findings)
    {
        findings = new List<ThemeDiagnostic>();
        var document = ThemeLayoutValidator.Validate(theme.Layouts["Home"], HomeLayoutContract.Instance, findings);
        Assert.True(document is not null, string.Join(" | ", findings.Select(f => f.Message)));
        return document!;
    }

    // ===================== Export a theme =====================

    [Fact]
    public void AnInstalledTheme_ExportsExactlyAsInstalled_AndInstallsAgainFromTheExport()
    {
        var service = CreateService();
        service.InstallThemePackage(Pack("harbor"));
        string exported = Path.Combine(_root, "out", "harbor-copy.crpck");

        Assert.True(service.ExportThemePackage("harbor:light", exported, out string? error), error);      // any variant's key exports the whole family

        using (var archive = ZipFile.OpenRead(exported))
        {
            Assert.Equal(new[] { "layouts/home.json", "theme.json" }, archive.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal));
            using var reader = new StreamReader(archive.GetEntry("theme.json")!.Open());
            Assert.Equal(File.ReadAllText(Path.Combine(SamplesDirectory(), "harbor", "theme.json")), reader.ReadToEnd());
        }

        var again = service.InstallThemePackage(exported);
        Assert.True(again.Success, again.Error);
        Assert.Equal(3, service.GetAvailableThemes().Count(t => t.FamilyKey == "harbor-copy"));
    }

    [Fact]
    public void ABuiltInTheme_Exports_SoItCanBeATemplate_AndTheUsersOverridesAreNotInIt()
    {
        var service = CreateService();
        service.ApplyTheme("daylight");
        service.SetOverride(ThemeOverrideScope.Family, "action.primary", "#FF00FF");
        string exported = Path.Combine(_root, "daylight.crpck");

        Assert.True(service.ExportThemePackage("daylight", exported, out string? error), error);

        using var archive = ZipFile.OpenRead(exported);
        using var reader = new StreamReader(archive.GetEntry("theme.json")!.Open());
        string json = reader.ReadToEnd();
        Assert.Contains("Daylight", json);
        Assert.DoesNotContain("FF00FF", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportingAThemeThatDoesNotExist_SaysSo_AndWritesNothing()
    {
        string exported = Path.Combine(_root, "nothing.crpck");

        Assert.False(CreateService().ExportThemePackage("no_such_theme", exported, out string? error));

        Assert.NotNull(error);
        Assert.False(File.Exists(exported));
    }

    // ===================== The user's customizations as a file =====================

    [Fact]
    public void Customizations_ExportAndImport_RoundTrip_AndApplyStraightAway()
    {
        var first = CreateService();
        first.ApplyTheme("default");
        first.SetOverride(ThemeOverrideScope.Global, "metrics.radius", "11");
        first.SetOverride(ThemeOverrideScope.Family, "status.danger", "#AA0000");
        first.SetEffectQuality(ThemeEffectQuality.Balanced);
        first.SetAllowThemeLayouts(true);
        first.SetLayoutPatch("Home", "side", new JsonObject { ["width"] = 280 });
        string file = Path.Combine(_root, "mine.json");
        Assert.True(first.ExportCustomizations(file, out string? exportError), exportError);

        var second = CreateService();          // a different computer: nothing customized yet
        second.ApplyTheme("default");
        Assert.Null(second.GetOverride(ThemeOverrideScope.Global, "metrics.radius"));

        Assert.True(second.ImportCustomizations(file, out string? importError), importError);

        Assert.Equal("11", second.GetOverride(ThemeOverrideScope.Global, "metrics.radius"));
        Assert.Equal("#AA0000", second.GetOverride(ThemeOverrideScope.Family, "status.danger"));
        Assert.Equal(ThemeEffectQuality.Balanced, second.GetEffectQuality());
        Assert.True(second.GetAllowThemeLayouts());
        Assert.Equal(1, second.CountLayoutPatches());
        Assert.Equal(Color.Parse("#AA0000"), second.Current!.Color("status.danger"));      // applied live, not on the next start
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{ "version": 999, "global": { "metrics.radius": "40" } }""")]
    [InlineData("""{ "global": { "metrics.radius": "40" } }""")]
    public void AFileThatIsNotACustomizationsFile_IsRefused_AndNothingChanges(string content)
    {
        var service = CreateService();
        service.ApplyTheme("default");
        service.SetOverride(ThemeOverrideScope.Global, "metrics.radius", "9");
        string file = Path.Combine(_root, "bad.json");
        File.WriteAllText(file, content);

        Assert.False(service.ImportCustomizations(file, out string? error));

        Assert.NotNull(error);
        Assert.Equal("9", service.GetOverride(ThemeOverrideScope.Global, "metrics.radius"));
    }

    [Fact]
    public void AnImportedValueThatIsNotValidForItsToken_IsIgnored_NotApplied()
    {
        var service = CreateService();
        service.ApplyTheme("default");
        var before = service.Current!.Color("text.primary");
        string file = Path.Combine(_root, "odd.json");
        File.WriteAllText(file, """{ "version": 1, "global": { "text.primary": "banana", "metrics.radius": "12" } }""");

        Assert.True(service.ImportCustomizations(file, out _));

        Assert.Equal(before, service.Current!.Color("text.primary"));           // the theme's own text colour stands
        Assert.Equal(12, service.Current.Number("metrics.radius"));
    }

    // ===================== The Appearance controls =====================

    [Fact]
    public void TheCustomizePanel_ShowsAndSetsEffectQuality_AndThemeLayouts()
    {
        var service = CreateService();
        service.InstallThemePackage(Pack("harbor"));
        service.ApplyTheme("default");
        var panel = new ViewModels.ThemeCustomizationViewModel(service, () => null, _ => { });

        Assert.Equal("High", panel.EffectQualityText);
        Assert.False(panel.AllowThemeLayouts);
        Assert.Equal("This theme does not rearrange any screen.", panel.LayoutSummary);

        panel.EffectQualityText = "Performance";
        Assert.Equal(ThemeEffectQuality.Performance, service.GetEffectQuality());

        service.ApplyTheme("harbor:dark");
        panel.Refresh();
        Assert.Equal("This theme can rearrange Home.", panel.LayoutSummary);

        panel.AllowThemeLayouts = true;
        Assert.True(service.GetAllowThemeLayouts());
        Assert.Equal("This theme rearranges Home.", panel.LayoutSummary);

        service.SetLayoutPatch("Home", "side", new JsonObject { ["width"] = 260 });
        Assert.True(panel.HasLayoutAdjustments);
        panel.ResetLayoutCommand.Execute(null);
        Assert.False(panel.HasLayoutAdjustments);
        Assert.Equal(0, service.CountLayoutPatches());
    }
}
