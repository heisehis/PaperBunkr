using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Gcd;
using Paperbunkr.App.Services.Scheduling;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.App.Tests.Gcd;

/// <summary>
/// Installing, updating and removing the Grand Comics Database data, and the Preferences row (docs/superpowers/specs/
/// 2026-09-27-gcd-data-design.md §2). A fake HTTP handler serves a tiny extract; nothing touches the network or %AppData%.
/// </summary>
public class GcdDataInstallerTests : IDisposable
{
    private const string ManifestUrl = "https://example.test/gcd-data.json";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_gcd_install_test_{Guid.NewGuid():N}");
    private readonly string _dbPath;

    public GcdDataInstallerTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "library.db");
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string DataFolder => Path.Combine(_root, "gcd");

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private GcdDataInstaller Installer(FakeGcdHttp http) => new(new HttpClient(http), DataFolder, ManifestUrl);

    [Fact]
    public async Task Install_GoodDownload_IsSwappedIn()
    {
        using var extract = new GcdTestExtract("2026-09-15").Series(1, "Hulk", 2008);
        var (zip, manifest) = extract.Package();
        var installer = Installer(new FakeGcdHttp().File(manifest.Url, zip));
        var reported = new List<(long, long)>();

        await installer.InstallAsync(manifest, new SyncProgress<(long, long)>(reported.Add), CancellationToken.None);

        Assert.True(installer.IsInstalled);
        Assert.Equal("2026-09-15", installer.InstalledDumpDate());
        Assert.Equal((zip.Length, zip.Length), reported[^1]);
        Assert.False(File.Exists(DataFolder + ".download.zip"));
        Assert.False(Directory.Exists(DataFolder + ".staging"));
    }

    [Fact]
    public async Task Install_Update_ReplacesTheOldData()
    {
        using var first = new GcdTestExtract("2026-09-15");
        using var second = new GcdTestExtract("2026-10-15");
        var (zip1, m1) = first.Package();
        var (zip2, m2) = second.Package();
        m2 = m2 with { Url = "https://example.test/gcd2.zip" };
        var installer = Installer(new FakeGcdHttp().File(m1.Url, zip1).File(m2.Url, zip2));

        await installer.InstallAsync(m1, null, CancellationToken.None);
        await installer.InstallAsync(m2, null, CancellationToken.None);

        Assert.Equal("2026-10-15", installer.InstalledDumpDate());
        Assert.False(Directory.Exists(DataFolder + ".old"));
    }

    [Fact]
    public async Task Install_HashMismatch_IsRejected_AndNothingIsInstalled()
    {
        using var extract = new GcdTestExtract();
        var (zip, manifest) = extract.Package(corruptHash: true);
        var installer = Installer(new FakeGcdHttp().File(manifest.Url, zip));

        var ex = await Assert.ThrowsAsync<GcdInstallException>(() => installer.InstallAsync(manifest, null, CancellationToken.None));

        Assert.Contains("checksum", ex.Message);
        Assert.False(installer.IsInstalled);
        Assert.False(File.Exists(DataFolder + ".download.zip"));
    }

    [Fact]
    public async Task Install_WrongSize_IsRejected()
    {
        using var extract = new GcdTestExtract();
        var (zip, manifest) = extract.Package();
        var installer = Installer(new FakeGcdHttp().File(manifest.Url, zip));

        await Assert.ThrowsAsync<GcdInstallException>(() => installer.InstallAsync(manifest with { SizeBytes = zip.Length + 10 }, null, CancellationToken.None));
        await Assert.ThrowsAsync<GcdInstallException>(() => installer.InstallAsync(manifest with { SizeBytes = zip.Length - 10 }, null, CancellationToken.None));
        Assert.False(installer.IsInstalled);
    }

    [Fact]
    public async Task Install_OtherSchema_IsRejected_AndKeepsWhatWasThere()
    {
        using var good = new GcdTestExtract("2026-09-15");
        using var future = new GcdTestExtract("2026-12-01", schemaVersion: 99);
        var (zip1, m1) = good.Package();
        var (zip2, m2) = future.Package();
        m2 = m2 with { Url = "https://example.test/future.zip" };
        var installer = Installer(new FakeGcdHttp().File(m1.Url, zip1).File(m2.Url, zip2));
        await installer.InstallAsync(m1, null, CancellationToken.None);

        await Assert.ThrowsAsync<GcdInstallException>(() => installer.InstallAsync(m2, null, CancellationToken.None));

        Assert.Equal("2026-09-15", installer.InstalledDumpDate());
    }

    [Fact]
    public async Task FetchManifest_ReturnsValidOnes_NullOtherwise()
    {
        using var extract = new GcdTestExtract();
        var (_, manifest) = extract.Package();

        Assert.Equal(manifest, await Installer(new FakeGcdHttp().Manifest(manifest)).FetchManifestAsync(CancellationToken.None));
        Assert.Null(await Installer(new FakeGcdHttp()).FetchManifestAsync(CancellationToken.None));
        Assert.Null(await Installer(new FakeGcdHttp().Manifest(manifest with { SchemaVersion = 99 })).FetchManifestAsync(CancellationToken.None));
        Assert.Null(await Installer(new FakeGcdHttp().Manifest(manifest with { Url = "http://example.test/gcd.zip" })).FetchManifestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Remove_DeletesTheData_AndForgetsEveryGcdLink()
    {
        using var extract = new GcdTestExtract().Series(1, "Hulk", 1968).Series(2, "Hulk", 2008).Bond(1, 2, "major_name_numbering_continues");
        var (zip, manifest) = extract.Package();
        var installer = Installer(new FakeGcdHttp().File(manifest.Url, zip));
        await installer.InstallAsync(manifest, null, CancellationToken.None);
        using (var context = NewContext())
        {
            context.Series.AddRange(
                new Series { Name = "Incredible Hulk (1968)", GcdSeriesId = 1, GcdMatchSource = GcdMatchSourceKind.Name },
                new Series { Name = "Hulk (2008)", GcdSeriesId = 2, GcdMatchSource = GcdMatchSourceKind.Name });
            context.SaveChanges();
        }

        using (var store = GcdDataStore.TryOpen(installer.ExtractPath)!)
        {
            GcdBondSync.Run(NewContext, store);
        }

        installer.Remove(NewContext);

        Assert.False(installer.IsInstalled);
        using var check = NewContext();
        Assert.All(check.Series, s => Assert.Null(s.GcdSeriesId));
        Assert.Empty(check.MediaRelations);
    }

    [Fact]
    public void SettingsRow_States()
    {
        using var extract = new GcdTestExtract("2026-09-15");
        var (zip, bundled) = extract.Package();
        var activity = new ActivityService(dispatch: a => a(), recordRun: _ => { });

        var notInstalled = new GcdDataSettingsViewModel(NewContext, activity, Installer(new FakeGcdHttp()), bundled);
        Assert.True(notInstalled.CanDownload);
        Assert.False(notInstalled.CanRemove);
        Assert.StartsWith("Download (", notInstalled.DownloadLabel);
        Assert.Equal("Not installed · data from 2026-09-15", notInstalled.StateText);

        Directory.CreateDirectory(DataFolder);
        File.Copy(extract.ExtractPath, Path.Combine(DataFolder, GcdExtractor.ExtractFileName));
        var current = new GcdDataSettingsViewModel(NewContext, activity, Installer(new FakeGcdHttp()), bundled);
        Assert.False(current.ShowDownload);
        Assert.True(current.CanCheck);
        Assert.True(current.CanRemove);
        Assert.Equal("Data from 2026-09-15", current.StateText);

        var newer = new GcdDataSettingsViewModel(NewContext, activity, Installer(new FakeGcdHttp()), bundled with { DumpDate = "2026-10-15" });
        Assert.True(newer.CanUpdate);
        Assert.StartsWith("Update (", newer.DownloadLabel);
        Assert.Equal("Data from 2026-09-15 · 2026-10-15 is available", newer.StateText);
    }

    [Fact]
    public async Task SettingsRow_Download_InstallsThenMatches()
    {
        using var extract = new GcdTestExtract("2026-09-15");
        var (zip, manifest) = extract.Package();
        var http = new FakeGcdHttp().File(manifest.Url, zip);
        var activity = new ActivityService(dispatch: a => a(), recordRun: _ => { });
        string? matchedPath = null;
        var vm = new GcdDataSettingsViewModel(NewContext, activity, Installer(http), manifest,
            runInBackground: work => work(),
            match: (_, _, _, path) =>
            {
                matchedPath = path;
                return Task.FromResult("3 series matched");
            });

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.True(vm.IsInstalled);
        Assert.Equal(Path.Combine(DataFolder, GcdExtractor.ExtractFileName), matchedPath);
        Assert.Contains("3 series matched", vm.StatusText);
        Assert.Contains(ManifestUrl, http.Requests);   // the live manifest was checked first (404 here, so the bundled one was used)
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SettingsRow_BadDownload_SaysWhy()
    {
        using var extract = new GcdTestExtract();
        var (zip, manifest) = extract.Package(corruptHash: true);
        var activity = new ActivityService(dispatch: a => a(), recordRun: _ => { });
        var vm = new GcdDataSettingsViewModel(NewContext, activity, Installer(new FakeGcdHttp().File(manifest.Url, zip)), manifest,
            runInBackground: work => work(), match: (_, _, _, _) => Task.FromResult(string.Empty));

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.False(vm.IsInstalled);
        Assert.Contains("checksum", vm.StatusText);
    }

    [Fact]
    public async Task Matching_WithoutData_SaysSo()
    {
        string summary = await GcdMatching.RunAsync(NewContext, null, CancellationToken.None, Path.Combine(_root, "missing.sqlite"));

        Assert.Equal(GcdMatching.NotInstalled, summary);
    }

    [Fact]
    public void ScheduledTask_IsWeekly_AndOnByDefault()
    {
        var task = ScheduledTaskCatalog.Find(ScheduledTaskCatalog.GcdMatch);

        Assert.NotNull(task);
        Assert.True(task!.DefaultEnabled);
        Assert.Equal(TimeSpan.FromDays(7), task.DefaultInterval);
    }

    [Fact]
    public void BundledManifest_MatchesTheRepoCopy()
    {
        var bundled = GcdDataInstaller.BundledManifest();

        Assert.NotNull(bundled);
        Assert.Equal(GcdExtractor.SchemaVersion, bundled!.SchemaVersion);
        Assert.StartsWith("https://github.com/heisehis/paperbunkr-gcd-data/releases/download/gcd-", bundled.Url);
    }

    /// <summary>Reports inline (no synchronization context), so the test sees every report before InstallAsync returns.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
