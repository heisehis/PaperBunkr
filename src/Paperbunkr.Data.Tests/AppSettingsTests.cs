using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the <see cref="Entities.AppSettings"/> singleton-row pattern
/// (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md §1).
/// </summary>
public class AppSettingsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly PaperbunkrDbContext _context;

    public AppSettingsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_appsettings_test_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(_options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void GetOrCreateAppSettings_CreatesRowWithDefaults_OnFirstAccess()
    {
        var settings = _context.GetOrCreateAppSettings();

        Assert.Equal(1, settings.Id);
        Assert.Equal("default", settings.ActiveSkinKey);
        Assert.Null(settings.SelectedFontFamily);
        Assert.True(settings.OpenLastPage);
        Assert.True(settings.AutoNavigateComics);
        Assert.True(settings.ReverseRtlNavigation);
        Assert.True(settings.HighQualityPageDisplay);
    }

    /// <summary>
    /// New columns from migration <c>AddReaderPolishSettings</c> (docs/superpowers/specs/
    /// 2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §11) round-trip with
    /// their CE-verified defaults - the standard "existing rows come back with new columns at their
    /// defaults" migration test (spec §13), same shape as the other `GetOrCreateAppSettings_*` cases
    /// above rather than a separate file, since this is the same singleton row/pattern.
    /// </summary>
    [Fact]
    public void GetOrCreateAppSettings_ReaderPolishFields_DefaultToCeVerifiedValues()
    {
        var settings = _context.GetOrCreateAppSettings();

        Assert.Equal(2.0, settings.MagnifierZoom);
        Assert.Equal(1.0, settings.MagnifierOpacity);
        Assert.Equal(200, settings.MagnifierSizePixels);
        Assert.Equal(0.0, settings.DefaultBrightness);
        Assert.Equal(0.0, settings.DefaultContrast);
        Assert.Equal(0.0, settings.DefaultSaturation);
        Assert.Equal(0.0, settings.DefaultGamma);
        Assert.Equal(Entities.ImageBackgroundMode.Color, settings.ImageBackgroundMode);
        Assert.Equal("WhiteSmoke", settings.BackgroundColor);
        Assert.False(settings.PageMarginEnabled);
        Assert.Equal(0.05, settings.PageMarginPercentWidth);
        Assert.True(settings.ShowScrubberOverlay);
    }

    [Fact]
    public void GetOrCreateAppSettings_ReaderPolishFields_PersistAcrossContexts()
    {
        var settings = _context.GetOrCreateAppSettings();
        settings.MagnifierZoom = 3.5;
        settings.ImageBackgroundMode = Entities.ImageBackgroundMode.Auto;
        settings.BackgroundColor = "Black";
        settings.PageMarginEnabled = true;
        settings.ShowScrubberOverlay = false;
        _context.SaveChanges();

        using var freshContext = new PaperbunkrDbContext(_options);
        var reloaded = freshContext.GetOrCreateAppSettings();

        Assert.Equal(3.5, reloaded.MagnifierZoom);
        Assert.Equal(Entities.ImageBackgroundMode.Auto, reloaded.ImageBackgroundMode);
        Assert.Equal("Black", reloaded.BackgroundColor);
        Assert.True(reloaded.PageMarginEnabled);
        Assert.False(reloaded.ShowScrubberOverlay);
    }

    [Fact]
    public void GetOrCreateAppSettings_BehaviorFlags_PersistAcrossContexts()
    {
        var settings = _context.GetOrCreateAppSettings();
        settings.OpenLastPage = false;
        settings.AutoNavigateComics = false;
        settings.ReverseRtlNavigation = false;
        settings.HighQualityPageDisplay = false;
        _context.SaveChanges();

        using var freshContext = new PaperbunkrDbContext(_options);
        var reloaded = freshContext.GetOrCreateAppSettings();

        Assert.False(reloaded.OpenLastPage);
        Assert.False(reloaded.AutoNavigateComics);
        Assert.False(reloaded.ReverseRtlNavigation);
        Assert.False(reloaded.HighQualityPageDisplay);
    }

    [Fact]
    public void GetOrCreateAppSettings_IsIdempotent_DoesNotDuplicateRow()
    {
        var first = _context.GetOrCreateAppSettings();
        first.ActiveSkinKey = "windows_11";
        _context.SaveChanges();

        var second = _context.GetOrCreateAppSettings();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("windows_11", second.ActiveSkinKey);
        Assert.Single(_context.AppSettings);
    }
}
