using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>Implementation plan Phase 3 verification for the scrape-and-apply orchestrator: auto-
/// choose vs. interactive review vs. non-interactive skip-and-log, and the overwrite/ignore-blank/
/// enabled-field write gates.</summary>
public sealed class ScrapeOrchestratorTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _dbPath;

    public ScrapeOrchestratorTests()
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

    private static IScrapeComicVine Cv(HttpMessageHandler handler)
    {
        var client = new ComicVineClient("KEY", ComicVineRequestPriority.High, new HttpClient(handler));
        return new ScrapeComicVineAdapter(client, client);
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

    /// <summary>No issues found for the chosen volume - <c>FindIssueDetailsAsync</c> breaks out on the
    /// first empty page and returns null, so <c>ApplyAsync</c> falls back to volume-level fields only.
    /// Appended as the 2nd fixture response to every pre-existing test below that doesn't care about
    /// the new per-issue apply pipeline, so those keep asserting exactly what they did before it
    /// existed.</summary>
    private const string EmptyIssueSearchJson = """{"status_code":1,"error":"OK","results":[]}""";

    private const string SingleIssueSearchJson =
        """
        {"status_code":1,"error":"OK","results":[{"id":555,"name":"The Beginning","issue_number":"3","image":null}]}
        """;

    private const string IssueDetailsJson =
        """
        {"status_code":1,"error":"OK","results":{"id":555,"name":"The Beginning","issue_number":"3","site_detail_url":"https://comicvine.gamespot.com/batman-3/4000-555/","cover_date":"1990-04-25","store_date":"1990-03-15","description":"<p>Batman <b>fights</b> crime.</p>","volume":{"id":1,"name":"Batman"},"story_arc_credits":[{"id":1,"name":"Zero Year"}],"character_credits":[{"id":2,"name":"Batman"},{"id":3,"name":"Joker"}],"team_credits":[{"id":9,"name":"Bat-Family"}],"location_credits":[{"id":4,"name":"Gotham City"}],"person_credits":[{"name":"Bob Kane","role":"writer"},{"name":"Bill Finger","role":"writer, artist"},{"name":"Jerry Robinson","role":"inker"}]}}
        """;

    private const string ErrorJson = """{"status_code":100,"error":"Simulated failure"}""";

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
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        bool reviewCalled = false;

        int applied = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => { reviewCalled = true; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.Equal(1, applied);
        Assert.False(reviewCalled);
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("DC Comics", context.Issues.Single(i => i.Id == 1).Publisher);
    }

    private sealed class FakeMetron : IScrapeComicVine
    {
        public int Searches { get; private set; }

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default)
        {
            Searches++;
            return Task.FromResult<IReadOnlyList<ComicVineVolumeSearchResult>>(page > 1 ? Array.Empty<ComicVineVolumeSearchResult>()
                : new[] { new ComicVineVolumeSearchResult(900, "Batman", "1990", "Metron House", 50, null) });
        }

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineVolumeDetails?>(null);

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(Array.Empty<ComicVineIssueSummary>());

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineIssueDetails?>(null);
    }

    [Fact]
    public async Task SwitchingProviderInTheReviewDialog_MovesTheRestOfTheRunToThatSource_WithItsOwnMatchMemory()
    {
        var first = MakeIssue();
        var second = new Issue { Id = 2, SeriesId = 1, Number = "4", FilePath = "book2.cbz" };
        Seed(first);
        Seed(second);
        second.Series = first.Series;
        var metron = new FakeMetron();
        var orchestrator = new ScrapeOrchestrator(
            Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson)),
            new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings(), ComicProvider.ComicVine,
            p => p == ComicProvider.Metron ? (metron, new ComicVineMatchMemory(CreateDbContext, ComicProvider.Metron)) : null);
        var providersSeenByReview = new List<ComicProvider>();

        int applied = await orchestrator.ScrapeAsync(
            new[] { first, second }, isInteractive: true,
            async (_, _, _, search, ct) =>
            {
                if (orchestrator.Provider == ComicProvider.ComicVine)
                {
                    Assert.True(orchestrator.TrySwitchProvider(ComicProvider.Metron));      // the dialog's source switch
                }

                providersSeenByReview.Add(orchestrator.Provider);
                var results = await search("Batman", ct);
                return results[0].Volume;
            },
            CreateDbContext);

        Assert.Equal(2, applied);
        Assert.Equal(new[] { ComicProvider.Metron, ComicProvider.Metron }, providersSeenByReview);   // the second book stays on Metron
        Assert.Equal(3, metron.Searches);                                   // the dialog search for book 1, then the automatic and dialog searches for book 2
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("Metron House", context.Issues.Single(i => i.Id == 2).Publisher);
        Assert.All(context.Issues.ToList(), i => Assert.Equal(ComicProvider.Metron, i.MetadataSource));      // both books were applied after the switch
        Assert.Equal(0, context.ComicVineMatchMemories.Count(m => m.Provider == ComicProvider.ComicVine));
        Assert.Equal(1, context.ComicVineMatchMemories.Count(m => m.Provider == ComicProvider.Metron));
    }

    [Fact]
    public void SwitchingToASourceThatCannotBeBuilt_LeavesTheRunWhereItWas()
    {
        var orchestrator = new ScrapeOrchestrator(new FakeMetron(), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings(), ComicProvider.ComicVine, _ => null);
        Assert.False(orchestrator.TrySwitchProvider(ComicProvider.Metron));
        Assert.Equal(ComicProvider.ComicVine, orchestrator.Provider);
        Assert.True(orchestrator.TrySwitchProvider(ComicProvider.ComicVine));     // already there: nothing to build
        Assert.False(new ScrapeOrchestrator(new FakeMetron(), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings()).TrySwitchProvider(ComicProvider.Metron));
    }

    [Fact]
    public async Task The_volume_is_the_series_start_year_never_the_sources_volume_id()
    {
        var issue = MakeIssue();
        issue.Volume = "77691";                                  // what an earlier version wrote: ComicVine's volume id
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var orchestrator = new ScrapeOrchestrator(comicVine, new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = true });

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null), CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("1990", context.Issues.Single(i => i.Id == 1).Volume);          // VolumeSearchJson's start_year: replaces the id
    }

    [Fact]
    public async Task An_unknown_start_year_leaves_the_volume_alone_and_overwrite_off_keeps_a_real_one()
    {
        const string noYear = """{"status_code":1,"error":"OK","results":[{"id":1,"name":"Batman","start_year":null,"publisher":{"name":"DC Comics"},"count_of_issues":50,"image":null}]}""";
        var first = MakeIssue();
        first.Volume = "2";
        Seed(first);
        var orchestrator = new ScrapeOrchestrator(Cv(new FakeHttpMessageHandler(noYear, EmptyIssueSearchJson)), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = true });
        await orchestrator.ScrapeAsync(new[] { first }, isInteractive: true, (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null), CreateDbContext);
        using (PaperbunkrDbContext context = CreateDbContext())
        {
            Assert.Equal("2", context.Issues.Single(i => i.Id == 1).Volume);          // no year to write: nothing changed
        }

        var second = new Issue { Id = 2, SeriesId = 1, Number = "4", FilePath = "book2.cbz", Volume = "3" };
        Seed(second);
        var keep = new ScrapeOrchestrator(Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson)), new ComicVineMatchMemory(CreateDbContext),
            new ScrapeSettings { AutoChooseTopMatch = true, OverwriteExisting = false });
        second.Series = first.Series;
        await keep.ScrapeAsync(new[] { second }, isInteractive: true, (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null), CreateDbContext);
        using (PaperbunkrDbContext context = CreateDbContext())
        {
            Assert.Equal("3", context.Issues.Single(i => i.Id == 2).Volume);          // has a value, overwrite off
        }
    }

    [Fact]
    public async Task Interactive_review_applies_whatever_the_user_chose()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        int applied = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext);

        Assert.Equal(1, applied);
    }

    [Fact]
    public async Task Interactive_review_skipping_applies_nothing()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        int applied = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true, (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null), CreateDbContext);

        Assert.Equal(0, applied);
    }

    [Fact]
    public async Task Non_interactive_run_with_auto_choose_off_skips_rather_than_hanging()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

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
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true, EnabledScrapeFields = new HashSet<ScrapeField>() }; // nothing enabled
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

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
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true, OverwriteExisting = false };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("Existing Publisher", context.Issues.Single(i => i.Id == 1).Publisher);
    }

    [Fact]
    public async Task A_chosen_match_is_recorded_in_the_match_memory_for_future_priorscore_boosts()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.True(matchMemory.WasChosen(ComicVineMatchMemory.NormalizeSearchKey("Batman"), 1));
    }

    private const string VertigoVolumeSearchJson =
        """
        {"status_code":1,"error":"OK","results":[{"id":1,"name":"Batman","start_year":"1990","publisher":{"name":"Vertigo"},"count_of_issues":50,"image":null}]}
        """;

    [Fact]
    public async Task Applied_publisher_is_resolved_through_the_static_imprint_table()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VertigoVolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("DC Comics", context.Issues.Single(i => i.Id == 1).Publisher);
    }

    [Fact]
    public async Task A_users_imprint_override_wins_over_the_static_table()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VertigoVolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings
        {
            AutoChooseTopMatch = true,
            ImprintOverrides = new Dictionary<string, string> { ["Vertigo"] = "My Custom Publisher" },
        };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("My Custom Publisher", context.Issues.Single(i => i.Id == 1).Publisher);
    }

    private const string MultiYearVolumeSearchJson =
        """
        {"status_code":1,"error":"OK","results":[
            {"id":1,"name":"Batman","start_year":"1940","publisher":{"name":"DC Comics"},"count_of_issues":50,"image":null},
            {"id":2,"name":"Batman","start_year":"2011","publisher":{"name":"DC Comics"},"count_of_issues":50,"image":null}
        ]}
        """;

    [Fact]
    public async Task IgnoreVolumesBeforeYear_drops_candidates_older_than_the_cutoff()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(MultiYearVolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, IgnoreVolumesBeforeYear = 2000 };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>? seen = null;

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => { seen = candidates; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.NotNull(seen);
        Assert.Single(seen!);
        Assert.Equal(2, seen![0].Volume.Id);
    }

    [Fact]
    public async Task IgnoreVolumesAfterYear_drops_candidates_newer_than_the_cutoff()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(MultiYearVolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, IgnoreVolumesAfterYear = 2000 };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>? seen = null;

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => { seen = candidates; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.NotNull(seen);
        Assert.Single(seen!);
        Assert.Equal(1, seen![0].Volume.Id);
    }

    [Fact]
    public async Task IgnoreVolumesBeforeAndAfterYear_together_keep_only_the_in_range_candidate()
    {
        const string threeYearVolumeSearchJson =
            """
            {"status_code":1,"error":"OK","results":[
                {"id":1,"name":"Batman","start_year":"1940","publisher":{"name":"DC Comics"},"count_of_issues":50,"image":null},
                {"id":2,"name":"Batman","start_year":"1990","publisher":{"name":"DC Comics"},"count_of_issues":50,"image":null},
                {"id":3,"name":"Batman","start_year":"2011","publisher":{"name":"DC Comics"},"count_of_issues":50,"image":null}
            ]}
            """;
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(threeYearVolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, IgnoreVolumesBeforeYear = 1950, IgnoreVolumesAfterYear = 2000 };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>? seen = null;

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => { seen = candidates; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.NotNull(seen);
        Assert.Single(seen!);
        Assert.Equal(2, seen![0].Volume.Id);
    }

    [Fact]
    public async Task NeverIgnoreThreshold_bypasses_the_year_filter_for_a_long_running_series()
    {
        const string longRunningOldVolumeJson =
            """{"status_code":1,"error":"OK","results":[{"id":1,"name":"Batman","start_year":"1940","publisher":{"name":"DC Comics"},"count_of_issues":900,"image":null}]}""";
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(longRunningOldVolumeJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        // Without the threshold, IgnoreVolumesBeforeYear=2000 would drop this 1940 volume entirely.
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, IgnoreVolumesBeforeYear = 2000, NeverIgnoreThreshold = 500 };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>? seen = null;

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => { seen = candidates; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.NotNull(seen);
        Assert.Single(seen!);
        Assert.Equal(1, seen![0].Volume.Id);
    }

    [Fact]
    public async Task NeverIgnoreThreshold_does_not_bypass_a_series_below_the_threshold()
    {
        const string shortOldVolumeJson =
            """{"status_code":1,"error":"OK","results":[{"id":1,"name":"Batman","start_year":"1940","publisher":{"name":"DC Comics"},"count_of_issues":10,"image":null}]}""";
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(shortOldVolumeJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, IgnoreVolumesBeforeYear = 2000, NeverIgnoreThreshold = 500 };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>? seen = null;

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => { seen = candidates; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.NotNull(seen);
        Assert.Empty(seen!);
    }

    [Fact]
    public async Task IgnoredPublishers_drops_a_matching_candidate_case_and_whitespace_insensitively()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(MultiYearVolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, IgnoredPublishers = new HashSet<string> { " dc comics " } };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>? seen = null;

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => { seen = candidates; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.NotNull(seen);
        Assert.Empty(seen!);
    }

    [Fact]
    public async Task IgnoredPublishers_never_excludes_a_series_meeting_the_never_ignore_threshold()
    {
        const string bigDcVolumeJson =
            """{"status_code":1,"error":"OK","results":[{"id":1,"name":"Batman","start_year":"1990","publisher":{"name":"DC Comics"},"count_of_issues":900,"image":null}]}""";
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(bigDcVolumeJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, IgnoredPublishers = new HashSet<string> { "DC Comics" }, NeverIgnoreThreshold = 500 };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>? seen = null;

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => { seen = candidates; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.NotNull(seen);
        Assert.Single(seen!);
    }

    [Fact]
    public async Task IgnoredSearchTerms_are_stripped_whole_word_from_the_outgoing_search_query()
    {
        var issue = MakeIssue(); // series name "Batman"
        issue.Series!.Name = "The Batman Annual";
        Seed(issue);
        var handler = new FakeHttpMessageHandler(VolumeSearchJson);
        var comicVine = Cv(handler);
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        // AutoChooseTopMatch off + a skip callback, so the run never reaches ApplyAsync's own per-issue
        // lookup (extra SearchIssuesAsync/GetIssueDetailsAsync calls) - this test only cares about the
        // one initial SearchVolumesAsync request's query string.
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, IgnoredSearchTerms = new HashSet<string> { "the", "annual" } };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null),
            CreateDbContext);

        // CE's own real substitution (db.py's query_series_refs, verified) doesn't trim/collapse
        // whitespace afterward either - only asserting the stripped words are actually gone and the
        // real series name survives, not an exact encoded query string.
        string requestedUrl = Assert.Single(handler.RequestedUrls);
        Assert.Contains("Batman", requestedUrl);
        Assert.DoesNotContain("Annual", requestedUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("query=The", requestedUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IgnoredSearchTerms_only_strips_plain_alphanumeric_entries()
    {
        // "sci-fi" contains a hyphen, so it isn't alphanumeric - CE's own term.isalnum() guard means an
        // entry like this is silently never applied, not rejected up front.
        var issue = MakeIssue();
        issue.Series!.Name = "Sci-Fi Batman";
        Seed(issue);
        var handler = new FakeHttpMessageHandler(VolumeSearchJson);
        var comicVine = Cv(handler);
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, IgnoredSearchTerms = new HashSet<string> { "sci-fi" } };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null),
            CreateDbContext);

        string requestedUrl = Assert.Single(handler.RequestedUrls);
        Assert.Contains("Sci-Fi", requestedUrl);
    }

    [Fact]
    public async Task MaxSearchResults_caps_the_number_of_candidates_considered()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(MultiYearVolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, MaxSearchResults = 1 };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>? seen = null;

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => { seen = candidates; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.NotNull(seen);
        Assert.Single(seen!);
    }

    [Fact]
    public async Task Full_field_apply_writes_every_per_issue_field_from_the_matched_issue()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, SingleIssueSearchJson, IssueDetailsJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        int applied = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.Equal(1, applied);
        using PaperbunkrDbContext context = CreateDbContext();
        Issue saved = context.Issues.Single(i => i.Id == 1);
        Assert.Equal("3", saved.Number);
        Assert.Equal("The Beginning", saved.Title);
        Assert.Equal("Batman fights crime.", saved.Summary);
        Assert.Equal("https://comicvine.gamespot.com/batman-3/4000-555/", saved.Web);
        Assert.Equal("Zero Year", saved.StoryArc);
        Assert.Equal("Batman, Joker", saved.Characters);
        Assert.Equal("Bat-Family", saved.Teams);
        Assert.Equal("Gotham City", saved.Locations);
        Assert.Equal(1990, saved.Year);
        Assert.Equal(4, saved.Month);
        Assert.Equal(25, saved.Day);
        Assert.Equal(new DateTime(1990, 3, 15), saved.ReleasedTime);
        // Bill Finger's role string "writer, artist" maps through PersonRoleMap on its FIRST
        // matching token only (ComicVineService.MapIssueDetails) - "writer" matches before "artist"
        // is ever checked, so his credit lands entirely on Writer, not Penciller.
        Assert.Equal("Bob Kane, Bill Finger", saved.Writer);
        Assert.Null(saved.Penciller);
        Assert.Equal("Jerry Robinson", saved.Inker);
    }

    [Fact]
    public async Task Imprint_field_gets_the_raw_publisher_name_while_Publisher_gets_the_resolved_parent()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VertigoVolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Issue saved = context.Issues.Single(i => i.Id == 1);
        Assert.Equal("DC Comics", saved.Publisher);
        Assert.Equal("Vertigo", saved.Imprint);
    }

    [Fact]
    public async Task Series_name_is_corrected_to_the_chosen_volumes_own_name()
    {
        var issue = MakeIssue();
        issue.Series!.Name = "batman (typo capitalization)";
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("Batman", context.Series.Single(s => s.Id == 1).Name);
    }

    [Fact]
    public async Task A_single_issue_volume_matches_regardless_of_the_books_own_number()
    {
        // A one-shot/TPB volume - only one issue in it - matches that issue even though the book's
        // own number ("3") doesn't equal the fixture's issue_number ("1").
        const string oneShotIssueSearchJson = """{"status_code":1,"error":"OK","results":[{"id":555,"name":"The Beginning","issue_number":"1","image":null}]}""";
        var issue = MakeIssue();
        Seed(issue);
        // The mismatched-number issue finds no number match; with exactly one issue in the volume the fallback fetches details for it.
        // (The shared client returns the volume's issues in one sweep, so there is no empty second page to consume.)
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, oneShotIssueSearchJson, IssueDetailsJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("The Beginning", context.Issues.Single(i => i.Id == 1).Title);
    }

    [Fact]
    public async Task No_matching_issue_number_in_the_volume_still_applies_volume_level_fields_only()
    {
        const string nonMatchingIssueSearchJson =
            """{"status_code":1,"error":"OK","results":[{"id":555,"name":"Different Issue","issue_number":"99","image":null},{"id":556,"name":"Another","issue_number":"100","image":null}]}""";
        var issue = MakeIssue();
        Seed(issue);
        // Page 1 has 2 issues, neither matching the book's number, so the loop pages once more; page 2
        // comes back empty, ending pagination with no match and no single-issue fallback (2 != 1).
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, nonMatchingIssueSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        int applied = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.Equal(1, applied);
        using PaperbunkrDbContext context = CreateDbContext();
        Issue saved = context.Issues.Single(i => i.Id == 1);
        Assert.Equal("DC Comics", saved.Publisher); // volume-level field still applied
        Assert.Null(saved.Title); // no per-issue match, so nothing per-issue was written
    }

    [Fact]
    public async Task A_network_failure_during_the_per_issue_lookup_still_applies_volume_level_fields()
    {
        var issue = MakeIssue();
        Seed(issue);
        // The issue-list call fails - FindIssueDetailsAsync's own try/catch swallows the resulting
        // ComicVineException and returns null, same as "no match found" (volume-level fields still apply).
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, ErrorJson, ErrorJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        int applied = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.Equal(1, applied);
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("DC Comics", context.Issues.Single(i => i.Id == 1).Publisher);
    }

    private const string TwoIssueSearchJson =
        """
        {"status_code":1,"error":"OK","results":[{"id":1,"name":"[Untitled]","issue_number":"1","image":null},{"id":2,"name":"Death of the Dream","issue_number":"2","image":null}]}
        """;

    /// <summary>
    /// Regression test for a real crash-adjacent bug found on first on-screen use: ComicVine's actual
    /// API does not reliably return an empty array once <c>page</c> goes past a small volume's last
    /// page - it repeats the final page's results instead. <see cref="FakeHttpMessageHandler"/>'s own
    /// "repeat the last response once exhausted" behavior (its class doc comment) reproduces exactly
    /// this shape when only one non-empty page is queued, which is what actually happened live (the
    /// issue dialog showed the same 2 issues over and over).
    /// </summary>
    [Fact]
    public async Task LoadIssuesAsync_stops_and_deduplicates_when_ComicVine_repeats_the_last_page_instead_of_returning_empty()
    {
        // Two identical pages, matching the live bug exactly (ComicVine repeating the same page
        // rather than ever signaling "no more results" with an empty array).
        var comicVine = Cv(new FakeHttpMessageHandler(TwoIssueSearchJson, TwoIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, new ScrapeSettings());

        IReadOnlyList<ComicVineIssueSummary> issues = await orchestrator.LoadIssuesAsync(1);

        Assert.Equal(2, issues.Count);
        Assert.Equal(new[] { 1, 2 }, issues.Select(i => i.Id));
    }

    // docs/superpowers/specs/2026-09-13-cluster-scraper-ui-redesign-plan.md Step 6 verification.

    [Fact]
    public async Task OnProgress_fires_once_per_book_with_the_correct_total_and_index()
    {
        var issue1 = MakeIssue();
        var issue2 = new Issue { Id = 2, SeriesId = 1, Number = "4", FilePath = "book2.cbz" };
        Seed(issue1);
        using (PaperbunkrDbContext context = CreateDbContext())
        {
            // No Series navigation set here - Series id 1 already exists from Seed(issue1) above;
            // attaching a second, distinct in-memory Series object with the same Id would make EF
            // try to insert it again and violate the primary key.
            context.Issues.Add(issue2);
            context.SaveChanges();
        }

        var comicVine = Cv(new FakeHttpMessageHandler(
            VolumeSearchJson, EmptyIssueSearchJson, VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        var progress = new List<(int Total, int Index, int IssueId)>();

        await orchestrator.ScrapeAsync(
            new[] { issue1, issue2 }, isInteractive: true, null, CreateDbContext,
            onProgress: (total, index, issue) => progress.Add((total, index, issue.Id)));

        Assert.Equal(new[] { (2, 1, 1), (2, 2, 2) }, progress);
    }

    [Fact]
    public async Task ConfirmIssueMatch_true_routes_through_the_interactive_issue_dialog_and_applies_the_confirmed_issue()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(
            VolumeSearchJson, SingleIssueSearchJson, IssueDetailsJson));       // the shared client returns a volume's issues in one sweep: no empty second page
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        int applied = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext,
            interactiveIssueReview: (_, _, issues, autoMatched, _) =>
                Task.FromResult(ComicVineIssueReviewResult.Confirmed(autoMatched ?? issues[0])));

        Assert.Equal(1, applied);
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("The Beginning", context.Issues.Single(i => i.Id == 1).Title);
    }

    [Fact]
    public async Task ConfirmIssueMatch_true_with_skipped_outcome_applies_volume_level_fields_only()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(
            VolumeSearchJson, SingleIssueSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        int applied = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext,
            interactiveIssueReview: (_, _, _, _, _) => Task.FromResult(ComicVineIssueReviewResult.Skipped));

        Assert.Equal(1, applied);
        using PaperbunkrDbContext context = CreateDbContext();
        Issue saved = context.Issues.Single(i => i.Id == 1);
        Assert.Equal("DC Comics", saved.Publisher);
        Assert.Null(saved.Title);
    }

    [Fact]
    public async Task ConfirmIssueMatch_true_with_went_back_outcome_re_shows_the_series_dialog_for_the_same_book()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(
            VolumeSearchJson, SingleIssueSearchJson, EmptyIssueSearchJson, VolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        int seriesReviewCalls = 0;
        int issueReviewCalls = 0;

        int applied = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) =>
            {
                seriesReviewCalls++;
                // Second time around (after Go Back), skip the series dialog entirely to end the test.
                return Task.FromResult(seriesReviewCalls == 1 ? candidates[0].Volume : null);
            },
            CreateDbContext,
            interactiveIssueReview: (_, _, _, _, _) =>
            {
                issueReviewCalls++;
                return Task.FromResult(ComicVineIssueReviewResult.WentBack);
            });

        Assert.Equal(0, applied);
        Assert.Equal(2, seriesReviewCalls);
        Assert.Equal(1, issueReviewCalls); // the 2nd series pass never reaches the issue dialog (chosen is null)
    }

    [Fact]
    public async Task ConfirmIssueMatch_false_never_calls_the_interactive_issue_delegate()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        bool issueDialogShown = false;

        await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext,
            interactiveIssueReview: (_, _, _, _, _) => { issueDialogShown = true; return Task.FromResult(ComicVineIssueReviewResult.Skipped); });

        Assert.False(issueDialogShown);
    }

    [Fact]
    public async Task Numeric_issue_number_matching_tolerates_leading_zero_differences()
    {
        const string paddedIssueSearchJson = """{"status_code":1,"error":"OK","results":[{"id":555,"name":"The Beginning","issue_number":"03","image":null},{"id":556,"name":"Other","issue_number":"04","image":null}]}""";
        var issue = MakeIssue(); // Number = "3"
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, paddedIssueSearchJson, IssueDetailsJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("The Beginning", context.Issues.Single(i => i.Id == 1).Title);
    }
}
