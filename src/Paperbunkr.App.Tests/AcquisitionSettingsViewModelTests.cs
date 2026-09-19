using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views.Preferences;
using Paperbunkr.Daemon.Indexers;
using Paperbunkr.Data;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

public class AcquisitionSettingsViewModelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_acqprefs_{Guid.NewGuid():N}.db");
    private bool _connectionsOpened;
    private (string Url, string Key)? _testedWith;
    private ConnectionTestResult _testResult = ConnectionTestResult.Ok("Connected to Prowlarr 1.30.");

    public AcquisitionSettingsViewModelTests()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private AcquisitionSettingsViewModel Create() => new(NewContext, () => _connectionsOpened = true, (url, key) =>
    {
        _testedWith = (url, key);
        return new StubIndexer(() => _testResult);
    });

    private sealed class StubIndexer(Func<ConnectionTestResult> result) : IIndexerClient
    {
        public Task<IReadOnlyList<IndexerRelease>> SearchAsync(string queryText, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IndexerRelease>>(Array.Empty<IndexerRelease>());

        public Task<ConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(result());
    }

    [Fact]
    public void Defaults_AreSafe_NothingRunsUntilTheUserOptsIn()
    {
        var vm = Create();

        Assert.False(vm.Enabled);
        Assert.Equal(60, vm.PollIntervalMinutes);
        Assert.Equal(500, vm.MaxSizeMb);
        Assert.True(vm.PreferCbz);
        Assert.False(vm.HasSavedApiKey);
        Assert.Equal("Prowlarr API key", vm.ApiKeyWatermark);
    }

    [Fact]
    public void Save_PersistsSettings_AndStoresTheKeyEncrypted_NeverEchoingItBack()
    {
        var vm = Create();
        vm.Enabled = true;
        vm.ProwlarrUrl = "  http://prowlarr.local:9696  ";
        vm.ProwlarrApiKey = "  SECRET  ";
        vm.MinSizeMb = 5;
        vm.PreferredReleaseGroups = "Zone-Empire, Minutemen";

        vm.SaveCommand.Execute(null);

        using var context = NewContext();
        var saved = context.GetOrCreateAcquisitionSettings();
        Assert.True(saved.Enabled);
        Assert.Equal("http://prowlarr.local:9696", saved.ProwlarrUrl);   // trimmed
        Assert.Equal(5, saved.MinSizeMb);
        Assert.Equal("Zone-Empire, Minutemen", saved.PreferredReleaseGroups);

        var raw = context.ProviderCredentials.Single(c => c.Provider == "Prowlarr").Value;
        Assert.True(CredentialStore.IsEncrypted(raw));
        Assert.DoesNotContain("SECRET", raw);
        Assert.Equal("SECRET", CredentialStore.Get(context, "Prowlarr", CredentialKind.ApiKey));

        Assert.Equal(string.Empty, vm.ProwlarrApiKey);                    // cleared after saving: write-only
        Assert.True(vm.HasSavedApiKey);
        Assert.Equal("Saved — leave blank to keep it", vm.ApiKeyWatermark);
        Assert.Equal("Saved.", vm.StatusMessage);
        Assert.True(vm.HasInfoStatus);
        Assert.False(vm.HasErrorStatus);
    }

    [Fact]
    public void ABlankKeyField_KeepsTheStoredKey()
    {
        var vm = Create();
        vm.ProwlarrApiKey = "FIRST";
        vm.SaveCommand.Execute(null);

        vm.ProwlarrUrl = "http://changed:9696";
        vm.SaveCommand.Execute(null);

        using var context = NewContext();
        Assert.Equal("FIRST", CredentialStore.Get(context, "Prowlarr", CredentialKind.ApiKey));
    }

    [Fact]
    public void Save_RaisesThePollIntervalToTheFifteenMinuteFloor()
    {
        var vm = Create();
        vm.PollIntervalMinutes = 1;

        vm.SaveCommand.Execute(null);

        Assert.Equal(15, vm.PollIntervalMinutes);
        using var context = NewContext();
        Assert.Equal(15, context.GetOrCreateAcquisitionSettings().PollIntervalMinutes);
    }

    [Fact]
    public void Save_RefusesAMinimumLargerThanTheMaximum_AndWritesNothing()
    {
        var vm = Create();
        vm.MinSizeMb = 800;
        vm.MaxSizeMb = 100;
        vm.Enabled = true;

        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasErrorStatus);
        using var context = NewContext();
        Assert.False(context.GetOrCreateAcquisitionSettings().Enabled);
    }

    [Fact]
    public void SavingWhileEnabledWithoutAKey_SaysWhatIsStillNeeded()
    {
        var vm = Create();
        vm.Enabled = true;

        vm.SaveCommand.Execute(null);

        Assert.Contains("Prowlarr address and API key", vm.StatusMessage);
    }

    [Fact]
    public async Task Test_UsesTheTypedKey_ThenTheStoredOne_WithoutSavingAnything()
    {
        var vm = Create();
        vm.ProwlarrUrl = "http://p:9696";
        vm.ProwlarrApiKey = "TYPED";
        await vm.TestProwlarrCommand.ExecuteAsync(null);

        Assert.Equal(("http://p:9696", "TYPED"), _testedWith);
        Assert.Equal("Connected to Prowlarr 1.30.", vm.StatusMessage);
        Assert.True(vm.HasInfoStatus);
        using (var context = NewContext())
        {
            Assert.False(context.ProviderCredentials.Any(c => c.Provider == "Prowlarr"));   // testing never saves
        }

        vm.ProwlarrApiKey = "STORED";
        vm.SaveCommand.Execute(null);
        vm.ProwlarrApiKey = string.Empty;
        await vm.TestProwlarrCommand.ExecuteAsync(null);
        Assert.Equal(("http://p:9696", "STORED"), _testedWith);
    }

    [Fact]
    public async Task Test_ReportsFailures_AndMissingInput()
    {
        var vm = Create();
        await vm.TestProwlarrCommand.ExecuteAsync(null);
        Assert.True(vm.HasErrorStatus);
        Assert.Null(_testedWith);                                                     // nothing to test yet

        vm.ProwlarrUrl = "http://p:9696";
        vm.ProwlarrApiKey = "K";
        _testResult = ConnectionTestResult.Fail("Prowlarr rejected the API key.");
        await vm.TestProwlarrCommand.ExecuteAsync(null);
        Assert.True(vm.HasErrorStatus);
        Assert.Equal("Prowlarr rejected the API key.", vm.StatusMessage);
    }

    [Fact]
    public void Load_ReflectsAComicVineKeyAddedElsewhere_AndOpenConnectionsNavigates()
    {
        var vm = Create();
        Assert.False(vm.HasComicVineKey);

        using (var context = NewContext())
        {
            CredentialStore.Set(context, "ComicVine", CredentialKind.ApiKey, "CV");
        }

        vm.Load();

        Assert.True(vm.HasComicVineKey);
        vm.OpenConnectionsCommand.Execute(null);
        Assert.True(_connectionsOpened);
    }

    /// <summary>
    /// Proves the section's compiled XAML was actually woven into the assembly (CLAUDE.md, "adding a new Avalonia View": a build can
    /// report 0 errors while shipping an un-woven assembly, which only fails when the view is first constructed).
    /// </summary>
    [Fact]
    public void TheSectionView_Constructs_AndBindsToTheViewModel()
    {
        TestAppBuilder.EnsureInitialized();
        var vm = Create();

        var view = new AcquisitionSection { DataContext = vm };

        Assert.IsAssignableFrom<UserControl>(view);
        Assert.NotNull(view.Content);
    }
}
