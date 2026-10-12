using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Scraper;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Organizing;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The "Import…" button in Preferences → Organize (docs/superpowers/specs/2026-10-04-lop-profile-import-design.md): the profiles it saves, which one is selected, and the
/// notes that stay on screen. View-model level only; the XAML is not rendered here.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class LopImportUiTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_lop_ui_{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly PickerReturning _picker = new();

    public LopImportUiTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "test.db");
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private sealed class PickerReturning : IFilePickerService
    {
        public string? Path { get; set; }
        public string? LastExtension { get; private set; }

        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel)
        {
            LastExtension = extension;
            return Task.FromResult(Path);
        }

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private ProfileManagerViewModel Manager(bool withPicker = true) =>
        new(new OrganizerProfileStore(NewContext), filePicker: withPicker ? _picker : null);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Lop", name);

    [Fact]
    public async Task Import_SavesTheProfile_SelectsIt_AndShowsItsNotes()
    {
        _picker.Path = Fixture("05 - Publishers.lop");
        var manager = Manager();

        await manager.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Equal("lop", _picker.LastExtension);
        Assert.Equal("05 - Publishers", manager.Selected!.Name);
        Assert.Contains(manager.Profiles, p => p.Name == "05 - Publishers");
        Assert.True(manager.HasImportNotes);
        Assert.Contains(manager.ImportNotes, n => n.Contains("Check the base folder"));
        Assert.Equal("Imported 1 profile.", manager.ImportStatusMessage);
    }

    [Fact]
    public async Task Import_ARefusedProfile_IsListedInTheNotes_AndTheStatusSaysSo()
    {
        string path = Path.Combine(_root, "mixed.lop");
        File.WriteAllText(path,
            "<Profiles>" +
            "<Profile Name=\"Bad\"><FileTemplate>{&lt;series&gt;}</FileTemplate><Mode>Copy</Mode>" +
            "<ExcludeRules Operator=\"All\" ExcludeMode=\"Only\"><ExcludeRule Field=\"Start Year\" Operator=\"is\" Value=\"2000\" /></ExcludeRules></Profile>" +
            "<Profile Name=\"Good\"><FileTemplate>{&lt;series&gt;}</FileTemplate><Mode>Copy</Mode><CopyMode>false</CopyMode></Profile>" +
            "</Profiles>");
        _picker.Path = path;
        var manager = Manager();

        await manager.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Equal("Good", manager.Selected!.Name);
        Assert.DoesNotContain(manager.Profiles, p => p.Name == "Bad");
        Assert.Contains(manager.ImportNotes, n => n.StartsWith("Bad was not imported:"));
        Assert.Equal("Imported 1; 1 not imported (see the notes).", manager.ImportStatusMessage);
    }

    [Fact]
    public async Task Import_WhenEveryProfileIsRefused_StillShowsTheNotes()
    {
        string path = Path.Combine(_root, "bad.lop");
        File.WriteAllText(path,
            "<Profile Name=\"Bad\"><FileTemplate>{&lt;series&gt;}</FileTemplate><Mode>Copy</Mode>" +
            "<ExcludeRules Operator=\"All\" ExcludeMode=\"Do not\"><ExcludeRule Field=\"Manga\" Operator=\"is\" Value=\"Yes\" /></ExcludeRules></Profile>");
        _picker.Path = path;
        var manager = Manager();

        await manager.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Null(manager.Selected);
        Assert.True(manager.HasImportNotes);
        Assert.Empty(manager.Profiles);
    }

    [Fact]
    public async Task Import_NotAProfileFile_SaysNothingWasImported()
    {
        string path = Path.Combine(_root, "junk.lop");
        File.WriteAllText(path, "nope");
        _picker.Path = path;
        var manager = Manager();

        await manager.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Equal("Nothing was imported.", manager.ImportStatusMessage);
        Assert.Contains("junk.lop", Assert.Single(manager.ImportNotes));
        Assert.Empty(manager.Profiles);
    }

    [Fact]
    public async Task Import_CancellingThePicker_ChangesNothing()
    {
        _picker.Path = null;
        var manager = Manager();

        await manager.ImportProfilesCommand.ExecuteAsync(null);

        Assert.Null(manager.ImportStatusMessage);
        Assert.False(manager.HasImportNotes);
        Assert.Empty(manager.Profiles);
    }

    [Fact]
    public async Task DismissingTheNotes_ClearsThem_ButKeepsTheImportedProfiles()
    {
        _picker.Path = Fixture("05 - Publishers.lop");
        var manager = Manager();
        await manager.ImportProfilesCommand.ExecuteAsync(null);

        manager.DismissImportNotesCommand.Execute(null);

        Assert.False(manager.HasImportNotes);
        Assert.Single(manager.Profiles);
    }

    [Fact]
    public async Task ASecondImport_ReplacesTheNotesOfTheFirst()
    {
        _picker.Path = Fixture("05 - Publishers.lop");
        var manager = Manager();
        await manager.ImportProfilesCommand.ExecuteAsync(null);
        _picker.Path = Fixture("Events.lop");

        await manager.ImportProfilesCommand.ExecuteAsync(null);

        Assert.DoesNotContain(manager.ImportNotes, n => n.StartsWith("05 - Publishers"));
        Assert.Equal(2, manager.Profiles.Count);
    }

    [Fact]
    public void WithoutAFilePicker_TheImportButtonIsHidden()
    {
        Assert.False(Manager(withPicker: false).CanImport);
        Assert.True(Manager(withPicker: true).CanImport);
    }
}
