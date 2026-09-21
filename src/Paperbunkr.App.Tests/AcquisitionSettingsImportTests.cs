using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Daemon.Clients;
using Paperbunkr.Daemon.Indexers;
using Paperbunkr.Data;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>Preferences → Acquisition, the qBittorrent / import / automatic-download settings (slices 2-4).</summary>
public class AcquisitionSettingsImportTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_acqprefs2_{Guid.NewGuid():N}.db");
    private (string Url, string User, string Password, string Category)? _qbitTestedWith;
    private ConnectionTestResult _qbitResult = ConnectionTestResult.Ok("Connected to qBittorrent v5.");

    public AcquisitionSettingsImportTests()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private AcquisitionSettingsViewModel Create() => new(NewContext, () => { }, (_, _) => new StubIndexer(),
        (url, user, password, category) =>
        {
            _qbitTestedWith = (url, user, password, category);
            return new StubDownloadClient(() => _qbitResult);
        });

    private sealed class StubIndexer : IIndexerClient
    {
        public Task<IReadOnlyList<IndexerRelease>> SearchAsync(string queryText, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<IndexerRelease>>(Array.Empty<IndexerRelease>());
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(ConnectionTestResult.Ok("ok"));
    }

    private sealed class StubDownloadClient(Func<ConnectionTestResult> result) : IDownloadClient
    {
        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(result());
        public Task<string> AddAsync(string downloadUrl, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadStatus>> GetStatusAsync(IReadOnlyCollection<string>? hashes, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<DownloadFile>> GetFilesAsync(string hash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(string hash, bool deleteFiles, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public void QBittorrentSettings_Save_WithThePasswordEncrypted_AndTheCategoryDefaulted()
    {
        var vm = Create();
        vm.QBittorrentUrl = " http://qbit.local:8080 ";
        vm.QBittorrentUsername = " admin ";
        vm.QBittorrentPassword = "hunter2";
        vm.QBittorrentCategory = "   ";               // blank is never allowed: Paperbunkr only touches torrents in its own category

        vm.SaveCommand.Execute(null);

        using var context = NewContext();
        var saved = context.GetOrCreateAcquisitionSettings();
        Assert.Equal("http://qbit.local:8080", saved.QBittorrentUrl);
        Assert.Equal("paperbunkr-comics", saved.QBittorrentCategory);
        Assert.Equal("paperbunkr-comics", vm.QBittorrentCategory);
        Assert.Equal("admin", CredentialStore.Get(context, "qBittorrent", CredentialKind.Username));
        var raw = context.ProviderCredentials.Single(c => c.Provider == "qBittorrent" && c.Kind == CredentialKind.Password).Value;
        Assert.True(CredentialStore.IsEncrypted(raw));
        Assert.DoesNotContain("hunter2", raw);
        Assert.Equal(string.Empty, vm.QBittorrentPassword);                 // write-only
        Assert.True(vm.HasSavedQBittorrentPassword);
        Assert.Equal("Saved — leave blank to keep it", vm.QBittorrentPasswordWatermark);
    }

    [Fact]
    public void ABlankQBittorrentPassword_KeepsTheStoredOne_AndLoadShowsTheUsername()
    {
        var vm = Create();
        vm.QBittorrentUsername = "admin";
        vm.QBittorrentPassword = "first";
        vm.SaveCommand.Execute(null);
        vm.QBittorrentUrl = "http://changed:8080";
        vm.SaveCommand.Execute(null);

        var reloaded = Create();
        Assert.Equal("admin", reloaded.QBittorrentUsername);
        Assert.Equal("http://changed:8080", reloaded.QBittorrentUrl);
        Assert.True(reloaded.HasSavedQBittorrentPassword);
        using var context = NewContext();
        Assert.Equal("first", CredentialStore.Get(context, "qBittorrent", CredentialKind.Password));
    }

    [Fact]
    public async Task TestQBittorrent_UsesTheTypedPassword_ThenTheStoredOne_AndNeverSaves()
    {
        var vm = Create();
        vm.QBittorrentUrl = "http://q:8080";
        vm.QBittorrentUsername = "admin";
        vm.QBittorrentPassword = "typed";
        await vm.TestQBittorrentCommand.ExecuteAsync(null);

        Assert.Equal(("http://q:8080", "admin", "typed", "paperbunkr-comics"), _qbitTestedWith);
        Assert.True(vm.HasInfoStatus);
        using (var context = NewContext())
        {
            Assert.DoesNotContain(context.ProviderCredentials, c => c.Provider == "qBittorrent");   // testing never saves
        }

        vm.SaveCommand.Execute(null);
        vm.QBittorrentPassword = string.Empty;
        await vm.TestQBittorrentCommand.ExecuteAsync(null);
        Assert.Equal("typed", _qbitTestedWith!.Value.Password);

        _qbitResult = ConnectionTestResult.Fail("qBittorrent rejected the username or password.");
        await vm.TestQBittorrentCommand.ExecuteAsync(null);
        Assert.True(vm.HasErrorStatus);

        vm.QBittorrentUrl = "";
        await vm.TestQBittorrentCommand.ExecuteAsync(null);
        Assert.Contains("address", vm.StatusMessage);
    }

    [Fact]
    public void TheTemplate_IsValidatedBeforeSaving_AndPreviewedLive()
    {
        var vm = Create();
        Assert.True(vm.RenameTemplateIsValid);
        Assert.Equal("e.g. Image/Spawn (1992)/Spawn #263.cbz", vm.RenameTemplatePreview);

        vm.RenameTemplate = "{<series>} #{<number2>}";
        Assert.Equal("e.g. Spawn #263.cbz", vm.RenameTemplatePreview);

        vm.RenameTemplate = "{<colour>}";
        Assert.False(vm.RenameTemplateIsValid);
        Assert.Contains("not supported", vm.RenameTemplatePreview);
        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasErrorStatus);
        using var context = NewContext();
        Assert.Equal(AcquisitionSettings.DefaultRenameTemplate, context.GetOrCreateAcquisitionSettings().RenameTemplate);   // nothing was written
    }

    [Fact]
    public void TheDestination_MustBeAFolderThatExists_AndLibraryFoldersAreOffered()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"paperbunkr_dest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            using (var context = NewContext())
            {
                context.WatchedFolders.Add(new WatchedFolder { Path = folder });
                context.SaveChanges();
            }

            var vm = Create();
            Assert.Equal(new[] { folder }, vm.DestinationChoices);

            vm.DestinationFolderPath = Path.Combine(folder, "nope");
            vm.SaveCommand.Execute(null);
            Assert.True(vm.HasErrorStatus);

            vm.DestinationFolderPath = folder;
            vm.SaveCommand.Execute(null);
            Assert.True(vm.HasInfoStatus);
            using var check = NewContext();
            Assert.Equal(folder, check.GetOrCreateAcquisitionSettings().DestinationFolderPath);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void ImportAndAutoGrabOptions_Persist_WithTheScoreClamped()
    {
        var vm = Create();
        vm.WriteComicInfo = false;
        vm.MoveOriginalOnImport = true;
        vm.AutoGrab = true;
        vm.AutoGrabMinScore = 9999;

        vm.SaveCommand.Execute(null);

        using var context = NewContext();
        var saved = context.GetOrCreateAcquisitionSettings();
        Assert.False(saved.WriteComicInfo);
        Assert.True(saved.MoveOriginalOnImport);
        Assert.True(saved.AutoGrab);
        Assert.Equal(500, saved.AutoGrabMinScore);
        Assert.Equal(500, vm.AutoGrabMinScore);

        var reloaded = Create();
        Assert.True(reloaded.AutoGrab);
        Assert.False(reloaded.WriteComicInfo);
    }

    [Fact]
    public void ScrapeOnImport_IsOnByDefault_AndPersists()
    {
        var vm = Create();
        Assert.True(vm.ScrapeOnImport);

        vm.ScrapeOnImport = false;
        vm.SaveCommand.Execute(null);

        using var context = NewContext();
        Assert.False(context.GetOrCreateAcquisitionSettings().ScrapeOnImport);
        Assert.False(Create().ScrapeOnImport);
    }

    [Fact]
    public void Defaults_KeepAutomaticDownloadsOff_AndSeedingOn()
    {
        var vm = Create();

        Assert.False(vm.AutoGrab);
        Assert.False(vm.MoveOriginalOnImport);
        Assert.True(vm.WriteComicInfo);
        Assert.Equal("paperbunkr-comics", vm.QBittorrentCategory);
    }

    [Fact]
    public void AFailedTemplateUpgrade_ShowsWhatTheUserHad_UntilTheySaveANewTemplate()
    {
        using (var context = NewContext())
        {
            var settings = context.GetOrCreateAcquisitionSettings();
            settings.RenameTemplateOriginal = "{series}[ {volumeyear} {title}]";
            settings.RenameTemplateUpgradeFailed = true;
            context.SaveChanges();
        }

        var vm = Create();
        Assert.True(vm.HasTemplateUpgradeNotice);
        Assert.Contains("{series}[ {volumeyear} {title}]", vm.TemplateUpgradeNotice);

        vm.RenameTemplate = "{<series>} #{<number3>}";
        vm.SaveCommand.Execute(null);

        Assert.False(vm.HasTemplateUpgradeNotice);           // saving is the explicit act that retires the original
        using var check = NewContext();
        var saved = check.GetOrCreateAcquisitionSettings();
        Assert.Null(saved.RenameTemplateOriginal);
        Assert.False(saved.RenameTemplateUpgradeFailed);
        Assert.Equal("{<series>} #{<number3>}", saved.RenameTemplate);
    }

    [Fact]
    public void ASuccessfulUpgrade_ShowsNoNotice_EvenThoughTheOriginalIsKept()
    {
        using (var context = NewContext())
        {
            var settings = context.GetOrCreateAcquisitionSettings();
            settings.RenameTemplateOriginal = "{series} #{number:00}";
            context.SaveChanges();
        }

        Assert.False(Create().HasTemplateUpgradeNotice);
    }

    [Fact]
    public void SaveConnections_SavesOnlyTheConnectionFields_SoAnUnrelatedBadValueCannotBlockIt()
    {
        var vm = Create();
        vm.ProwlarrUrl = " http://prowlarr:9696 ";
        vm.ProwlarrApiKey = "KEY";
        vm.QBittorrentUrl = "http://qbit:8080";
        vm.QBittorrentUsername = "admin";
        vm.QBittorrentPassword = "hunter2";
        vm.RenameTemplate = "{<colour>}";                        // invalid, and a full Save would refuse it
        vm.MinSizeMb = 900;
        vm.MaxSizeMb = 10;                                       // also invalid

        vm.SaveConnectionsCommand.Execute(null);

        Assert.True(vm.HasInfoStatus);
        using var context = NewContext();
        var saved = context.GetOrCreateAcquisitionSettings();
        Assert.Equal("http://prowlarr:9696", saved.ProwlarrUrl);
        Assert.Equal("http://qbit:8080", saved.QBittorrentUrl);
        Assert.Equal(AcquisitionSettings.DefaultRenameTemplate, saved.RenameTemplate);     // untouched
        Assert.Equal(0, saved.MinSizeMb);
        Assert.Equal("KEY", CredentialStore.Get(context, "Prowlarr", CredentialKind.ApiKey));
        Assert.Equal("admin", CredentialStore.Get(context, "qBittorrent", CredentialKind.Username));
        Assert.True(CredentialStore.IsEncrypted(context.ProviderCredentials.Single(c => c.Provider == "qBittorrent" && c.Kind == CredentialKind.Password).Value));
        Assert.True(vm.IsProwlarrConnected);
        Assert.True(vm.IsQBittorrentConnected);
    }
}
