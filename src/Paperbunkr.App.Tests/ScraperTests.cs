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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
        // CommunityRating is excluded from CE's own default-enabled field set (docs/superpowers/specs/
        // 2026-09-24-comicvine-scraper-fidelity-design.md Phase 1, verified against configuration.py's
        // real __DEFAULT_SCRAPE_FLAGS) - every other field defaults on.
        Assert.All(vm.FieldToggles.Where(t => t.Field != ScrapeField.CommunityRating), t => Assert.True(t.IsEnabled));
        Assert.False(vm.FieldToggles.Single(t => t.Field == ScrapeField.CommunityRating).IsEnabled);

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

    [Fact]
    public void Phase3Settings_LoadWithCeDefaults_AndSaveRoundTrips()
    {
        var vm = CreateSettingsVm();
        vm.Load();
        Assert.True(vm.ConvertImprints);
        Assert.True(vm.ForceSeriesArt);
        Assert.True(vm.ShowCovers);
        Assert.Equal("1000", vm.ScrapeDelayMs);
        Assert.Equal(string.Empty, vm.PublisherAliases);

        vm.ConvertImprints = false;
        vm.ForceSeriesArt = false;
        vm.ShowCovers = false;
        vm.ScrapeDelayMs = "5000";
        vm.PublisherAliases = "Marvel UK --> Marvel";
        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasInfoStatus);
        var reloaded = CreateSettingsVm();
        reloaded.Load();
        Assert.False(reloaded.ConvertImprints);
        Assert.False(reloaded.ForceSeriesArt);
        Assert.False(reloaded.ShowCovers);
        Assert.Equal("5000", reloaded.ScrapeDelayMs);
        Assert.Contains("Marvel UK --> Marvel", reloaded.PublisherAliases);
    }

    [Fact]
    public void ABadPublisherAliasLine_IsRefusedWithTheExpectedShape()
    {
        var vm = CreateSettingsVm();
        vm.Load();
        vm.PublisherAliases = "Marvel UK";

        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasErrorStatus);
        Assert.Contains("-->", vm.StatusMessage);
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
        public string? ImageUrl;
        public bool ThrowOnSearch;

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            if (ThrowOnSearch)
            {
                throw new ComicVineException("simulated failure", apiStatusCode: 100);
            }

            IReadOnlyList<ComicVineVolumeSearchResult> found = page == 1
                ? new[] { new ComicVineVolumeSearchResult(1, "Batman", "1990", "DC Comics", 50, ImageUrl) }
                : Array.Empty<ComicVineVolumeSearchResult>();
            return Task.FromResult(found);
        }

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineVolumeDetails?>(null);

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(Array.Empty<ComicVineIssueSummary>());

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineIssueDetails?>(null);
    }

    private ScrapeCoordinator Coordinator(FakeComicVine? comicVine, Func<int, string?>? getCoverPath = null) => new(
        new NativePluginModalHostViewModel(),
        NewContext,
        new ActivityService(dispatch: a => a(), recordRun: _runs.Add),
        _writeBacks.Add,
        (_, _) => comicVine is null ? null : (IScrapeComicVine)comicVine,
        getCoverPath);

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
    public async Task AnUnattendedScrape_NeverAsks_AndIsOneActivityJobEvenWhenTheCoverGateDeclines()
    {
        // ScrapeCoordinator always wires ScrapeOrchestrator's cover-hash safety gate (docs/superpowers/
        // specs/2026-09-24-comicvine-scraper-fidelity-design.md §2.1) via CoverThumbnailService.
        // GetEffectiveCoverPath - FakeComicVine's candidate carries no ImageUrl and this fixture issue
        // has no real cached cover on disk, so the gate can never confirm a match here, and an
        // unattended run correctly declines to auto-apply on text score alone (CE parity: "can't
        // confirm" means "don't trust it", not "check unavailable" - see PassesCoverHashGateAsync).
        // This test's own purpose is the activity-job/write-back wiring around that outcome, not the
        // gate's own pass/fail logic - that's covered directly by ScrapeOrchestratorTests'
        // Cover_hash_gate_* tests in Paperbunkr.Data.Tests.
        var id = SeedIssue();
        using (var context = NewContext())
        {
            new ScrapeSettings { AutoChooseTopMatch = true }.Save(context);
        }

        var message = await Coordinator(new FakeComicVine()).ScrapeIssuesAsync(new[] { id }, isInteractive: false);

        // docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §4.1 - a gate decline
        // with no reviewer available is the NoMatchFound bucket (nothing written, no exception, no
        // human decision), not Failed, so the job's own ItemsFailed count stays 0 here.
        Assert.Contains("Applied a ComicVine match to 0 of 1", message);
        Assert.Contains("1 no match found", message);
        Assert.DoesNotContain("failed", message);
        using var check = NewContext();
        Assert.Null(check.Issues.Single(i => i.Id == id).Publisher);
        Assert.Equal(new[] { id }, _writeBacks);                          // the file is queued for write-back
        var run = Assert.Single(_runs);
        Assert.Equal(ActivityJobKind.Scrape, run.Kind);
        Assert.Equal(ActivityRunStatus.Succeeded, run.Status);
        Assert.Equal(0, run.ItemsProcessed);
        Assert.Equal(0, run.ItemsFailed);
    }

    [Fact]
    public async Task AnUnattendedScrape_SummarizesARealSearchFailure_SeparatelyFromNoMatchFound()
    {
        var id = SeedIssue();
        using (var context = NewContext())
        {
            new ScrapeSettings { AutoChooseTopMatch = true }.Save(context);
        }

        var message = await Coordinator(new FakeComicVine { ThrowOnSearch = true }).ScrapeIssuesAsync(new[] { id }, isInteractive: false);

        Assert.Contains("Applied a ComicVine match to 0 of 1", message);
        Assert.Contains("1 failed", message);
        Assert.DoesNotContain("no match found", message);
        var run = Assert.Single(_runs);
        Assert.Equal(ActivityRunStatus.Succeeded, run.Status); // a per-book search failure doesn't fail the whole job - same per-item resilience as the rest of the scraper
        Assert.Equal(0, run.ItemsProcessed);
        Assert.Equal(1, run.ItemsFailed);
    }

    /// <summary>Solid-color fixtures don't work for average-hash comparisons (every pixel equals the
    /// image's own mean regardless of the actual color, so any two solid colors hash near-identically)
    /// - a real tonal split is needed, same fixture shape as CoverPerceptualHashTests in
    /// Paperbunkr.Data.Tests.</summary>
    private static byte[] TopLightBottomDarkCover(int size = 64)
    {
        using var image = new Image<Rgba32>(size, size);
        var light = new Rgba32(240, 240, 240);
        var dark = new Rgba32(15, 15, 15);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                image[x, y] = y < size / 2 ? light : dark;
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    /// <summary>Returns the same canned image bytes for every request, regardless of URL - stands in
    /// for <see cref="ScrapeOrchestrator.CoverHttp"/> (a static test seam) so the cover-hash gate never
    /// needs a live network call.</summary>
    private sealed class FakeImageHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;

        public FakeImageHandler(byte[] bytes) => _bytes = bytes;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) });
    }

    [Fact]
    public async Task AnUnattendedScrape_AppliesWhenAutoChooseIsOn_AndTheCoverGateConfirmsTheMatch()
    {
        var id = SeedIssue();
        using (var context = NewContext())
        {
            new ScrapeSettings { AutoChooseTopMatch = true }.Save(context);
        }

        byte[] coverBytes = TopLightBottomDarkCover();
        string localCoverPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_scraper_cover_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(localCoverPath, coverBytes);
        HttpClient originalCoverHttp = ScrapeOrchestrator.CoverHttp;
        ScrapeOrchestrator.CoverHttp = new HttpClient(new FakeImageHandler(coverBytes)); // the candidate's own fetched cover - identical bytes, so the gate sees a perfect match

        string message;
        try
        {
            message = await Coordinator(new FakeComicVine { ImageUrl = "http://fake.test/cover.png" }, getCoverPath: _ => localCoverPath)
                .ScrapeIssuesAsync(new[] { id }, isInteractive: false);
        }
        finally
        {
            ScrapeOrchestrator.CoverHttp = originalCoverHttp;
            try { File.Delete(localCoverPath); } catch (IOException) { }
        }

        Assert.Contains("Applied a ComicVine match to 1 of 1", message);
        using var check = NewContext();
        Assert.Equal("DC Comics", check.Issues.Single(i => i.Id == id).Publisher);
        Assert.Equal(new[] { id }, _writeBacks);
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
    public async Task ScrapeUnscraped_OnlyTouchesComicsWithNeitherAVolumeNorARecordedSource()
    {
        var unscraped = SeedIssue();
        int scraped;
        using (var context = NewContext())
        {
            var issue = new Issue { Series = new Series { Name = "Done" }, Number = "1", FilePath = "C:/x/done.cbz", Volume = "12345" };
            // scraped from a source that knew no start year: the volume stays empty, but the recorded source says it was scraped
            context.Issues.Add(new Issue { Series = new Series { Name = "Yearless" }, Number = "1", FilePath = "C:/x/yearless.cbz", MetadataSource = ComicProvider.Metron });
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
    public async Task TheSeriesPanel_InvokesOnScraped_OnceTheBatchFinishes()
    {
        // Real bug (2026-09-24): the whole-series scrape panel had no completion callback at all, so
        // the Detail screen's Issues tab tiles never reloaded after a whole-series scrape - every field
        // the scrape wrote stayed invisible until the user navigated away and back. onScraped is the
        // fix; this proves the panel actually calls it, and only after the scrape (not before/never).
        //
        // Deliberately a series with zero issues: CreateSeriesPanel's own wired callback always calls
        // ScrapeSeriesAsync with isInteractive left at its true default (matching the real button, which
        // has no other mode) - any seeded issue would hit the cover-hash gate (there's no real decoded
        // cover for a fake file path) and defer into a genuine interactive ComicVineMatchReviewDialog
        // via the real NativePluginModalHostViewModel, which nothing in this test would ever resolve -
        // an unconditional hang, not a flaky timing issue. Zero issues makes ScrapeSeriesAsync return
        // "No issues in this series to scrape." immediately, never touching the modal host at all, which
        // is enough to prove the completion wiring itself (this test's actual point) without needing to
        // fake a real matching cover.
        TestAppBuilder.EnsureInitialized();
        int seriesId;
        using (var context = NewContext())
        {
            var series = new Series { Name = "Batman" };
            context.Series.Add(series);
            context.SaveChanges();
            seriesId = series.Id;
        }

        int reloadCount = 0;
        var coordinator = Coordinator(new FakeComicVine());
        Series seriesEntity;
        using (var context = NewContext())
        {
            seriesEntity = context.Series.Single(s => s.Id == seriesId);
        }

        var control = coordinator.CreateSeriesPanel(seriesEntity, () => reloadCount++);
        var vm = Assert.IsType<SeriesScraperPanelViewModel>(control!.DataContext);
        Assert.Equal(0, reloadCount);

        await vm.ScrapeCommand.ExecuteAsync(null);

        Assert.Equal(1, reloadCount);
        Assert.Equal("No issues in this series to scrape.", vm.StatusMessage);
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
        Assert.NotNull(new ScrapeBatchSummaryDialogView
        {
            DataContext = new ScrapeBatchSummaryDialogViewModel(
                new ScrapeBatchResult(new[] { new ScrapeBookOutcome(1, "Batman #3", ScrapeOutcomeKind.Applied, "Matched \"Batman\"") }), () => { }),
        }.Content);
    }

    [Fact]
    public void AReScrapeStartsOnTheSourceMostOfTheComicsCameFrom_ElseTheDefault()
    {
        static Issue Book(ComicProvider? source) => new() { MetadataSource = source };

        Assert.Equal(ComicProvider.Metron, ScrapeCoordinator.StartingProvider(new[] { Book(ComicProvider.Metron), Book(ComicProvider.Metron), Book(ComicProvider.ComicVine) }, ComicProvider.ComicVine));
        Assert.Equal(ComicProvider.ComicVine, ScrapeCoordinator.StartingProvider(new[] { Book(null), Book(null) }, ComicProvider.ComicVine));          // never scraped: the default
        Assert.Equal(ComicProvider.Metron, ScrapeCoordinator.StartingProvider(new[] { Book(null) }, ComicProvider.Metron));
        Assert.Equal(ComicProvider.ComicVine, ScrapeCoordinator.StartingProvider(new[] { Book(ComicProvider.Metron), Book(ComicProvider.ComicVine) }, ComicProvider.ComicVine));   // a tie goes to the default
        Assert.Equal(ComicProvider.Metron, ScrapeCoordinator.StartingProvider(new[] { Book(ComicProvider.Metron), Book(null), Book(null) }, ComicProvider.ComicVine));                // unscraped ones don't outvote a recorded source
    }
}
