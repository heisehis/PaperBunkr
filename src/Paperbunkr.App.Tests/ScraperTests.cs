using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Scraper;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views.Preferences;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Xunit;

namespace Paperbunkr.App.Tests;

/// <summary>Preferences → Organize &amp; Scrape, and the app-side scrape coordinator (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 8).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ScraperTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_scraper_{Guid.NewGuid():N}.db");
    private readonly List<ActivityRun> _runs = new();
    private readonly List<int> _writeBacks = new();

    public ScraperTests()
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

    private OrganizeScrapeSettingsViewModel CreateSettingsVm() => new(NewContext, () => { });

    [Fact]
    public void Settings_LoadWithCeDefaults_AndSaveRoundTripsEveryField()
    {
        var vm = CreateSettingsVm();
        vm.Load();
        Assert.True(vm.ConfirmIssueMatch);
        Assert.False(vm.AutoChooseTopMatch);
        Assert.Equal("100", vm.MaxSearchResults);
        Assert.All(vm.FieldToggles, t => Assert.True(t.IsEnabled));

        vm.AutoChooseTopMatch = true;
        vm.IgnoreBlankValues = true;
        vm.IgnoreBeforeYear = "1990";
        vm.IgnoredPublishers = "Panini\nAbril";
        vm.IgnoredSearchTerms = "annual";
        vm.ImprintOverrides = "Vertigo --> DC Comics";
        vm.FieldToggles.Single(t => t.Field == ScrapeField.Summary).IsEnabled = false;
        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasInfoStatus);
        var reloaded = CreateSettingsVm();
        reloaded.Load();
        Assert.True(reloaded.AutoChooseTopMatch);
        Assert.Equal("1990", reloaded.IgnoreBeforeYear);
        Assert.Contains("Panini", reloaded.IgnoredPublishers);
        Assert.Contains("Vertigo --> DC Comics", reloaded.ImprintOverrides);
        Assert.False(reloaded.FieldToggles.Single(t => t.Field == ScrapeField.Summary).IsEnabled);
        Assert.True(reloaded.FieldToggles.Single(t => t.Field == ScrapeField.Title).IsEnabled);
    }

    [Theory]
    [InlineData("MaxSearchResults", "abc")]
    [InlineData("MaxSearchResults", "0")]
    [InlineData("IgnoreBeforeYear", "-5")]
    [InlineData("NeverIgnoreThreshold", "99999999")]
    public void ABadNumber_IsRefused_AndNothingIsWritten(string property, string value)
    {
        var vm = CreateSettingsVm();
        vm.Load();
        typeof(OrganizeScrapeSettingsViewModel).GetProperty(property)!.SetValue(vm, value);

        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasErrorStatus);
        using var context = NewContext();
        Assert.Empty(context.ScrapeSettingsRows);
    }

    [Fact]
    public void ABadImprintLine_IsRefusedWithTheExpectedShape()
    {
        var vm = CreateSettingsVm();
        vm.Load();
        vm.ImprintOverrides = "Vertigo";

        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasErrorStatus);
        Assert.Contains("-->", vm.StatusMessage);
    }

    private sealed class FakeComicVine : IScrapeComicVine
    {
        public int SearchCalls;

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            IReadOnlyList<ComicVineVolumeSearchResult> found = page == 1
                ? new[] { new ComicVineVolumeSearchResult(1, "Batman", "1990", "DC Comics", 50, null) }
                : Array.Empty<ComicVineVolumeSearchResult>();
            return Task.FromResult(found);
        }

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineVolumeDetails?>(null);

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(Array.Empty<ComicVineIssueSummary>());

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineIssueDetails?>(null);
    }

    private ScrapeCoordinator Coordinator(FakeComicVine? comicVine) => new(
        new NativePluginModalHostViewModel(),
        NewContext,
        new ActivityService(dispatch: a => a(), recordRun: _runs.Add),
        _writeBacks.Add,
        (_, _) => comicVine is null ? null : (IScrapeComicVine)comicVine);

    private int SeedIssue()
    {
        using var context = NewContext();
        var issue = new Issue { Series = new Series { Name = "Batman" }, Number = "3", FilePath = "C:/x/batman3.cbz" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public async Task WithoutAComicVineKey_TheScrapeSaysWhereToAddOne_AndDoesNothing()
    {
        var id = SeedIssue();

        var message = await Coordinator(null).ScrapeIssuesAsync(new[] { id });

        Assert.Equal(ScrapeCoordinator.NoKeyMessage, message);
        Assert.Empty(_runs);
        Assert.Empty(_writeBacks);
    }

    [Fact]
    public async Task AnUnattendedScrape_NeverAsks_AppliesWhenAutoChooseIsOn_AndIsOneActivityJob()
    {
        var id = SeedIssue();
        using (var context = NewContext())
        {
            new ScrapeSettings { AutoChooseTopMatch = true }.Save(context);
        }

        var message = await Coordinator(new FakeComicVine()).ScrapeIssuesAsync(new[] { id }, isInteractive: false);

        Assert.Contains("Applied a ComicVine match to 1 of 1", message);
        using var check = NewContext();
        Assert.Equal("DC Comics", check.Issues.Single(i => i.Id == id).Publisher);
        Assert.Equal(new[] { id }, _writeBacks);                          // the file is queued for write-back
        var run = Assert.Single(_runs);
        Assert.Equal(ActivityJobKind.Scrape, run.Kind);
        Assert.Equal(ActivityRunStatus.Succeeded, run.Status);
    }

    [Fact]
    public async Task AnUnattendedScrape_WithAutoChooseOff_SkipsInsteadOfOpeningADialog()
    {
        var id = SeedIssue();     // default settings: auto-choose off

        var message = await Coordinator(new FakeComicVine()).ScrapeIssuesAsync(new[] { id }, isInteractive: false);

        Assert.Contains("0 of 1", message);
        using var check = NewContext();
        Assert.Null(check.Issues.Single(i => i.Id == id).Publisher);
    }

    [Fact]
    public async Task ScrapeUnscraped_OnlyTouchesComicsWithNoVolumeLink()
    {
        var unscraped = SeedIssue();
        int scraped;
        using (var context = NewContext())
        {
            var issue = new Issue { Series = new Series { Name = "Done" }, Number = "1", FilePath = "C:/x/done.cbz", Volume = "12345" };
            context.Issues.Add(issue);
            context.SaveChanges();
            scraped = issue.Id;
            new ScrapeSettings { AutoChooseTopMatch = true }.Save(context);
        }

        var comicVine = new FakeComicVine();
        await Coordinator(comicVine).ScrapeUnscrapedAsync(CancellationToken.None);

        Assert.Equal(1, comicVine.SearchCalls);                           // only the unscraped one was searched
        Assert.Equal(new[] { unscraped }, _writeBacks);
        Assert.NotEqual(scraped, unscraped);
    }

    [Fact]
    public void TheSeriesPanel_IsOfferedForComics_ButNotForTheMangaFamily()
    {
        TestAppBuilder.EnsureInitialized();
        var coordinator = Coordinator(new FakeComicVine());

        Assert.NotNull(coordinator.CreateSeriesPanel(new Series { Name = "Batman", ContentType = ContentType.Comic }));
        Assert.NotNull(coordinator.CreateSeriesPanel(new Series { Name = "Unclassified", ContentType = ContentType.Unknown }));   // most real comics are never explicitly classified
        Assert.Null(coordinator.CreateSeriesPanel(new Series { Name = "One Piece", ContentType = ContentType.Manga }));
    }

    /// <summary>Proves each new view's compiled XAML was woven (see CLAUDE.md, "adding a new Avalonia View").</summary>
    [Fact]
    public void TheNewViews_Construct()
    {
        TestAppBuilder.EnsureInitialized();
        var vm = CreateSettingsVm();
        vm.Load();

        Assert.NotNull(new OrganizeScrapeSection { DataContext = vm }.Content);
        // (ConnectionsSection needs the app's own theme resources such as PbRadiusSm, which the headless test app doesn't load, so it is verified by its compiled
        // bindings building cleanly rather than by constructing it here.)
        Assert.NotNull(new AcquisitionSection { DataContext = new AcquisitionSettingsViewModel(NewContext, () => { }) }.Content);
        Assert.NotNull(new ScrapeBatchHeaderView { DataContext = new ScrapeBatchHeaderViewModel(3, () => { }) }.Content);
        Assert.NotNull(new ComicVineIssueReviewDialogView
        {
            DataContext = new ComicVineIssueReviewDialogViewModel("Batman #3", new[] { new ComicVineIssueSummary(1, "3", "The Beginning", null) }, null, readOnlyPeek: false, _ => { }),
        }.Content);
        Assert.NotNull(new ComicVineMatchReviewDialogView
        {
            DataContext = new ComicVineMatchReviewDialogViewModel(
                "Batman #3", "Batman", new[] { (new ComicVineVolumeSearchResult(1, "Batman", "1990", "DC Comics", 50, null), 42.0) },
                (_, _) => Task.FromResult<IReadOnlyList<(ComicVineVolumeSearchResult, double)>>(Array.Empty<(ComicVineVolumeSearchResult, double)>()), _ => { }, loadIssues: _ => Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(Array.Empty<ComicVineIssueSummary>())),
        }.Content);
    }
}
