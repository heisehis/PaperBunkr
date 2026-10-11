using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Where a ComicRack <c>.cbl</c> lands depending on what it holds (docs/superpowers/specs/2026-10-04-smart-list-cbl-import-design.md): the Smart screen's Import
/// button and the New Reading List dialog. Redirects <see cref="PaperbunkrDbContext.DatabasePathOverride"/> to a temp SQLite file, like
/// <see cref="SmartScreenViewModelTests"/>.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CeImportRoutingTests : IDisposable
{
    private const string Header = "<?xml version=\"1.0\"?><ReadingList xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">";

    private readonly string? _originalDbPathOverride;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"pb_ce_import_{Guid.NewGuid():N}");
    private readonly PickerReturning _picker = new();

    public CeImportRoutingTests()
    {
        Directory.CreateDirectory(_dir);
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = Path.Combine(_dir, "test.db");
        using var context = PaperbunkrDb.CreateContext();
        context.Database.Migrate();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class PickerReturning : IFilePickerService
    {
        public string? Path { get; set; }

        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult(Path);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    private string WriteSmartList(string name, params string[] matchers)
    {
        string path = Path.Combine(_dir, name + ".cbl");
        File.WriteAllText(path, $"{Header}<Name>{name}</Name><Books /><Matchers>{string.Concat(matchers)}</Matchers></ReadingList>");
        return path;
    }

    private string WriteReadingList(string name)
    {
        string path = Path.Combine(_dir, name + ".cbl");
        File.WriteAllText(path, $"{Header}<Name>{name}</Name><Books><Book Series=\"Alpha\" Number=\"1\" Volume=\"2000\" Year=\"2000\"><Id>{Guid.NewGuid()}</Id></Book></Books><Matchers /></ReadingList>");
        return path;
    }

    private const string FormatMatcher = "<ComicBookMatcher xsi:type=\"ComicBookFormatMatcher\"><MatchValue>Annual</MatchValue></ComicBookMatcher>";
    private const string UnknownScript = "<ComicBookMatcher xsi:type=\"ComicBookPluginMatcher\" MatchOperator=\"1\" PluginKey=\"someScript\"><MatchValue /></ComicBookMatcher>";

    private static int UserSmartListCount()
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.SmartLists.Count(s => !s.IsSystem);
    }

    // --- The Smart screen's Import button ---

    [Fact]
    public async Task SmartScreen_Import_AddsTheListAndOpensIt()
    {
        var activity = new ActivityService(a => a());
        _picker.Path = WriteSmartList("Annuals", FormatMatcher);
        var vm = new SmartScreenViewModel(_ => { }, _ => { }, filePicker: _picker, activity: activity);

        await vm.ImportComicRackListCommand.ExecuteAsync(null);

        Assert.Equal(1, UserSmartListCount());
        Assert.Contains(vm.CustomLists, l => l.Name == "Annuals");
        Assert.Empty(activity.Alerts);
    }

    [Fact]
    public async Task SmartScreen_Import_ADroppedConditionRaisesOneAlert_AndStillImportsTheRest()
    {
        var activity = new ActivityService(a => a());
        _picker.Path = WriteSmartList("Half", FormatMatcher, UnknownScript);
        var vm = new SmartScreenViewModel(_ => { }, _ => { }, filePicker: _picker, activity: activity);

        await vm.ImportComicRackListCommand.ExecuteAsync(null);

        Assert.Equal(1, UserSmartListCount());
        var alert = Assert.Single(activity.Alerts);
        Assert.Contains("someScript", alert.Detail);
        Assert.Contains("match more", alert.Detail);
    }

    [Fact]
    public async Task SmartScreen_Import_AListWithNothingUsable_IsRefusedWithAnAlert()
    {
        var activity = new ActivityService(a => a());
        _picker.Path = WriteSmartList("Scripty", UnknownScript);
        var vm = new SmartScreenViewModel(_ => { }, _ => { }, filePicker: _picker, activity: activity);

        await vm.ImportComicRackListCommand.ExecuteAsync(null);

        Assert.Equal(0, UserSmartListCount());
        Assert.Contains("was not imported", Assert.Single(activity.Alerts).Detail);
    }

    [Fact]
    public async Task SmartScreen_Import_AReadingListFile_IsNotImportedFromHere()
    {
        var activity = new ActivityService(a => a());
        _picker.Path = WriteReadingList("Order");
        var vm = new SmartScreenViewModel(_ => { }, _ => { }, filePicker: _picker, activity: activity);

        await vm.ImportComicRackListCommand.ExecuteAsync(null);

        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.ReadingLists);
        Assert.Equal(0, UserSmartListCount());
        Assert.Contains("is a reading list", Assert.Single(activity.Alerts).Detail);
    }

    [Fact]
    public async Task SmartScreen_Import_CancellingThePicker_DoesNothing()
    {
        var activity = new ActivityService(a => a());
        _picker.Path = null;
        var vm = new SmartScreenViewModel(_ => { }, _ => { }, filePicker: _picker, activity: activity);

        await vm.ImportComicRackListCommand.ExecuteAsync(null);

        Assert.Equal(0, UserSmartListCount());
        Assert.Empty(activity.Alerts);
    }

    [Fact]
    public async Task SmartScreen_Import_NotAComicRackFile_IsReported()
    {
        var activity = new ActivityService(a => a());
        string path = Path.Combine(_dir, "junk.cbl");
        File.WriteAllText(path, "nope");
        _picker.Path = path;
        var vm = new SmartScreenViewModel(_ => { }, _ => { }, filePicker: _picker, activity: activity);

        await vm.ImportComicRackListCommand.ExecuteAsync(null);

        Assert.Contains("is not a ComicRack list", Assert.Single(activity.Alerts).Detail);
    }

    // --- The New Reading List dialog ---

    private NewReadingListViewModel NewReadingListDialog(IActivityService activity, Action<int> created)
    {
        var vm = new NewReadingListViewModel(_picker, created, () => { }, activity);
        vm.Reset();
        vm.SelectMethodCommand.Execute("Import");
        return vm;
    }

    [Fact]
    public async Task NewReadingList_ASmartListFile_BecomesASmartList_NotAReadingList()
    {
        var activity = new ActivityService(a => a());
        int created = 0;
        _picker.Path = WriteSmartList("Annuals", FormatMatcher);
        var vm = NewReadingListDialog(activity, id => created = id);

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.Equal(0, created);
        Assert.Equal(1, UserSmartListCount());
        using var context = PaperbunkrDb.CreateContext();
        Assert.Empty(context.ReadingLists);
        Assert.Contains("smart list", vm.StatusMessage);
        Assert.Contains("Annuals", vm.StatusMessage);
    }

    [Fact]
    public async Task NewReadingList_AReadingListFile_StillCreatesTheReadingList()
    {
        var activity = new ActivityService(a => a());
        int created = 0;
        _picker.Path = WriteReadingList("Order");
        var vm = NewReadingListDialog(activity, id => created = id);

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.NotEqual(0, created);
        using var context = PaperbunkrDb.CreateContext();
        Assert.Equal("Order", Assert.Single(context.ReadingLists).Name);
        Assert.Equal(0, UserSmartListCount());
    }

    [Fact]
    public async Task NewReadingList_ASmartListWithDroppedConditions_RaisesTheAlert()
    {
        var activity = new ActivityService(a => a());
        _picker.Path = WriteSmartList("Half", FormatMatcher, UnknownScript);
        var vm = NewReadingListDialog(activity, _ => { });

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.Contains("someScript", Assert.Single(activity.Alerts).Detail);
    }
}
