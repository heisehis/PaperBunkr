using Avalonia;
using Avalonia.Media;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The two theme-mode resources the 2026-09-28 Home pitch adds (docs/superpowers/specs/2026-09-28-home-cosmetics-design.md C1/C2):
/// <c>PbHeroScrimColor</c> and <see cref="ThemeService.IsLightThemeActive"/> follow the applied theme's mode.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ThemeServiceHomeResourcesTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_theme_home_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ThemeServiceHomeResourcesTests()
    {
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
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
    public void ScrimAndLightFlag_FollowTheThemeMode()
    {
        var service = new ThemeService(() => new PaperbunkrDbContext(_dbOptions));
        var themes = service.GetAvailableThemes();
        string lightKey = themes.First(t => string.Equals(service.LoadTheme(t.Key).Mode, "Light", StringComparison.OrdinalIgnoreCase)).Key;
        string darkKey = themes.First(t => !string.Equals(service.LoadTheme(t.Key).Mode, "Light", StringComparison.OrdinalIgnoreCase)).Key;

        try
        {
            service.ApplyTheme(lightKey);
            Assert.True(ThemeService.IsLightThemeActive);
            Assert.Equal(Color.Parse("#1F000000"), Application.Current!.Resources["PbHeroScrimColor"]);

            service.ApplyTheme(darkKey);
            Assert.False(ThemeService.IsLightThemeActive);
            Assert.Equal(Color.Parse("#59000000"), Application.Current!.Resources["PbHeroScrimColor"]);
        }
        finally
        {
            service.ApplyTheme("default");
        }
    }
}
