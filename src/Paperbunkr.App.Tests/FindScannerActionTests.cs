using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.ViewModels.LibraryActions;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.ScanCredits;

namespace Paperbunkr.App.Tests;

/// <summary>
/// The Library's "Find scanner from file name" action and the scanner lists in Preferences (docs/superpowers/specs/2026-10-11-scan-credits-design.md). The database path override
/// also moves <c>scan-credits.json</c>, so the user's lists are read from the test folder.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class FindScannerActionTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_findscanner_{Guid.NewGuid():N}");
    private readonly List<(string Title, string Message)> _toasts = new();
    private readonly List<int> _writeBacks = new();

    public FindScannerActionTests()
    {
        Directory.CreateDirectory(_dir);
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        PaperbunkrDbContext.DatabasePathOverride = Path.Combine(_dir, "test.db");
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
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

    private LibraryScreenViewModel NewVm()
    {
        var vm = new LibraryScreenViewModel(
            goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            showToast: (t, m) => _toasts.Add((t, m)),
            enqueueMetadataWriteBack: (id, _) => _writeBacks.Add(id));
        vm.History = new MetadataEditHistoryService();
        return vm;
    }

    private static int Seed(string fileName, string? scanInformation = null, string publisher = "Image")
    {
        using var context = PaperbunkrDb.CreateContext();
        var issue = new Issue { Series = new Series { Name = "Saga" }, Number = "54", Publisher = publisher, PageCount = 20, FilePath = $@"C:\lib\{fileName}", ScanInformation = scanInformation };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static string? ScanInfo(int id)
    {
        using var context = PaperbunkrDb.CreateContext();
        return context.Issues.Single(i => i.Id == id).ScanInformation;
    }

    [Fact]
    public void TheScannerInTheFileName_IsAddedToScanInformation()
    {
        int id = Seed("Saga 054 (2018) (Digital) (Zone-Empire).cbz");
        var vm = NewVm();

        vm.FindScannerFromFileNameCommand.Execute(LibraryTarget.Issues(new[] { id }));
        TestDispatcher.Drain();

        Assert.Equal("Zone-Empire", ScanInfo(id));
        Assert.Contains(_toasts, t => t.Title == "Scanner found" && t.Message.Contains("1 book"));
    }

    [Fact]
    public void ItIsAddedBesideWhatIsAlreadyThere_AndABookThatAlreadyListsItIsNotTouched()
    {
        int has = Seed("Saga 054 (2018) (Digital) (Zone-Empire).cbz", "c2c");
        int already = Seed("Saga 055 (2018) (Digital) (Zone-Empire).cbz", "zone-empire");
        var vm = NewVm();

        vm.FindScannerFromFileNameCommand.Execute(LibraryTarget.Issues(new[] { has, already }));
        TestDispatcher.Drain();

        Assert.Equal("c2c, Zone-Empire", ScanInfo(has));
        Assert.Equal("zone-empire", ScanInfo(already));
        Assert.Contains(_toasts, t => t.Message.Contains("1 book"));
    }

    [Fact]
    public void AFileNameWithNoScanner_SaysSo_AndRecordsNoUndoStep()
    {
        int id = Seed("Saga 054 (2018) (Digital).cbz");
        var vm = NewVm();

        vm.FindScannerFromFileNameCommand.Execute(LibraryTarget.Issues(new[] { id }));
        TestDispatcher.Drain();

        Assert.Null(ScanInfo(id));
        Assert.Contains(_toasts, t => t.Title == "No scanner found");
        Assert.False(vm.History.CanUndo);
    }

    [Fact]
    public void ABracketThatIsTheBooksOwnPublisher_IsNotAScanner()
    {
        int id = Seed("Saga 054 (2018) (Image).cbz", publisher: "Image");
        var vm = NewVm();

        vm.FindScannerFromFileNameCommand.Execute(LibraryTarget.Issues(new[] { id }));
        TestDispatcher.Drain();

        Assert.Null(ScanInfo(id));
    }

    [Fact]
    public void OneUndo_TakesTheScannerBackOut()
    {
        int id = Seed("Saga 054 (2018) (Digital) (Zone-Empire).cbz");
        var vm = NewVm();
        vm.FindScannerFromFileNameCommand.Execute(LibraryTarget.Issues(new[] { id }));
        TestDispatcher.Drain();

        Assert.NotNull(vm.History.Undo(PaperbunkrDb.CreateContext));

        Assert.Null(ScanInfo(id));
    }

    [Fact]
    public void TheUsersOwnIgnoredWords_AreRespected()
    {
        ScanCreditSettings.Save(new ScanCreditSettings([], ["remastered"]));
        int id = Seed("Saga 054 (2018) (Remastered).cbz");
        var vm = NewVm();

        vm.FindScannerFromFileNameCommand.Execute(LibraryTarget.Issues(new[] { id }));
        TestDispatcher.Drain();

        Assert.Null(ScanInfo(id));
    }

    [Fact]
    public void TheMenu_OffersTheAction()
    {
        Seed("Saga 054 (2018) (Digital) (Zone-Empire).cbz");
        var vm = NewVm();

        var entries = ((IContextMenuProvider)vm).BuildContextMenu(vm.IssueList.Rows.Single()) ?? Array.Empty<ContextMenuEntry>();

        Assert.Contains(entries.Single(e => e.Header == "Tidy up").Children!, e => e.Header == "Find scanner from file name");
    }

    // --- Preferences ---

    [Fact]
    public void Preferences_SaveWritesTheLists_AndLoadReadsThemBack()
    {
        var settings = new OrganizeScrapeSettingsViewModel(PaperbunkrDb.CreateContext, () => { });
        settings.Load();
        settings.ScannerNames = "My Crew" + Environment.NewLine + "Other Crew";
        settings.ScanIgnoredWords = "remastered";

        settings.SaveCommand.Execute(null);

        var saved = ScanCreditSettings.Load();
        Assert.Equal(["My Crew", "Other Crew"], saved.Scanners);
        Assert.Equal(["remastered"], saved.Ignored);

        var again = new OrganizeScrapeSettingsViewModel(PaperbunkrDb.CreateContext, () => { });
        again.Load();
        Assert.Contains("My Crew", again.ScannerNames);
        Assert.Equal("remastered", again.ScanIgnoredWords);
    }
}
