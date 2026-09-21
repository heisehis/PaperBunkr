namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="AppDataPaths"/> (docs/superpowers/specs/2026-09-19-remote-library-sharing-design.md
/// §11 P0): with no override every path resolves exactly where it did before the helper existed;
/// with one, everything - including the database - moves under it.
/// </summary>
public class AppDataPathsTests : IDisposable
{
    private readonly string? _savedRoot = AppDataPaths.RootOverride;
    private readonly string? _savedDb = PaperbunkrDbContext.DatabasePathOverride;

    public void Dispose()
    {
        AppDataPaths.RootOverride = _savedRoot;
        PaperbunkrDbContext.DatabasePathOverride = _savedDb;
    }

    [Fact]
    public void Root_WithoutOverride_IsRoamingAppDataPaperbunkr()
    {
        AppDataPaths.RootOverride = null;

        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Paperbunkr");

        Assert.Equal(expected, AppDataPaths.Root);
        Assert.Equal(Path.Combine(expected, "thumbnails"), AppDataPaths.Combine("thumbnails"));
    }

    [Fact]
    public void LocalRoot_WithoutOverride_IsLocalAppDataPaperbunkr()
    {
        AppDataPaths.RootOverride = null;

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paperbunkr"),
            AppDataPaths.LocalRoot);
    }

    [Fact]
    public void Override_RedirectsRootLocalRootAndCombine()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_datadir_{Guid.NewGuid():N}");
        AppDataPaths.RootOverride = dir;

        Assert.Equal(dir, AppDataPaths.Root);
        Assert.Equal(dir, AppDataPaths.LocalRoot);
        Assert.Equal(Path.Combine(dir, "backups"), AppDataPaths.Combine("backups"));
        Assert.Equal(Path.Combine(dir, "a", "b.json"), AppDataPaths.Combine("a", "b.json"));
    }

    [Fact]
    public void DefaultDatabasePath_FollowsDataDirOverride()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_datadir_{Guid.NewGuid():N}");
        AppDataPaths.RootOverride = dir;
        PaperbunkrDbContext.DatabasePathOverride = null;

        try
        {
            Assert.Equal(Path.Combine(dir, "paperbunkr.db"), PaperbunkrDbContext.GetDefaultDatabasePath());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DatabasePathOverride_StillWinsOverDataDir()
    {
        AppDataPaths.RootOverride = Path.Combine(Path.GetTempPath(), $"paperbunkr_datadir_{Guid.NewGuid():N}");
        string explicitDb = Path.Combine(Path.GetTempPath(), "explicit.db");
        PaperbunkrDbContext.DatabasePathOverride = explicitDb;

        Assert.Equal(explicitDb, PaperbunkrDbContext.GetDefaultDatabasePath());
    }
}
