using ClusterLibraryManager.ComicVine;
using ClusterLibraryManager.Organizing;
using ClusterLibraryManager.Persistence;
using ClusterLibraryManager.Settings;

namespace ClusterLibraryManager.Tests;

/// <summary>Implementation plan Phase 3 Step 3.6 verification: settings load/save round-trip,
/// test-connection wiring, and profile CRUD + insert-token behavior.</summary>
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _testRoot;
    private readonly PluginDatabase _database;

    public SettingsViewModelTests()
    {
        _testRoot = Directory.CreateTempSubdirectory("clm-settings-test-").FullName;
        _database = new PluginDatabase(Path.Combine(_testRoot, "plugin.db"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void SettingsViewModel_loads_persisted_settings_on_construction()
    {
        var store = new PluginSettingsStore(_database);
        store.Save(new PluginSettings { ApiKey = "abc123", AutoChooseTopMatch = true });

        var vm = new SettingsViewModel(store, key => new ComicVineService(key));

        Assert.Equal("abc123", vm.ApiKey);
        Assert.True(vm.AutoChooseTopMatch);
        Assert.All(vm.ScrapeFieldToggles, t => Assert.True(t.IsEnabled)); // all default enabled, per CE
    }

    [Fact]
    public void SettingsViewModel_save_persists_the_current_values_including_toggled_off_fields()
    {
        var store = new PluginSettingsStore(_database);
        var vm = new SettingsViewModel(store, key => new ComicVineService(key));
        vm.ApiKey = "new-key";
        vm.ScrapeFieldToggles.First(t => t.Field == ScrapeField.Webpage).IsEnabled = false;

        vm.SaveCommand.Execute(null);

        PluginSettings reloaded = store.Load();
        Assert.Equal("new-key", reloaded.ApiKey);
        Assert.DoesNotContain(ScrapeField.Webpage, reloaded.EnabledScrapeFields);
        Assert.Contains(ScrapeField.Series, reloaded.EnabledScrapeFields);
    }

    [Fact]
    public async Task SettingsViewModel_test_connection_reports_failure_without_calling_out_when_key_is_blank()
    {
        var store = new PluginSettingsStore(_database);
        bool factoryCalled = false;
        var vm = new SettingsViewModel(store, key => { factoryCalled = true; return new ComicVineService(key); });

        await vm.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(factoryCalled);
        Assert.Contains("API key", vm.TestConnectionStatus);
    }

    [Fact]
    public void ProfileManagerViewModel_starts_with_no_profiles_and_can_add_one()
    {
        var store = new OrganizerProfileStore(_database);
        var vm = new ProfileManagerViewModel(store);
        Assert.Empty(vm.Profiles);

        vm.AddProfileCommand.Execute(null);

        var profile = Assert.Single(vm.Profiles);
        Assert.Equal("New Profile", profile.Name);
        Assert.Same(profile, vm.Selected);
    }

    [Fact]
    public void ProfileManagerViewModel_save_selected_persists_edits()
    {
        var store = new OrganizerProfileStore(_database);
        var vm = new ProfileManagerViewModel(store);
        vm.AddProfileCommand.Execute(null);
        vm.Selected!.Name = "Manga Library";
        vm.Selected.FolderTemplate = "{<publisher>}/{<series>}";

        vm.SaveSelectedCommand.Execute(null);

        var reloaded = new ProfileManagerViewModel(store);
        var profile = Assert.Single(reloaded.Profiles);
        Assert.Equal("Manga Library", profile.Name);
        Assert.Equal("{<publisher>}/{<series>}", profile.FolderTemplate);
    }

    [Fact]
    public void ProfileManagerViewModel_delete_selected_removes_the_profile()
    {
        var store = new OrganizerProfileStore(_database);
        var vm = new ProfileManagerViewModel(store);
        vm.AddProfileCommand.Execute(null);

        vm.DeleteSelectedCommand.Execute(null);

        Assert.Empty(vm.Profiles);
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void ProfileManagerViewModel_insert_token_appends_to_the_selected_profiles_folder_template()
    {
        var store = new OrganizerProfileStore(_database);
        var vm = new ProfileManagerViewModel(store);
        vm.AddProfileCommand.Execute(null);
        vm.Selected!.FolderTemplate = "{<publisher>}";

        InsertableToken seriesToken = vm.InsertableTokens.Single(t => t.Token == "{<series>}");
        seriesToken.InsertIntoFolder.Execute(seriesToken.Token);

        Assert.Equal("{<publisher>}{<series>}", vm.Selected.FolderTemplate);
    }

    [Fact]
    public void ProfileManagerViewModel_insert_token_appends_to_the_selected_profiles_file_template()
    {
        var store = new OrganizerProfileStore(_database);
        var vm = new ProfileManagerViewModel(store);
        vm.AddProfileCommand.Execute(null);
        vm.Selected!.FileTemplate = "";

        InsertableToken numberToken = vm.InsertableTokens.Single(t => t.Token == "{ #<number2>}");
        numberToken.InsertIntoFile.Execute(numberToken.Token);

        Assert.Equal("{ #<number2>}", vm.Selected.FileTemplate);
    }
}
