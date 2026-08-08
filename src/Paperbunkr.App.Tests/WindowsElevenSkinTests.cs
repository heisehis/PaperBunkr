using System.IO.Compression;
using Avalonia.Media;
using Avalonia.Platform;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Proves the "windows_11" reference skin (docs/superpowers/specs/2026-08-07-preferences-skin-system-design.md
/// §5's deferred follow-up, colors sourced from docs/open_items_resolved.md §7's real Fluent
/// Design token research - accent #0078D4, success #107C10, etc.) is a genuinely valid, installable
/// `.crpck` with real alternate values, not just the synthetic fixture <see cref="SkinServiceTests"/>
/// uses. Packages the actual checked-in <c>Assets/Skins/windows_11/theme.json</c> (read via
/// <c>AssetLoader</c>, same avares:// mechanism as the built-in default skin) into a real ZIP so a
/// content edit to that file is what this test actually exercises - no separately hand-duplicated
/// copy to drift out of sync.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class WindowsElevenSkinTests : IDisposable
{
    private readonly string _originalInstalledDirectory;
    private readonly string _originalExtractedDirectory;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _crpckPath;

    public WindowsElevenSkinTests()
    {
        _originalInstalledDirectory = SkinPaths.InstalledDirectory;
        _originalExtractedDirectory = SkinPaths.ExtractedDirectory;

        string root = Path.Combine(Path.GetTempPath(), $"paperbunkr_win11_test_{Guid.NewGuid():N}");
        SkinPaths.InstalledDirectory = Path.Combine(root, "skins");
        SkinPaths.ExtractedDirectory = Path.Combine(root, "skins-extracted");

        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_win11_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Database.EnsureCreated();
        }

        string fixtureDir = Path.Combine(Path.GetTempPath(), $"paperbunkr_win11_fixture_{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureDir);
        // Filename (sans extension) becomes the installed skin's key - must be exactly "windows_11",
        // not a randomized name, so it round-trips to the real reference-skin key.
        _crpckPath = Path.Combine(fixtureDir, "windows_11.crpck");
        using var themeStream = AssetLoader.Open(new Uri("avares://Paperbunkr.App/Assets/Skins/windows_11/theme.json"));
        using var zipStream = new FileStream(_crpckPath, FileMode.Create);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
        using var entryStream = archive.CreateEntry("theme.json").Open();
        themeStream.CopyTo(entryStream);
    }

    public void Dispose()
    {
        SkinPaths.InstalledDirectory = _originalInstalledDirectory;
        SkinPaths.ExtractedDirectory = _originalExtractedDirectory;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            string? fixtureDir = Path.GetDirectoryName(_crpckPath);
            if (fixtureDir is not null && Directory.Exists(fixtureDir)) Directory.Delete(fixtureDir, recursive: true);
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Windows11Skin_InstallsAndAppliesWithRealAlternateValues()
    {
        var service = new SkinService(() => new PaperbunkrDbContext(_dbOptions));

        bool installed = service.TryInstallSkin(_crpckPath, out string? error);
        Assert.True(installed);
        Assert.Null(error);

        var theme = service.LoadSkin("windows_11");
        Assert.Equal("Windows 11", theme.Name);
        Assert.Equal(8, theme.Radius);

        var skins = service.GetAvailableSkins();
        Assert.Contains(skins, s => s.Key == "windows_11" && s.Name == "Windows 11");

        service.ApplySkin("windows_11");

        var resources = Avalonia.Application.Current!.Resources;
        Assert.Equal(Color.Parse("#F3F3F3"), resources["PbBgColor"]);
        Assert.Equal(Color.Parse("#0078D4"), resources["PbAccentColor"]);
        Assert.Equal(Color.Parse("#107C10"), resources["PbSuccessColor"]);
        Assert.Equal(8.0, resources["PbRadius"]);

        Assert.True(service.GetAvailableSkins().Single(s => s.Key == "windows_11").IsActive);
    }
}
