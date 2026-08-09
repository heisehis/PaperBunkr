using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="SkinService"/> (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §3). Joins <see cref="AvaloniaTestCollection"/> since <c>ApplySkin</c> touches
/// <c>Application.Current.Resources</c>. Redirects <see cref="SkinPaths"/> to a temp folder and
/// uses an injected in-memory-database context factory (the same test-injection seam as
/// <see cref="CoverThumbnailService"/>) so tests never touch the real per-user skins folder or database.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class SkinServiceTests : IDisposable
{
    private readonly string _originalInstalledDirectory;
    private readonly string _originalExtractedDirectory;
    private readonly string _installedDirectory;
    private readonly string _extractedDirectory;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public SkinServiceTests()
    {
        _originalInstalledDirectory = SkinPaths.InstalledDirectory;
        _originalExtractedDirectory = SkinPaths.ExtractedDirectory;

        string root = Path.Combine(Path.GetTempPath(), $"paperbunkr_skins_test_{Guid.NewGuid():N}");
        _installedDirectory = Path.Combine(root, "skins");
        _extractedDirectory = Path.Combine(root, "skins-extracted");
        SkinPaths.InstalledDirectory = _installedDirectory;
        SkinPaths.ExtractedDirectory = _extractedDirectory;

        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_skins_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SkinPaths.InstalledDirectory = _originalInstalledDirectory;
        SkinPaths.ExtractedDirectory = _originalExtractedDirectory;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(Path.GetDirectoryName(_installedDirectory)))
            {
                Directory.Delete(Path.GetDirectoryName(_installedDirectory)!, recursive: true);
            }

            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private SkinService CreateService() => new(() => new PaperbunkrDbContext(_dbOptions));

    /// <summary>Builds a real .crpck (plain ZIP) fixture, same "generate via the real code path" precedent as <see cref="CbzFixture"/>.</summary>
    private static string CreateCrpckFixture(string key, string themeJson)
    {
        string crpckPath = Path.Combine(Path.GetTempPath(), $"{key}_{Guid.NewGuid():N}.crpck");
        using (var stream = new FileStream(crpckPath, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("theme.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(themeJson);
        }

        return crpckPath;
    }

    [Fact]
    public void GetAvailableSkins_ListsBuiltInDefault_WhenNothingInstalled()
    {
        var service = CreateService();

        var skins = service.GetAvailableSkins();

        Assert.Single(skins);
        Assert.Equal(SkinService.DefaultSkinKey, skins[0].Key);
        Assert.True(skins[0].IsActive);
    }

    [Fact]
    public void TryInstallSkin_RoundTrips_InstallExtractParseApply()
    {
        string crpckPath = CreateCrpckFixture("windows_11", """
            {"name":"Windows 11","colors":{"bg":"#111111","chrome":"#222222","border":"#333333","text":"#EEEEEE","textMuted":"#CCCCCC","textFaint":"#999999","accent":"#4488FF","accentText":"#66AAFF","accentSoft":"#204488FF","badge":"#FFAA00","badgeText":"#000000","success":"#33AA55"},"spacingUnit":4,"radius":8,"icons":{}}
            """);

        try
        {
            var service = CreateService();

            bool installed = service.TryInstallSkin(crpckPath, out string? error);
            Assert.True(installed);
            Assert.Null(error);

            string key = Path.GetFileNameWithoutExtension(crpckPath);
            var skins = service.GetAvailableSkins();
            Assert.Contains(skins, s => s.Key == key && s.Name == "Windows 11");

            service.ApplySkin(key);

            var refreshed = service.GetAvailableSkins();
            Assert.True(refreshed.Single(s => s.Key == key).IsActive);
            Assert.False(refreshed.Single(s => s.Key == SkinService.DefaultSkinKey).IsActive);

            var resources = Avalonia.Application.Current!.Resources;
            Assert.Equal(Avalonia.Media.Color.Parse("#111111"), resources["PbBgColor"]);
        }
        finally
        {
            if (File.Exists(crpckPath)) File.Delete(crpckPath);
        }
    }

    [Fact]
    public void TryInstallSkin_RejectsMalformedThemeJson_WithoutCrashing()
    {
        string crpckPath = CreateCrpckFixture("broken", "{ not valid json");

        try
        {
            var service = CreateService();

            bool installed = service.TryInstallSkin(crpckPath, out string? error);

            Assert.False(installed);
            Assert.NotNull(error);

            string key = Path.GetFileNameWithoutExtension(crpckPath);
            Assert.DoesNotContain(service.GetAvailableSkins(), s => s.Key == key);
        }
        finally
        {
            if (File.Exists(crpckPath)) File.Delete(crpckPath);
        }
    }

    [Fact]
    public void GetIcon_ReturnsNull_ForUndefinedIcon()
    {
        var service = CreateService();

        var icon = service.GetIcon(SkinService.DefaultSkinKey, "does-not-exist");

        Assert.Null(icon);
    }
}
