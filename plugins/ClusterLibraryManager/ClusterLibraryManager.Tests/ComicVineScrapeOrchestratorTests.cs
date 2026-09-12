using ClusterLibraryManager.ComicVine;
using ClusterLibraryManager.Persistence;
using ClusterLibraryManager.Settings;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace ClusterLibraryManager.Tests;

/// <summary>Implementation plan Phase 3 verification for the scrape-and-apply orchestrator: auto-
/// choose vs. interactive review vs. non-interactive skip-and-log, and the overwrite/ignore-blank/
/// enabled-field write gates.</summary>
public sealed class ComicVineScrapeOrchestratorTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;

    public ComicVineScrapeOrchestratorTests()
    {
        _testRoot = Directory.CreateTempSubdirectory("clm-scrape-test-").FullName;
        _dbPath = Path.Combine(_testRoot, "test.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch (IOException) { }
    }

    private PaperbunkrDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private void Seed(Issue issue)
    {
        using PaperbunkrDbContext context = CreateDbContext();
        if (issue.Series is not null && context.Series.Find(issue.Series.Id) is null)
        {
            context.Series.Add(issue.Series);
        }

        context.Issues.Add(issue);
        context.SaveChanges();
    }

    private const string VolumeSearchJson =
        """
        {"status_code":1,"error":"OK","results":[{"id":1,"name":"Batman","start_year":"1990","publisher":{"name":"DC Comics"},"count_of_issues":50,"image":null}]}
        """;

    private static Issue MakeIssue() => new()
    {
        Id = 1,
        SeriesId = 1,
        Series = new Series { Id = 1, Name = "Batman" },
        Number = "3",
        FilePath = "book.cbz",
    };

    [Fact]
    public async Task Auto_choose_applies_the_top_match_without_any_review_callback()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = new ComicVineService("KEY", new HttpClient(new FakeHttpMessageHandler(VolumeSearchJson)));
        var matchMemory = new ComicVineMatchMemory(new PluginDatabase(Path.Combine(_testRoot, "plugin.db")));
        var settings = new PluginSettings { AutoChooseTopMatch = true };
        var orchestrator = new ComicVineScrapeOrchestrator(comicVine, matchMemory, settings);
        bool reviewCalled = false;

        int applied = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _) => { reviewCalled = true; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.Equal(1, applied);
        Assert.False(reviewCalled);
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("DC Comics", context.Issues.Single(i => i.Id == 1).Publisher);
    }

    [Fact]
    public async Task Interactive_review_applies_whatever_the_user_chose()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = new ComicVineService("KEY", new HttpClient(new FakeHttpMessageHandler(VolumeSearchJson)));
        var matchMemory = new ComicVineMatchMemory(new PluginDatabase(Path.Combine(_testRoot, "plugin.db")));
        var settings = new PluginSettings { AutoChooseTopMatch = false };
        var orchestrator = new ComicVineScrapeOrchestrator(comicVine, matchMemory, settings);

        int applied = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, candidates) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext);

        Assert.Equal(1, applied);
    }

    [Fact]
    public async Task Interactive_review_skipping_applies_nothing()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = new ComicVineService("KEY", new HttpClient(new FakeHttpMessageHandler(VolumeSearchJson)));
        var matchMemory = new ComicVineMatchMemory(new PluginDatabase(Path.Combine(_testRoot, "plugin.db")));
        var settings = new PluginSettings { AutoChooseTopMatch = false };
        var orchestrator = new ComicVineScrapeOrchestrator(comicVine, matchMemory, settings);

        int applied = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true, (_, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null), CreateDbContext);

        Assert.Equal(0, applied);
    }

    [Fact]
    public async Task Non_interactive_run_with_auto_choose_off_skips_rather_than_hanging()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = new ComicVineService("KEY", new HttpClient(new FakeHttpMessageHandler(VolumeSearchJson)));
        var matchMemory = new ComicVineMatchMemory(new PluginDatabase(Path.Combine(_testRoot, "plugin.db")));
        var settings = new PluginSettings { AutoChooseTopMatch = false };
        var orchestrator = new ComicVineScrapeOrchestrator(comicVine, matchMemory, settings);

        // interactiveReview is null, matching how the plugin calls this for a non-interactive
        // (Scheduled Task-style) run - must never be invoked and must never hang.
        int applied = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: false, interactiveReview: null, CreateDbContext);

        Assert.Equal(0, applied);
    }

    [Fact]
    public async Task A_disabled_scrape_field_is_never_written_even_when_the_match_has_a_value()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = new ComicVineService("KEY", new HttpClient(new FakeHttpMessageHandler(VolumeSearchJson)));
        var matchMemory = new ComicVineMatchMemory(new PluginDatabase(Path.Combine(_testRoot, "plugin.db")));
        var settings = new PluginSettings { AutoChooseTopMatch = true, EnabledScrapeFields = new HashSet<ScrapeField>() }; // nothing enabled
        var orchestrator = new ComicVineScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Null(context.Issues.Single(i => i.Id == 1).Publisher);
    }

    [Fact]
    public async Task OverwriteExisting_false_does_not_replace_an_already_populated_field()
    {
        Issue issue = MakeIssue();
        issue.Publisher = "Existing Publisher";
        Seed(issue);
        var comicVine = new ComicVineService("KEY", new HttpClient(new FakeHttpMessageHandler(VolumeSearchJson)));
        var matchMemory = new ComicVineMatchMemory(new PluginDatabase(Path.Combine(_testRoot, "plugin.db")));
        var settings = new PluginSettings { AutoChooseTopMatch = true, OverwriteExisting = false };
        var orchestrator = new ComicVineScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("Existing Publisher", context.Issues.Single(i => i.Id == 1).Publisher);
    }

    [Fact]
    public async Task A_chosen_match_is_recorded_in_the_match_memory_for_future_priorscore_boosts()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = new ComicVineService("KEY", new HttpClient(new FakeHttpMessageHandler(VolumeSearchJson)));
        var matchMemory = new ComicVineMatchMemory(new PluginDatabase(Path.Combine(_testRoot, "plugin.db")));
        var settings = new PluginSettings { AutoChooseTopMatch = true };
        var orchestrator = new ComicVineScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.True(matchMemory.WasChosen(ComicVineMatchMemory.NormalizeSearchKey("Batman"), 1));
    }
}
