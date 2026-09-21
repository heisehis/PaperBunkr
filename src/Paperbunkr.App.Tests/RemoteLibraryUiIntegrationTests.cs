using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Sharing;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Sharing.Client;
using Paperbunkr.Sharing.Server;

namespace Paperbunkr.App.Tests;

/// <summary>
/// How remote books look and behave in the app (docs/superpowers/specs/2026-09-19-remote-library-sharing-
/// design.md §7.2/§7.3): the Library models mark and open them, and the real <see cref="ReaderScreenViewModel"/>
/// reads a page from the host and stores progress on the client's own row.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class RemoteLibraryUiIntegrationTests : IAsyncLifetime
{
    private const string Password = "hunter2-hunter2";

    private readonly string? _originalDbPathOverride;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_remoteui_{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly SlowPages _pages = new() { DelayMs = 5 };
    private ShareServer _server = null!;
    private ShareClient _client = null!;
    private RemotePageFetcher _fetcher = null!;
    private int _remoteIssueId, _remoteSeriesId, _localIssueId, _unknownCountIssueId;

    public RemoteLibraryUiIntegrationTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "ui.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
    }

    public async Task InitializeAsync()
    {
        var certs = new CertificateManager(Path.Combine(_root, "cert"));
        _server = new ShareServer(new ShareServerOptions { Port = 0, PasswordHash = PasswordHasher.Hash(Password, 1_000) }, new AllSharedCatalog(), _pages, certs.GetOrCreate());
        await _server.StartAsync();
        _client = new ShareClient("127.0.0.1", _server.Port, certs.Fingerprint, Password);
        _fetcher = new RemotePageFetcher(_ => _client, new PeerPageCache(Path.Combine(_root, "pages")));

        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(options) { IncludeRemote = true };
        context.Database.EnsureCreated();

        var source = new RemoteSource { InstanceId = "host", DisplayName = "Den PC", Host = "127.0.0.1", Port = _server.Port, CertFingerprint = certs.Fingerprint };
        context.RemoteSources.Add(source);
        context.SaveChanges();

        var remote = new Series { Name = "Saga", RemoteSourceId = source.Id, RemoteSeriesId = 1, ContentType = ContentType.Comic };
        var local = new Series { Name = "Local Series" };
        context.Series.AddRange(remote, local);
        context.SaveChanges();
        _remoteSeriesId = remote.Id;

        var remoteIssue = new Issue { SeriesId = remote.Id, Number = "1", Title = "One", RemoteSourceId = source.Id, RemoteIssueId = 100, PageCount = 6 };
        var unknown = new Issue { SeriesId = remote.Id, Number = "2", RemoteSourceId = source.Id, RemoteIssueId = 101 };   // no page count yet
        var localIssue = new Issue { SeriesId = local.Id, Number = "1", FilePath = CbzFixture.Create(Path.Combine(_root, "local.cbz"), 2) };
        context.Issues.AddRange(remoteIssue, unknown, localIssue);
        context.SaveChanges();
        remote.CoverIssueId = remoteIssue.Id;
        context.SaveChanges();
        (_remoteIssueId, _unknownCountIssueId, _localIssueId) = (remoteIssue.Id, unknown.Id, localIssue.Id);
    }

    public async Task DisposeAsync()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        _fetcher.Dispose();
        _client.Dispose();
        await _server.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PaperbunkrDbContext Ctx(bool includeRemote = true) => new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options) { IncludeRemote = includeRemote };

    // ---------------------------------------------------------------- Library models

    [Fact]
    public void ARemoteSeriesCard_IsMarkedRemote_NamesItsLibrary_AndCountsAsReadable()
    {
        using var context = Ctx();
        var series = context.Series.Include(s => s.Issues).Include(s => s.RemoteSource).Single(s => s.Id == _remoteSeriesId);

        var card = SeriesCardSample.FromSeries(series);

        Assert.True(card.IsRemote);
        Assert.Equal("Den PC", card.RemoteSourceName);
        Assert.Contains("on Den PC", card.Sub);                        // visible in every view, not just colour or an icon
        Assert.True(card.HasFile);                                     // no local file, yet it opens
        Assert.False(card.Missing);
        Assert.Equal(_remoteIssueId, card.CoverIssueId);
    }

    [Fact]
    public void ALocalSeriesCard_IsUnchanged()
    {
        using var context = Ctx();
        var series = context.Series.Include(s => s.Issues).Single(s => s.Name == "Local Series");

        var card = SeriesCardSample.FromSeries(series);

        Assert.False(card.IsRemote);
        Assert.Null(card.RemoteSourceName);
        Assert.DoesNotContain(" on ", card.Sub);
        Assert.True(card.HasFile);                                     // it has a real file
    }

    [Fact]
    public void ARemoteSeriesWithNoBooksYet_StillCountsAsReadableNotAsAPlaceholder()
    {
        using var context = Ctx();
        var empty = new Series { Name = "Empty Remote", RemoteSourceId = context.RemoteSources.Single().Id, RemoteSeriesId = 9 };
        context.Series.Add(empty);
        context.SaveChanges();
        var loaded = context.Series.Include(s => s.Issues).Include(s => s.RemoteSource).Single(s => s.Id == empty.Id);

        Assert.True(SeriesCardSample.FromSeries(loaded).HasFile);
    }

    [Fact]
    public void ARemoteIssueRow_IsMarkedRemote_AndReadable()
    {
        using var context = Ctx();
        var issue = context.Issues.Include(i => i.Tags).Single(i => i.Id == _remoteIssueId);
        var series = context.Series.Include(s => s.RemoteSource).Single(s => s.Id == _remoteSeriesId);

        var row = IssueListRow.FromIssue(issue, series);

        Assert.True(row.IsRemote);
        Assert.Equal("Den PC", row.RemoteSourceName);
        Assert.True(row.HasFile);
        Assert.Null(row.FilePath);
    }

    [Fact]
    public void ALocalIssueRow_IsNotRemote()
    {
        using var context = Ctx();
        var issue = context.Issues.Single(i => i.Id == _localIssueId);
        var series = context.Series.Single(s => s.Id == issue.SeriesId);

        var row = IssueListRow.FromIssue(issue, series);

        Assert.False(row.IsRemote);
        Assert.True(row.HasFile);
    }

    [Fact]
    public void TheLibraryLoad_ListsRemoteAndLocalSeriesTogether_ButAnythingElseSeesOnlyLocal()
    {
        // Loads on construction (against the redirected test database), exactly as opening the Library does.
        var library = new LibraryScreenViewModel(goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { });

        Assert.Contains(library.Covers, c => c.Name == "Saga" && c.IsRemote);
        Assert.Contains(library.Covers, c => c.Name == "Local Series" && !c.IsRemote);

        // A default (non-opted-in) context - what every scan/repair/write job uses - sees only the local one.
        using var jobContext = PaperbunkrDb.CreateContext();
        Assert.Equal(new[] { "Local Series" }, jobContext.Series.Select(s => s.Name).ToList());
        Assert.Single(jobContext.Issues);
    }

    // ---------------------------------------------------------------- read-only gating

    private (LibraryScreenViewModel Vm, List<(string Title, string Detail)> Toasts, List<int> Editors, List<IReadOnlyList<int>> BulkSeries) NewLibrary()
    {
        var toasts = new List<(string, string)>();
        var editors = new List<int>();
        var bulkSeries = new List<IReadOnlyList<int>>();
        var vm = new LibraryScreenViewModel(
            goDetail: _ => { }, goReaderForIssue: _ => { }, goToNewIssueProperties: (_, _, _) => { },
            goIssueProperties: editors.Add, showToast: (t, d) => toasts.Add((t, d)),
            goBulkSeriesProperties: bulkSeries.Add);
        return (vm, toasts, editors, bulkSeries);
    }

    private static IEnumerable<string?> Headers(IReadOnlyList<Paperbunkr.App.ContextMenus.ContextMenuEntry>? entries) =>
        (entries ?? Array.Empty<Paperbunkr.App.ContextMenus.ContextMenuEntry>())
            .SelectMany(e => new[] { e.Header }.Concat(e.Children?.Select(c => c.Header) ?? Array.Empty<string?>()));

    [Fact]
    public void ARemoteIssueMenu_OffersOnlyWhatIsSafe_AndALocalOneStillOffersEverything()
    {
        var (vm, _, _, _) = NewLibrary();
        var menus = new LibraryContextMenuBuilder(vm);
        using var context = Ctx();
        var remoteRow = IssueListRow.FromIssue(context.Issues.Single(i => i.Id == _remoteIssueId), context.Series.Include(s => s.RemoteSource).Single(s => s.Id == _remoteSeriesId));
        var localRow = IssueListRow.FromIssue(context.Issues.Single(i => i.Id == _localIssueId), context.Series.Single(s => s.Name == "Local Series"));

        var remote = Headers(menus.Build(remoteRow)).Where(h => h is not null).ToList();
        var local = Headers(menus.Build(localRow)).Where(h => h is not null).ToList();

        Assert.Contains("Open", remote);
        Assert.Contains("Go to Series", remote);
        Assert.Contains("Read", remote);                                   // your own reading state is yours to keep
        Assert.DoesNotContain(remote, h => h!.Contains("Edit") || h.Contains("Delete") || h.Contains("Scrape") || h.Contains("Organize")
            || h.Contains("Write metadata") || h.Contains("Explorer") || h.Contains("Collection") || h.Contains("Reading List") || h.Contains("Quick Rate"));

        Assert.Contains("Edit Properties…", local);                        // the local menu is untouched
        Assert.Contains(local, h => h!.StartsWith("Delete"));
        Assert.Contains(local, h => h!.StartsWith("Scrape"));
    }

    [Fact]
    public void ARemoteSeriesMenu_IsJustOpenAndSelect()
    {
        var (vm, _, _, _) = NewLibrary();
        var menus = new LibraryContextMenuBuilder(vm);
        using var context = Ctx();
        var remoteCard = SeriesCardSample.FromSeries(context.Series.Include(s => s.Issues).Include(s => s.RemoteSource).Single(s => s.Id == _remoteSeriesId));
        var localCard = SeriesCardSample.FromSeries(context.Series.Include(s => s.Issues).Single(s => s.Name == "Local Series"));

        var remote = Headers(menus.Build(remoteCard)).Where(h => h is not null).ToList();
        var local = Headers(menus.Build(localCard)).Where(h => h is not null).ToList();

        Assert.Equal(new[] { "Open Series", "Select All", "Clear Selection" }, remote);
        Assert.Contains(local, h => h!.StartsWith("Delete"));
        Assert.Contains("Content Type", local);
    }

    [Fact]
    public void EditingARemoteBook_IsRefusedWithAMessage_NeverOpeningTheEditor()
    {
        var (vm, toasts, editors, _) = NewLibrary();

        vm.EditIssuePropertiesCommand.Execute(_remoteIssueId);

        Assert.Empty(editors);
        Assert.Contains(toasts, t => t.Title == "Remote books are read-only");
    }

    [Fact]
    public void EditingALocalBook_StillOpensTheEditor()
    {
        var (vm, toasts, editors, _) = NewLibrary();

        vm.EditIssuePropertiesCommand.Execute(_localIssueId);

        Assert.Equal(new[] { _localIssueId }, editors);
        Assert.Empty(toasts);
    }

    [Fact]
    public void AMixedSelection_EditsOnlyTheLocalBooks_AndSaysWhatWasLeftOut()
    {
        var (vm, toasts, editors, _) = NewLibrary();
        using (var context = Ctx())
        {
            var rows = new[] { _localIssueId, _remoteIssueId }
                .Select(id => IssueListRow.FromIssue(context.Issues.Single(i => i.Id == id), context.Series.Single(s => s.Id == context.Issues.Single(x => x.Id == id).SeriesId)))
                .ToList();
            vm.Selection.SelectAll(rows);
        }

        vm.EditIssuePropertiesCommand.Execute(_localIssueId);

        Assert.Equal(new[] { _localIssueId }, editors);
        Assert.Contains(toasts, t => t.Title == "Remote books are read-only");
    }

    [Fact]
    public void QuickRate_ScrapeOrganize_AndSeriesBulkEdit_AreRefusedForRemoteBooks()
    {
        var (vm, toasts, _, bulkSeries) = NewLibrary();

        vm.OpenQuickRateCommand.Execute(_remoteIssueId);
        vm.ScrapeWithComicVineCommand.Execute(_remoteIssueId);
        vm.OrganizeWithProfileCommand.Execute(_remoteIssueId);
        vm.ScrapeSeriesWithComicVineCommand.Execute(_remoteSeriesId);
        vm.OrganizeSeriesWithProfileCommand.Execute(_remoteSeriesId);
        using (var context = Ctx())
        {
            vm.SeriesSelection.SelectAll(new[] { SeriesCardSample.FromSeries(context.Series.Include(s => s.Issues).Include(s => s.RemoteSource).Single(s => s.Id == _remoteSeriesId)) });
        }

        vm.BulkEditSeriesSelectionCommand.Execute(null);

        Assert.Empty(bulkSeries);
        Assert.Equal(6, toasts.Count(t => t.Title == "Remote books are read-only"));
    }

    [Fact]
    public void DeletingARemoteBook_NeverTouchesTheMirror()
    {
        var (vm, toasts, _, _) = NewLibrary();

        vm.DeleteIssueCommand.Execute(_remoteIssueId);
        vm.DeleteSeriesCommand.Execute(_remoteSeriesId);

        using var context = Ctx();
        Assert.True(context.Issues.Any(i => i.Id == _remoteIssueId));
        Assert.True(context.Series.Any(s => s.Id == _remoteSeriesId));
        Assert.Contains(toasts, t => t.Title == "Remote books are read-only");
    }

    [Fact]
    public void MarkingARemoteBookRead_WorksAndIsStoredOnTheClientsOwnRow()
    {
        var (vm, _, _, _) = NewLibrary();

        vm.MarkIssueReadCommand.Execute(_remoteIssueId);

        using var context = Ctx();
        var row = context.Issues.Single(i => i.Id == _remoteIssueId);
        Assert.True(row.LastPageRead >= 5);                                // read through the end of its six pages
        Assert.Null(row.FilePath);
    }

    [Fact]
    public void TheDetailScreenOfARemoteSeries_HasNoEditOrChangeCover_ButALocalOneDoes()
    {
        var vm = new DetailScreenViewModel(goBack: () => { }, goToReader: _ => { }, goToProperties: _ => { }, goToBulkProperties: _ => { });

        vm.LoadSeries(_remoteSeriesId);
        Assert.True(vm.IsRemoteSeries);
        Assert.False(vm.CanEdit);
        Assert.DoesNotContain(vm.Actions, a => a.Label == "Change Cover");

        int localSeriesId;
        using (var context = Ctx()) { localSeriesId = context.Series.Single(s => s.Name == "Local Series").Id; }
        vm.LoadSeries(localSeriesId);
        Assert.False(vm.IsRemoteSeries);
        Assert.Contains(vm.Actions, a => a.Label == "Change Cover");
    }

    [Fact]
    public void TheLibrarySourceFilter_NarrowsToThisComputerOrOneRemoteLibrary()
    {
        var (vm, _, _, _) = NewLibrary();
        Assert.True(vm.HasRemoteSources);
        Assert.Equal(new int?[] { null, 0 }, vm.SourceOptions.Take(2).Select(o => o.Id).ToArray());
        int remoteSourceId = vm.SourceOptions.Last().Id!.Value;

        vm.SourceFilter = 0;
        Assert.All(vm.Covers, c => Assert.False(c.IsRemote));
        Assert.NotEmpty(vm.Covers);

        vm.SourceFilter = remoteSourceId;
        Assert.All(vm.Covers, c => Assert.True(c.IsRemote));
        Assert.NotEmpty(vm.Covers);

        vm.ClearAllFiltersCommand.Execute(null);
        Assert.Null(vm.SourceFilter);
        Assert.Contains(vm.Covers, c => c.IsRemote);
        Assert.Contains(vm.Covers, c => !c.IsRemote);
    }

    // ---------------------------------------------------------------- reader

    [Fact]
    public void TheReader_OpensARemoteBook_DecodesARealPage_AndStoresProgressOnTheClientRow()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { }) { RemoteReader = new RemoteReaderSource(_fetcher) };

        vm.LoadIssue(_remoteIssueId);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(6, vm.PageCount);
        Assert.NotNull(vm.Decoder);
        using (var page = vm.Decoder!.GetPage(2))
        {
            Assert.Equal(800, page.PixelSize.Width);
            Assert.Equal(1200, page.PixelSize.Height);
        }

        vm.GoToPage(3);
        vm.FlushPendingPositionSave();

        using var check = Ctx();
        var row = check.Issues.Single(i => i.Id == _remoteIssueId);
        Assert.Equal(3, row.LastPageRead);                             // progress is the client's own
        Assert.Equal(1, row.OpenCount);
        Assert.Null(row.FilePath);                                     // and still not pretending to be a local file
    }

    [Fact]
    public void TheReader_ExplainsARemoteBookWhosePageCountIsNotKnownYet()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { }) { RemoteReader = new RemoteReaderSource(_fetcher) };

        vm.LoadIssue(_unknownCountIssueId);

        Assert.Contains("Refresh the remote library", vm.ErrorMessage);
    }

    [Fact]
    public void TheReader_StillOpensALocalBook_Normally()
    {
        var vm = new ReaderScreenViewModel(goBack: () => { }) { RemoteReader = new RemoteReaderSource(_fetcher) };

        vm.LoadIssue(_localIssueId);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, vm.PageCount);
        Assert.Equal(0, _pages.Requests);                              // nothing went to the network
    }
}
