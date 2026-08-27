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

    /// <summary>
    /// New columns from migration <c>AddPageTransitionSettings</c> (docs/superpowers/specs/
    /// 2026-08-13-reader-page-transition-animations-design.md §2) round-trip with their defaults -
    /// same "existing row comes back with new columns at their defaults" shape as
    /// <see cref="GetOrCreateAppSettings_ReaderPolishFields_DefaultToCeVerifiedValues"/> above.
    /// </summary>
    [Fact]
    public void GetOrCreateAppSettings_PageTransitionFields_DefaultToNoneAnd250()
    {
        var settings = _context.GetOrCreateAppSettings();

        Assert.Equal(Entities.PageTransitionStyle.None, settings.PageTransitionStyle);
        Assert.Equal(250, settings.PageTransitionDurationMs);
    }

    [Fact]
    public void GetOrCreateAppSettings_PageTransitionFields_PersistAcrossContexts()
    {
        var settings = _context.GetOrCreateAppSettings();
        settings.PageTransitionStyle = Entities.PageTransitionStyle.Crossfade;
        settings.PageTransitionDurationMs = 400;
        _context.SaveChanges();

        using var freshContext = new PaperbunkrDbContext(_options);
        var reloaded = freshContext.GetOrCreateAppSettings();

        Assert.Equal(Entities.PageTransitionStyle.Crossfade, reloaded.PageTransitionStyle);
        Assert.Equal(400, reloaded.PageTransitionDurationMs);
    }

    /// <summary>
    /// New column from migration <c>AddPageLayoutModeSettings</c> (docs/superpowers/specs/
    /// 2026-08-15-reader-double-page-spread-design.md §2) round-trips with its default.
    /// </summary>
    [Fact]
    public void GetOrCreateAppSettings_PageLayoutModeField_DefaultsToSingle()
    {
        var settings = _context.GetOrCreateAppSettings();

        Assert.Equal(Entities.PageLayoutMode.Single, settings.DefaultPageLayoutMode);
    }

    [Fact]
    public void GetOrCreateAppSettings_PageLayoutModeField_PersistsAcrossContexts()
    {
        var settings = _context.GetOrCreateAppSettings();
        settings.DefaultPageLayoutMode = Entities.PageLayoutMode.Double;
        _context.SaveChanges();

        using var freshContext = new PaperbunkrDbContext(_options);
        var reloaded = freshContext.GetOrCreateAppSettings();

        Assert.Equal(Entities.PageLayoutMode.Double, reloaded.DefaultPageLayoutMode);
    }

    /// <summary>
    /// New columns from migration <c>AddRenderingBackendSettings</c> (docs/superpowers/specs/
    /// 2026-08-27-hardware-accelerated-rendering-design.md) round-trip with their defaults - the
    /// standard "existing rows come back with new columns at their defaults" migration test, same
    /// shape as the other <c>GetOrCreateAppSettings_*</c> cases above.
    /// </summary>
    [Fact]
    public void GetOrCreateAppSettings_RenderingBackendFields_DefaultToAutoAndFalse()
    {
        var settings = _context.GetOrCreateAppSettings();

        Assert.Equal(Entities.RenderBackend.Auto, settings.RenderingBackend);
        Assert.False(settings.PreferNativeOpenGl);
    }

    [Fact]
    public void GetOrCreateAppSettings_RenderingBackendFields_PersistAcrossContexts()
    {
        var settings = _context.GetOrCreateAppSettings();
        settings.RenderingBackend = Entities.RenderBackend.Software;
        settings.PreferNativeOpenGl = true;
        _context.SaveChanges();

        using var freshContext = new PaperbunkrDbContext(_options);
        var reloaded = freshContext.GetOrCreateAppSettings();

        Assert.Equal(Entities.RenderBackend.Software, reloaded.RenderingBackend);
        Assert.True(reloaded.PreferNativeOpenGl);
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
