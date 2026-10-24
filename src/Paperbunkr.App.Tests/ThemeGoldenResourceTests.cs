using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The equivalence oracle for the theme-system v2 work (docs/superpowers/specs/2026-10-12-theme-system-v2-gap-analysis-and-plan.md, phase 0):
/// for each built-in theme, apply it and compare every <c>Pb*</c> resource it writes with a checked-in snapshot taken before the v2 refactor.
/// A later phase may ADD keys (new semantic roles); it may not change the value of a key that is in the snapshot. Regenerate deliberately with
/// <c>PB_UPDATE_GOLDENS=1</c> and review the diff.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeGoldenResourceTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _originalInstalledDirectory = ThemePaths.InstalledDirectory;
    private readonly string _originalExtractedDirectory = ThemePaths.ExtractedDirectory;
    private readonly string? _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
    private readonly string _root;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ThemeGoldenResourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_golden_{Guid.NewGuid():N}");
        ThemePaths.InstalledDirectory = Path.Combine(_root, "skins");
        ThemePaths.ExtractedDirectory = Path.Combine(_root, "skins-extracted");
        Directory.CreateDirectory(_root);

        _dbPath = Path.Combine(_root, "golden.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        // The user-override file lives beside the database, so this keeps a developer's real overrides out of the snapshot.
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;

        // The glow tier is process-wide state in ThemeService; an earlier test in this collection may have left it on another tier.
        new ThemeService(() => new PaperbunkrDbContext(_dbOptions)).ApplyGlowTier(ThemeService.GlowTierNormal);
    }

    public void Dispose()
    {
        ThemePaths.InstalledDirectory = _originalInstalledDirectory;
        ThemePaths.ExtractedDirectory = _originalExtractedDirectory;
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

    public static TheoryData<string> BuiltInKeys => new()
    {
        "default", "windows_11", "cool_technical", "vibrant_pop", "vintage_paperback",
        "daylight", "overcast", "maximum_contrast", "colorblind_safe", "matrix",
    };

    [Theory]
    [MemberData(nameof(BuiltInKeys))]
    public void ApplyTheme_WritesTheSameResourcesAsTheSnapshot(string key)
    {
        var service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));

        var snapshot = SnapshotAfter(() => service.ApplyTheme(key));

        AssertMatchesGolden(key, snapshot);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("cool_technical")]
    public void TrueBlack_WritesTheSameResourcesAsTheSnapshot(string key)
    {
        var service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));

        var snapshot = SnapshotAfter(() =>
        {
            service.ApplyTheme(key);
            service.SetTrueBlackDark(true);
        });

        AssertMatchesGolden($"{key}.trueblack", snapshot);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("daylight")]
    public void AccentOverride_WritesTheSameResourcesAsTheSnapshot(string key)
    {
        var service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));

        var snapshot = SnapshotAfter(() =>
        {
            service.ApplyTheme(key);
            service.SetAccentOverrideHex("#3366CC");
        });

        AssertMatchesGolden($"{key}.accent", snapshot);
    }

    /// <summary>
    /// Runs <paramref name="act"/> against a resource dictionary with every <c>Pb*</c> key removed first, so a value left behind by an earlier test
    /// (a theme that set scrollbar tokens, say) cannot leak into the snapshot. Keys the action did not write are put back afterwards, so the
    /// tests that run later in this collection still see the motion and density tokens they expect.
    /// </summary>
    private static SortedDictionary<string, string> SnapshotAfter(Action act)
    {
        var resources = Application.Current!.Resources;
        var removed = new Dictionary<string, object?>();
        foreach (var key in resources.Keys.OfType<string>().Where(k => k.StartsWith("Pb", StringComparison.Ordinal)).ToList())
        {
            removed[key] = resources[key];
            resources.Remove(key);
        }

        try
        {
            act();
            return Snapshot();
        }
        finally
        {
            foreach (var (key, value) in removed)
            {
                if (!resources.ContainsKey(key))
                {
                    resources[key] = value;
                }
            }
        }
    }

    private static SortedDictionary<string, string> Snapshot()
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var resources = Application.Current!.Resources;
        foreach (var key in resources.Keys.OfType<string>().Where(k => k.StartsWith("Pb", StringComparison.Ordinal)))
        {
            result[key] = Describe(resources[key]);
        }

        result["@RequestedThemeVariant"] = Application.Current.RequestedThemeVariant?.ToString() ?? "null";
        return result;
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        Color color => "color:" + Hex(color),
        ISolidColorBrush brush => "brush:" + Hex(brush.Color) + (Math.Abs(brush.Opacity - 1) > 0.0001 ? "@" + brush.Opacity.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty),
        CornerRadius radius => string.Create(CultureInfo.InvariantCulture, $"radius:{radius.TopLeft},{radius.TopRight},{radius.BottomRight},{radius.BottomLeft}"),
        Thickness thickness => string.Create(CultureInfo.InvariantCulture, $"thickness:{thickness.Left},{thickness.Top},{thickness.Right},{thickness.Bottom}"),
        BoxShadows shadows => "shadows:" + shadows.ToString(),
        FontFamily font => "font:" + font.ToString(),
        TimeSpan span => "time:" + span.TotalMilliseconds.ToString(CultureInfo.InvariantCulture),
        double number => "double:" + number.ToString("R", CultureInfo.InvariantCulture),
        bool flag => "bool:" + (flag ? "true" : "false"),
        IFormattable formattable => value.GetType().Name + ":" + formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.GetType().Name + ":" + value,
    };

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static void AssertMatchesGolden(string name, SortedDictionary<string, string> actual, [CallerFilePath] string callerPath = "")
    {
        string goldenPath = Path.Combine(Path.GetDirectoryName(callerPath)!, "Goldens", "theme-resources", name + ".json");

        if (Environment.GetEnvironmentVariable("PB_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllText(goldenPath, JsonSerializer.Serialize(actual, JsonOptions));
            return;
        }

        Assert.True(File.Exists(goldenPath), $"No snapshot for '{name}'. Run once with PB_UPDATE_GOLDENS=1 to create {goldenPath}.");
        var golden = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(goldenPath))!;

        var differences = new List<string>();
        foreach (var (key, expected) in golden)
        {
            if (!actual.TryGetValue(key, out var got))
            {
                differences.Add($"{key}: missing (snapshot has {expected})");
            }
            else if (!string.Equals(got, expected, StringComparison.Ordinal))
            {
                differences.Add($"{key}: expected {expected} but was {got}");
            }
        }

        Assert.True(differences.Count == 0, $"Theme '{name}' no longer matches its snapshot:{Environment.NewLine}{string.Join(Environment.NewLine, differences)}");
    }
}
