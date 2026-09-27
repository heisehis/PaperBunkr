using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
        // ScrapeDelayMs's clamped 2-3600s inter-book delay (docs/superpowers/specs/2026-09-24-
        // comicvine-scraper-fidelity-design.md Phase 3) would otherwise add real multi-second waits to
        // any test scraping more than one book - disabled for every test in this class, same test-seam
        // pattern as ComicVineClient.RetryDelay.
        ScrapeOrchestrator.ScrapeDelayOverride = TimeSpan.Zero;
    }

    public void Dispose()
    {
        ScrapeOrchestrator.ScrapeDelayOverride = null;
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

    /// <summary>A multi-issue volume, unlike <see cref="SingleIssueSearchJson"/> - needed so a test can
    /// prove <c>FindByNumber</c> actually used the book's own resolved number to pick #3 out of several,
    /// rather than accidentally passing via the single-issue-volume fallback that also exists in
    /// <c>FindIssueDetailsAsync</c>.</summary>
    private const string MultiIssueSearchJson =
        """
        {"status_code":1,"error":"OK","results":[{"id":553,"name":"Origins","issue_number":"1","image":null},{"id":555,"name":"The Beginning","issue_number":"3","image":null},{"id":557,"name":"Aftermath","issue_number":"5","image":null}]}
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

        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => { reviewCalled = true; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.Equal(1, result.Applied);
        Assert.False(reviewCalled);
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("DC Comics", context.Issues.Single(i => i.Id == 1).Publisher);
    }

    [Fact]
    public async Task PendingNumberProposal_StillResolvesTheRightIssue_ForPerIssueDetails()
    {
        // Real bug (2026-09-24, found live by the user): a freshly-imported book's filename-parsed
        // issue number lives in a Pending MetadataProposal (LibraryFolderScanner) until a human
        // accepts it in Needs Review or edits+saves the issue directly - issue.Number itself stays
        // null until then. The orchestrator used to read issue.EffectiveNumber() directly at every
        // FindByNumber call site, which only ever resolves an *Accepted* proposal, so it always passed
        // a null bookNumber and could never find this book's own issue within a multi-issue volume -
        // every per-issue field (Title/Summary/credits/dates/...) silently never applied for a whole
        // series scrape, except whichever book already had Number set directly.
        var issue = MakeIssue();
        issue.Number = null;
        Seed(issue);
        using (var context = CreateDbContext())
        {
            context.MetadataProposals.Add(new MetadataProposal
            {
                IssueId = issue.Id,
                Field = MetadataProposalField.Number,
                ProposedValue = "3",
                Status = MetadataProposalStatus.Pending,
            });
            context.SaveChanges();
        }

        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, MultiIssueSearchJson, IssueDetailsJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null),
            CreateDbContext);

        Assert.Equal(1, result.Applied);
        using PaperbunkrDbContext context2 = CreateDbContext();
        var saved = context2.Issues.Single(i => i.Id == issue.Id);
        // Only reachable if bookNumber="3" (from the Pending proposal) was actually threaded through to
        // FindByNumber to pick issue id 555 out of the three in MultiIssueSearchJson.
        Assert.Equal("The Beginning", saved.Title);
        Assert.Equal("Zero Year", saved.StoryArc);
    }

    [Fact]
    public async Task Scrape_WritesCount_AndTheForkFields_IncludingRealArcReadingOrder()
    {
        // Volume "Batman" reports 50 issues; the matched issue (#3, id 555) belongs to arc id 1
        // ("Zero Year"), whose two issues ComicVine dates so that #3 (id 555) comes second.
        const string arcJson = """{"status_code":1,"error":"OK","results":{"id":1,"name":"Zero Year","issues":[{"id":555},{"id":600}]}}""";
        const string arcDatesJson =
            """
            {"status_code":1,"error":"OK","results":[
              {"id":555,"issue_number":"3","store_date":"1990-03-15","cover_date":"1990-04-25"},
              {"id":600,"issue_number":"1","store_date":"1990-01-01","cover_date":"1990-02-01"}]}
            """;
        var issue = MakeIssue();
        Seed(issue);
        var handler = new FakeHttpMessageHandler(VolumeSearchJson, SingleIssueSearchJson, IssueDetailsJson, arcJson, arcDatesJson);
        var orchestrator = new ScrapeOrchestrator(Cv(handler), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = true });

        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null), CreateDbContext);

        Assert.Equal(1, result.Applied);
        using PaperbunkrDbContext context = CreateDbContext();
        var saved = context.Issues.Single(i => i.Id == issue.Id);
        Assert.Equal(50, saved.Count);                        // the volume's issue count
        Assert.Equal("Batman", saved.MainCharacterOrTeam);    // first character in the response
        Assert.Equal("Zero Year", saved.SeriesGroup);
        Assert.Equal("2", saved.StoryArcNumber);              // dated second of the arc's two issues
        Assert.Equal("2", saved.AlternateNumber);
        Assert.Equal(2, saved.AlternateCount);
        Assert.Contains(handler.RequestedUrls, u => u.Contains("story_arc/4045-1"));
    }

    [Fact]
    public async Task AcceptedNumberProposal_NotIncludedOnTheLoadedIssue_StillResolvesTheRightIssue()
    {
        // The realistic case behind the same live bug: MetadataResolutionPolicy.Prompt has no
        // Preferences toggle to ever reach, so every real library runs under the default Automatic
        // policy, under which LibraryFolderScanner.AddFilenameProposal creates the Number/Year
        // proposal already Accepted - issue.Number itself is still never written directly. The
        // orchestrator's real caller (ScrapeCoordinator.ScrapeIssuesAsync) loads books via a bare
        // .Include(i => i.Series), never .Include(i => i.MetadataProposals), so issue.EffectiveNumber()
        // always saw an empty in-memory proposals collection and returned null even though the DB had
        // a perfectly good Accepted proposal the whole time. Mirrors that exact shape: the seeded issue
        // instance passed into ScrapeAsync has an empty MetadataProposals collection, same as a fresh
        // .Include(i => i.Series)-only load would.
        var issue = MakeIssue();
        issue.Number = null;
        Seed(issue);
        using (var context = CreateDbContext())
        {
            context.MetadataProposals.Add(new MetadataProposal
            {
                IssueId = issue.Id,
                Field = MetadataProposalField.Number,
                ProposedValue = "3",
                Status = MetadataProposalStatus.Accepted,
            });
            context.SaveChanges();
        }

        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, MultiIssueSearchJson, IssueDetailsJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null),
            CreateDbContext);

        Assert.Equal(1, result.Applied);
        using PaperbunkrDbContext context2 = CreateDbContext();
        var saved = context2.Issues.Single(i => i.Id == issue.Id);
        Assert.Equal("The Beginning", saved.Title);
        Assert.Equal("Zero Year", saved.StoryArc);
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

        var result = await orchestrator.ScrapeAsync(
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

        Assert.Equal(2, result.Applied);
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

        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext);

        Assert.Equal(1, result.Applied);
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

        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true, (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null), CreateDbContext);

        Assert.Equal(0, result.Applied);
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
        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: false, interactiveReview: null, CreateDbContext);

        Assert.Equal(0, result.Applied);
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
    public async Task ConvertImprints_false_reproduces_CEs_real_off_behavior()
    {
        // comicbook.py:464-466 (verified): with convert_imprints_b off, the raw ComicVine publisher
        // string ("Vertigo" itself, not "DC Comics") writes straight to Publisher, and Imprint is left
        // alone - the literal alternate branch, not just "skip resolution and do nothing".
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VertigoVolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true, ConvertImprints = false };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Issue saved = context.Issues.Single(i => i.Id == 1);
        Assert.Equal("Vertigo", saved.Publisher);
        Assert.Null(saved.Imprint);
    }

    [Fact]
    public async Task PublisherAliases_applies_to_both_the_resolved_publisher_and_the_imprint()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VertigoVolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings
        {
            AutoChooseTopMatch = true,
            PublisherAliases = new Dictionary<string, string> { ["DC Comics"] = "DC", ["Vertigo"] = "Vertigo (DC)" },
        };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Issue saved = context.Issues.Single(i => i.Id == 1);
        Assert.Equal("DC", saved.Publisher);          // resolved via the static imprint table, then aliased
        Assert.Equal("Vertigo (DC)", saved.Imprint);  // the raw imprint name, aliased independently
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
        Assert.Contains("Batman", requestedUrl, StringComparison.OrdinalIgnoreCase);   // cleaned to lowercase, as CE does
        Assert.DoesNotContain("Annual", requestedUrl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("query=The", requestedUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SeriesSearch_UsesTheFuzzySearchEndpoint_AndRetriesWithNumberWordsWhenEmpty()
    {
        // 2026-09-25, found live by the user: the old name-filter search was a punctuation-sensitive
        // substring match ("Batman Dark Victory" never found "Batman: Dark Victory"). CE searches the
        // fuzzy /search/ endpoint and, on an empty result, retries once with digits swapped for words.
        var issue = MakeIssue();
        issue.Series!.Name = "Fantastic 4";
        Seed(issue);
        var handler = new FakeHttpMessageHandler(EmptyIssueSearchJson, VolumeSearchJson);
        var orchestrator = new ScrapeOrchestrator(Cv(handler), new ComicVineMatchMemory(CreateDbContext),
            new ScrapeSettings { AutoChooseTopMatch = false });

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null), CreateDbContext);

        Assert.Equal(2, handler.RequestedUrls.Count);
        Assert.All(handler.RequestedUrls, u => Assert.Contains("/api/search/", u));
        // Uri.ToString() shows %20 as a plain space.
        Assert.Contains("query=fantastic 4", handler.RequestedUrls[0]);
        Assert.Contains("query=fantastic four", handler.RequestedUrls[1]);
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
        Assert.Contains("Sci-Fi", requestedUrl, StringComparison.OrdinalIgnoreCase);   // cleaned to lowercase, as CE does
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

        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.Equal(1, result.Applied);
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
        // Bill Finger's role string "writer, artist" applies EVERY recognized role token (docs/
        // superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §2.6, fixing a real bug -
        // this test used to document PersonRoleMap matching only the first token). "artist" itself
        // fans out to both Penciller and Inker (CE's cvdb.py:663-667, verified), so Bill Finger's
        // credit now correctly lands on Writer AND Penciller AND Inker, not just Writer.
        Assert.Equal("Bob Kane, Bill Finger", saved.Writer);
        Assert.Equal("Bill Finger", saved.Penciller);
        Assert.Equal("Bill Finger, Jerry Robinson", saved.Inker);
    }

    [Fact]
    public async Task Scrape_PersistsComicVineIdentity_ForBothTheSeriesAndTheMatchedIssue()
    {
        // docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-design.md §2.7 - CE persists a
        // durable comicvine_issue/comicvine_volume link on every scraped book; Paperbunkr had none.
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, SingleIssueSearchJson, IssueDetailsJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        using PaperbunkrDbContext context = CreateDbContext();
        Issue saved = context.Issues.Single(i => i.Id == 1);
        var seriesExternalId = context.ComicMetadataExternalIds.Single(e => e.EntityKind == ComicMetadataEntityKind.Series);
        Assert.Equal(saved.SeriesId, seriesExternalId.EntityId);
        Assert.Equal(ComicProvider.ComicVine, seriesExternalId.Provider);
        var issueExternalId = context.ComicMetadataExternalIds.Single(e => e.EntityKind == ComicMetadataEntityKind.Issue);
        Assert.Equal(saved.Id, issueExternalId.EntityId);
        Assert.Equal("555", issueExternalId.ExternalId);   // IssueDetailsJson's own "id":555
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

        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.Equal(1, result.Applied);
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

        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.Equal(1, result.Applied);
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
            onProgress: (phase, total, index, issue, bookLabel) => progress.Add((total, index, issue.Id)));

        Assert.Equal(new[] { (2, 1, 1), (2, 2, 2) }, progress);
    }

    [Fact]
    public async Task ScrapeDelayMs_waits_between_books_but_never_before_the_first_or_after_the_last()
    {
        // scrapeengine.py:231-235 (verified) - "wait for the scrape delay to pass after scraping each
        // book...don't do this for...the first book". Asserts via DelayInvocationCount, not wall-clock
        // duration - a real-time assertion here was flaky under a full parallel test-suite run's own
        // CPU/thread-pool contention (confirmed live: a configured 200ms delay measured as 148 real
        // seconds elapsed under that load, nothing to do with this feature's own correctness).
        var issue1 = MakeIssue();
        var issue2 = new Issue { Id = 2, SeriesId = 1, Number = "4", FilePath = "book2.cbz" };
        Seed(issue1);
        using (PaperbunkrDbContext context = CreateDbContext())
        {
            context.Issues.Add(issue2);
            context.SaveChanges();
        }

        var comicVine = Cv(new FakeHttpMessageHandler(
            VolumeSearchJson, EmptyIssueSearchJson, VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        ScrapeOrchestrator.DelayInvocationCount = 0;

        await orchestrator.ScrapeAsync(new[] { issue1, issue2 }, isInteractive: true, null, CreateDbContext);

        // One delay for two books - before the 2nd, none before the 1st or after the 2nd.
        Assert.Equal(1, ScrapeOrchestrator.DelayInvocationCount);
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

        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext,
            interactiveIssueReview: (_, _, issues, autoMatched, _) =>
                Task.FromResult(ComicVineIssueReviewResult.Confirmed(autoMatched ?? issues[0])));

        Assert.Equal(1, result.Applied);
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

        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext,
            interactiveIssueReview: (_, _, _, _, _) => Task.FromResult(ComicVineIssueReviewResult.Skipped));

        Assert.Equal(1, result.Applied);
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

        var result = await orchestrator.ScrapeAsync(
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

        Assert.Equal(0, result.Applied);
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

    // Cover-hash auto-match safety gate (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-
    // plan.md Step 11 verify section) - exercises PassesCoverHashGateAsync end-to-end through
    // ScrapeAsync, not just CoverPerceptualHash in isolation. Swaps the static ScrapeOrchestrator.
    // CoverHttp test seam for a fake handler returning canned image bytes, and getCoverPath for a temp
    // file, so no live network or real cover cache is involved. Fixture images mirror
    // CoverPerceptualHashTests' own tonal-split/tonal-inverse pattern (a solid color hashes near-
    // identically regardless of actual color under average-hash, so a flat fixture proves nothing).

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

    private static byte[] BottomLightTopDarkCover(int size = 64)
    {
        using var image = new Image<Rgba32>(size, size);
        var light = new Rgba32(240, 240, 240);
        var dark = new Rgba32(15, 15, 15);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                image[x, y] = y < size / 2 ? dark : light;
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private const string VolumeSearchWithImageJson =
        """
        {"status_code":1,"error":"OK","results":[{"id":1,"name":"Batman","start_year":"1990","publisher":{"name":"DC Comics"},"count_of_issues":50,"image":{"medium_url":"http://fake.test/cover.png"}}]}
        """;

    [Fact]
    public async Task Cover_hash_gate_passes_and_auto_applies_without_review_when_covers_match()
    {
        var issue = MakeIssue();
        Seed(issue);
        string localCoverPath = Path.Combine(_testRoot, "local-cover.png");
        File.WriteAllBytes(localCoverPath, TopLightBottomDarkCover());
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchWithImageJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings, getCoverPath: _ => localCoverPath);
        HttpClient original = ScrapeOrchestrator.CoverHttp;
        ScrapeOrchestrator.CoverHttp = new HttpClient(new FakeImageHttpMessageHandler(TopLightBottomDarkCover()));
        bool reviewCalled = false;

        ScrapeBatchResult result;
        try
        {
            result = await orchestrator.ScrapeAsync(
                new[] { issue }, isInteractive: true,
                (_, _, _, _, _) => { reviewCalled = true; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
                CreateDbContext);
        }
        finally
        {
            ScrapeOrchestrator.CoverHttp = original;
        }

        Assert.Equal(1, result.Applied);
        Assert.False(reviewCalled);
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("DC Comics", context.Issues.Single(i => i.Id == 1).Publisher);
    }

    [Fact]
    public async Task Cover_hash_gate_fails_and_falls_through_to_interactive_review_when_covers_mismatch()
    {
        var issue = MakeIssue();
        Seed(issue);
        string localCoverPath = Path.Combine(_testRoot, "local-cover.png");
        File.WriteAllBytes(localCoverPath, TopLightBottomDarkCover());
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchWithImageJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings, getCoverPath: _ => localCoverPath);
        HttpClient original = ScrapeOrchestrator.CoverHttp;
        ScrapeOrchestrator.CoverHttp = new HttpClient(new FakeImageHttpMessageHandler(BottomLightTopDarkCover()));
        bool reviewCalled = false;

        ScrapeBatchResult result;
        try
        {
            result = await orchestrator.ScrapeAsync(
                new[] { issue }, isInteractive: true,
                (_, _, _, _, _) => { reviewCalled = true; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
                CreateDbContext);
        }
        finally
        {
            ScrapeOrchestrator.CoverHttp = original;
        }

        // The gate failing falls through to interactive review exactly as if AutoChooseTopMatch were
        // off for this one book (design doc §2.1) - it doesn't mean "no match," it means "don't trust
        // this one without a human looking at it." The review here declines (returns null), so the
        // book is skipped, not force-applied.
        Assert.True(reviewCalled);
        Assert.Equal(0, result.Applied);
    }

    [Fact]
    public async Task Cover_hash_gate_fails_and_skips_silently_in_a_non_interactive_run()
    {
        var issue = MakeIssue();
        Seed(issue);
        string localCoverPath = Path.Combine(_testRoot, "local-cover.png");
        File.WriteAllBytes(localCoverPath, TopLightBottomDarkCover());
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchWithImageJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings, getCoverPath: _ => localCoverPath);
        HttpClient original = ScrapeOrchestrator.CoverHttp;
        ScrapeOrchestrator.CoverHttp = new HttpClient(new FakeImageHttpMessageHandler(BottomLightTopDarkCover()));

        ScrapeBatchResult result;
        try
        {
            // Non-interactive - same headless-automation gate every other low-confidence path in
            // ScrapeAsync already uses: no modal is ever attempted, the book is just skipped-and-logged.
            result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: false, interactiveReview: null, CreateDbContext);
        }
        finally
        {
            ScrapeOrchestrator.CoverHttp = original;
        }

        Assert.Equal(0, result.Applied);
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Null(context.Issues.Single(i => i.Id == 1).Publisher);
    }

    // Cover gate rebuilt to automatcher.py's real find_series_ref (2026-09-25, found live by the user:
    // "it barely automatically matches"). The old gate compared a book's cover with the volume's
    // generic series art - nearly always issue #1's cover - so for any other issue it could not match.

    private const string TwoLookalikeVolumesJson =
        """
        {"status_code":1,"error":"OK","results":[
            {"id":1,"name":"Batman","start_year":"1990","publisher":{"name":"DC Comics"},"count_of_issues":50,"image":{"medium_url":"http://fake.test/series-a.png"}},
            {"id":2,"name":"Batman","start_year":"1990","publisher":{"name":"DC Comics"},"count_of_issues":6,"image":{"medium_url":"http://fake.test/series-b.png"}}]}
        """;

    private const string IssueWithOwnCoverJson =
        """
        {"status_code":1,"error":"OK","results":[{"id":555,"name":"The Beginning","issue_number":"3","image":{"medium_url":"http://fake.test/issue3.png"}},{"id":556,"name":"Next","issue_number":"4","image":{"medium_url":"http://fake.test/issue4.png"}}]}
        """;

    private async Task<(ScrapeBatchResult Result, bool ReviewCalled)> RunGateScenario(string number, string[] responses, Func<string, byte[]> imageFor)
    {
        var issue = MakeIssue();
        issue.Number = number;
        Seed(issue);
        string localCoverPath = Path.Combine(_testRoot, "local-cover.png");
        File.WriteAllBytes(localCoverPath, TopLightBottomDarkCover());
        var orchestrator = new ScrapeOrchestrator(Cv(new FakeHttpMessageHandler(responses)), new ComicVineMatchMemory(CreateDbContext),
            new ScrapeSettings { AutoChooseTopMatch = true }, getCoverPath: _ => localCoverPath);
        HttpClient original = ScrapeOrchestrator.CoverHttp;
        ScrapeOrchestrator.CoverHttp = new HttpClient(new FakeImageHttpMessageHandler(imageFor));
        bool reviewCalled = false;
        try
        {
            var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true,
                (_, _, _, _, _) => { reviewCalled = true; return Task.FromResult<ComicVineVolumeSearchResult?>(null); }, CreateDbContext);
            return (result, reviewCalled);
        }
        finally
        {
            ScrapeOrchestrator.CoverHttp = original;
        }
    }

    [Fact]
    public async Task CoverGate_ComparesTheBooksOwnIssueCover_NotTheGenericSeriesArt()
    {
        // The series art is a different picture from the local file, but issue #3's own cover matches it.
        var result = await RunGateScenario("3",
            [TwoLookalikeVolumesJson, IssueWithOwnCoverJson, IssueDetailsJson],
            url => url.Contains("issue3") ? TopLightBottomDarkCover() : BottomLightTopDarkCover());

        Assert.False(result.ReviewCalled);   // auto-matched: never asked
        Assert.Equal(1, result.Result.Applied);
    }

    [Fact]
    public async Task CoverGate_TpbGuard_BlocksIssueOne_WhenTheTopTwoSeriesHaveLookalikeArt()
    {
        // Same picture for both series' art: a trade paperback and a regular #1 can't be told apart.
        var result = await RunGateScenario("1",
            [TwoLookalikeVolumesJson, TwoLookalikeVolumesJson],
            _ => TopLightBottomDarkCover());

        Assert.True(result.ReviewCalled);
        Assert.Equal(0, result.Result.Applied);
    }

    [Fact]
    public async Task CoverGate_TpbGuard_DoesNotApplyToOtherIssueNumbers()
    {
        // Identical setup to the test above but issue #3 - CE only runs the guard for a first/unnumbered book.
        var result = await RunGateScenario("3",
            [TwoLookalikeVolumesJson, IssueWithOwnCoverJson, IssueDetailsJson],
            _ => TopLightBottomDarkCover());

        Assert.False(result.ReviewCalled);
        Assert.Equal(1, result.Result.Applied);
    }

    // ScrapeBatchResult's four buckets (docs/superpowers/specs/2026-09-24-comicvine-scraper-fidelity-
    // design.md §4.1) - one focused test per classification, distinct from the tests above that mostly
    // care about the *fields written*, not which bucket the outcome landed in.

    [Fact]
    public async Task BatchResult_NormalApply_LandsOnlyInApplied()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.Equal((1, 0, 0, 0), (result.Applied, result.SkippedByUser, result.NoMatchFound, result.Failed));
    }

    // Per-book outcome detail (docs/superpowers/specs/2026-09-24-scraper-review-tables-and-batch-
    // summary-design.md §3.1) - the counts above are computed from Outcomes, so these verify the
    // actual per-book records a batch-summary UI would render: IssueId/BookLabel/Kind/Reason.

    [Fact]
    public async Task Outcomes_NormalApply_RecordsTheMatchedSeriesNameAsTheReason()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        var outcome = Assert.Single(result.Outcomes);
        Assert.Equal(1, outcome.IssueId);
        Assert.Equal("Batman #3", outcome.BookLabel);
        Assert.Equal(ScrapeOutcomeKind.Applied, outcome.Kind);
        Assert.Equal("Matched \"Batman\"", outcome.Reason);
    }

    [Fact]
    public async Task Outcomes_SearchThrows_RecordsTheRealExceptionMessage()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(ErrorJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        var outcome = Assert.Single(result.Outcomes);
        Assert.Equal(ScrapeOutcomeKind.Failed, outcome.Kind);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Reason));
    }

    [Fact]
    public async Task Outcomes_PermanentSkip_ReasonDistinguishesFromAPlainSkip()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        // Simulates ScrapeCoordinator.MarkPermanentlySkipped, which the real dialog's
        // SkipPermanentlyCommand calls synchronously before resolving with null - the orchestrator
        // has no direct signal of "which kind of skip", so it re-reads this same flag afterward.
        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, _, _, _) =>
            {
                using var context = CreateDbContext();
                context.Issues.Single(i => i.Id == issue.Id).ScrapePermanentlySkipped = true;
                context.SaveChanges();
                return Task.FromResult<ComicVineVolumeSearchResult?>(null);
            },
            CreateDbContext);

        var outcome = Assert.Single(result.Outcomes);
        Assert.Equal(ScrapeOutcomeKind.SkippedByUser, outcome.Kind);
        Assert.Equal("Skipped permanently", outcome.Reason);
    }

    [Fact]
    public async Task Outcomes_PlainSkip_ReasonIsJustSkipped()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null),
            CreateDbContext);

        var outcome = Assert.Single(result.Outcomes);
        Assert.Equal(ScrapeOutcomeKind.SkippedByUser, outcome.Kind);
        Assert.Equal("Skipped", outcome.Reason);
    }

    [Fact]
    public async Task BatchResult_UserSkip_LandsOnlyInSkippedByUser()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = false };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        // The review dialog's own "Skip this book" button resolves with null (ComicVineMatchReview
        // DialogViewModel.Skip()) - a real human decision, distinct from a search that found nothing.
        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null),
            CreateDbContext);

        Assert.Equal((0, 1, 0, 0), (result.Applied, result.SkippedByUser, result.NoMatchFound, result.Failed));
    }

    [Fact]
    public async Task BatchResult_EmptySearch_LandsOnlyInNoMatchFound()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(EmptyIssueSearchJson)); // an empty results array - a genuine "nothing found", not a thrown failure
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.Equal((0, 0, 1, 0), (result.Applied, result.SkippedByUser, result.NoMatchFound, result.Failed));
    }

    [Fact]
    public async Task BatchResult_SearchThrows_LandsOnlyInFailed_NotNoMatchFound()
    {
        var issue = MakeIssue();
        Seed(issue);
        // status_code 100 (not 1, and not a retried transient failure - ComicVineClient.RetryDelay's
        // own contract only retries a genuine transport/parse failure) - a real thrown ComicVineException,
        // distinguishable from a search that succeeded and came back empty.
        var comicVine = Cv(new FakeHttpMessageHandler(ErrorJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.Equal((0, 0, 0, 1), (result.Applied, result.SkippedByUser, result.NoMatchFound, result.Failed));
    }

    // CE's real manual-search fallback (scrapeengine.py:262-266, verified) - "no series could be
    // found using the current (automatic or manual) search terms...force the user to choose the
    // search terms." An auto-choose run whose automatic search comes back empty falls through to the
    // interactive dialog instead of silently skipping, as long as a human is actually there to ask.

    [Fact]
    public async Task EmptySearch_WithAutoChooseOn_FallsThroughToInteractiveReview_WhenTheUserPicksAMatch()
    {
        var issue = MakeIssue();
        Seed(issue);
        var picked = new ComicVineVolumeSearchResult(1, "Batman", "1990", "DC Comics", 50, null);
        var comicVine = Cv(new FakeHttpMessageHandler(EmptyIssueSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        bool reviewCalled = false;

        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, candidates, _, _) =>
            {
                reviewCalled = true;
                Assert.Empty(candidates); // the automatic search really did find nothing - an empty list reaches the dialog, not a skip
                return Task.FromResult<ComicVineVolumeSearchResult?>(picked);
            },
            CreateDbContext);

        Assert.True(reviewCalled);
        Assert.Equal((1, 0, 0, 0), (result.Applied, result.SkippedByUser, result.NoMatchFound, result.Failed));
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Equal("DC Comics", context.Issues.Single(i => i.Id == 1).Publisher);
    }

    [Fact]
    public async Task EmptySearch_WithAutoChooseOn_FallsThroughToInteractiveReview_WhenTheUserDeclines()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);
        bool reviewCalled = false;

        var result = await orchestrator.ScrapeAsync(
            new[] { issue }, isInteractive: true,
            (_, _, _, _, _) => { reviewCalled = true; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.True(reviewCalled);
        Assert.Equal((0, 1, 0, 0), (result.Applied, result.SkippedByUser, result.NoMatchFound, result.Failed));
    }

    [Fact]
    public async Task EmptySearch_WithAutoChooseOn_NonInteractive_StillJustSkips_NoDialogEverAttempted()
    {
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        // CE never had a headless mode - this preserves Paperbunkr's own existing headless-automation
        // gate rather than ever attempting a modal on an unattended run.
        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: false, interactiveReview: null, CreateDbContext);

        Assert.Equal((0, 0, 1, 0), (result.Applied, result.SkippedByUser, result.NoMatchFound, result.Failed));
    }

    // Two-phase auto-choose (2026-09-25, found live by the user, verified against scrapeengine.py's
    // real __scrape/__scrape_book): CE never interrupts an auto-choose batch to ask about a book its
    // own automatcher couldn't confidently resolve - it defers that one book to the end of the
    // worklist (BookStatus("DELAYED")) and keeps auto-matching everything else first, only
    // interactively resolving the deferred books once the whole batch's automatic pass is done. The
    // earlier EmptySearch_WithAutoChooseOn_* tests above only ever scraped a single book, so they
    // couldn't distinguish "falls through immediately" from "gets deferred, then reviewed at the end" -
    // both look identical with nothing else in the batch to interleave with.

    [Fact]
    public async Task TwoPhaseAutoChoose_DefersUnmatchedBooksToTheEnd_InsteadOfInterruptingTheBatch()
    {
        var series = new Series { Id = 1, Name = "Batman" };
        var bookA = new Issue { Id = 1, SeriesId = 1, Series = series, Number = "1", FilePath = "a.cbz" };
        var bookB = new Issue { Id = 2, SeriesId = 1, Number = "2", FilePath = "b.cbz" };
        var bookC = new Issue { Id = 3, SeriesId = 1, Number = "3", FilePath = "c.cbz" };
        Seed(bookA);
        using (var context = CreateDbContext())
        {
            // No .Series navigation on bookB/bookC here - EF's Add() cascades to any reachable,
            // not-yet-tracked entity, and the shared in-memory `series` object would collide with the
            // one bookA's own Seed() already wrote under the same Id. Set on the in-memory objects
            // below instead, purely for ScrapeAsync's own issue.Series?.Name read - never re-saved.
            context.Issues.Add(bookB);
            context.Issues.Add(bookC);
            context.SaveChanges();
        }

        bookB.Series = series;
        bookC.Series = series;

        var comicVine = Cv(new FakeHttpMessageHandler(
            VolumeSearchJson, EmptyIssueSearchJson,    // book A: auto-matches cleanly
            EmptyIssueSearchJson,                      // book B: phase-1 auto-match attempt finds nothing -> deferred, no dialog yet
            VolumeSearchJson, EmptyIssueSearchJson,    // book C: auto-matches cleanly, never blocked on book B
            EmptyIssueSearchJson));                    // book B: phase-2 fresh search, now shown to the dialog

        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var reviewedLabels = new List<string>();
        var result = await orchestrator.ScrapeAsync(
            new[] { bookA, bookB, bookC }, isInteractive: true,
            (label, query, candidates, search, ct) => { reviewedLabels.Add(label); return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        // Only the one unmatched book ever reached a dialog - A and C never did.
        Assert.Equal(new[] { "Batman #2" }, reviewedLabels);

        // Outcome order proves A and C applied without ever waiting on B: both Applied entries land
        // before B's Skipped one, which is only recorded once the deferred/Reviewing pass runs.
        Assert.Equal(
            new[] { (1, ScrapeOutcomeKind.Applied), (3, ScrapeOutcomeKind.Applied), (2, ScrapeOutcomeKind.SkippedByUser) },
            result.Outcomes.Select(o => (o.IssueId, o.Kind)).ToArray());
    }

    [Fact]
    public async Task TwoPhaseAutoChoose_ReportsProgress_AutoMatchingThenSeparatelyReviewing()
    {
        var series = new Series { Id = 1, Name = "Batman" };
        var bookA = new Issue { Id = 1, SeriesId = 1, Series = series, Number = "1", FilePath = "a.cbz" };
        var bookB = new Issue { Id = 2, SeriesId = 1, Number = "2", FilePath = "b.cbz" };
        Seed(bookA);
        using (var context = CreateDbContext())
        {
            context.Issues.Add(bookB);
            context.SaveChanges();
        }

        bookB.Series = series;

        var comicVine = Cv(new FakeHttpMessageHandler(
            VolumeSearchJson, EmptyIssueSearchJson, // book A auto-matches
            EmptyIssueSearchJson,                   // book B phase-1 attempt: nothing found, deferred
            EmptyIssueSearchJson));                 // book B phase-2 fresh search

        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var progress = new List<(ScrapePhase Phase, int Total, int Index, int IssueId)>();
        await orchestrator.ScrapeAsync(
            new[] { bookA, bookB }, isInteractive: true,
            (_, _, _, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(null),
            CreateDbContext,
            onProgress: (phase, total, index, issue, bookLabel) => progress.Add((phase, total, index, issue.Id)));

        // The Reviewing phase gets its own separate 1-based count (1 of 1), not a continuation of the
        // AutoMatching phase's count (which would have made it "3 of 2" - nonsensical).
        Assert.Equal(new[]
        {
            (ScrapePhase.AutoMatching, 2, 1, 1),
            (ScrapePhase.AutoMatching, 2, 2, 2),
            (ScrapePhase.Reviewing, 1, 1, 2),
        }, progress);
    }

    [Fact]
    public async Task TwoPhaseAutoChoose_NeverDefers_WhenNothingWouldCatchItLater()
    {
        // Guards the allowDefer gate itself: a genuinely single-phase auto-choose run (no interactive
        // reviewer at all) must keep its old immediate-skip behavior, never silently defer a book that
        // nothing would ever come back to resolve.
        var issue = MakeIssue();
        Seed(issue);
        var comicVine = Cv(new FakeHttpMessageHandler(EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        // isInteractive is true but no reviewer is supplied - same as ScrapeCoordinator would never
        // actually do, but the orchestrator itself must not assume one exists.
        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, interactiveReview: null, CreateDbContext);

        Assert.Equal((0, 0, 1, 0), (result.Applied, result.SkippedByUser, result.NoMatchFound, result.Failed));
    }

    // Step 16's "permanently skip" marker (docs/superpowers/specs/2026-09-24-comicvine-scraper-
    // fidelity-plan.md) - CE's real book.skip_forever() (comicbook.py:109, verified): a marked book is
    // silently excluded from every future scrape, interactive or unattended, without even a search
    // attempt - not just "skipped this one time" like a plain Skip.

    [Fact]
    public async Task PermanentlySkippedIssue_IsExcludedEntirely_NoSearchEverAttempted()
    {
        var issue = MakeIssue();
        issue.ScrapePermanentlySkipped = true;
        Seed(issue);
        var handler = new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson); // would match cleanly if ever reached
        var comicVine = Cv(handler);
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var result = await orchestrator.ScrapeAsync(new[] { issue }, isInteractive: true, null, CreateDbContext);

        Assert.Equal((0, 0, 1, 0), (result.Applied, result.SkippedByUser, result.NoMatchFound, result.Failed));
        Assert.Empty(handler.RequestedUrls); // no search, no issue lookup - nothing was ever attempted
        using PaperbunkrDbContext context = CreateDbContext();
        Assert.Null(context.Issues.Single(i => i.Id == 1).Publisher);
    }

    [Fact]
    public async Task PermanentlySkippedIssue_DoesNotBlockOtherBooksInTheSameBatch()
    {
        // Both issues share one Series object added in a single SaveChanges - splitting this across
        // two CreateDbContext() calls (this file's usual Seed() + a second Add()) would make EF try to
        // insert the same already-saved Series a second time, since a fresh context has no tracking
        // history for it even when the in-memory C# object reference is identical.
        var series = new Series { Id = 1, Name = "Batman" };
        var skipped = new Issue { Id = 1, SeriesId = 1, Series = series, Number = "3", FilePath = "book.cbz", ScrapePermanentlySkipped = true };
        var normal = new Issue { Id = 2, SeriesId = 1, Series = series, Number = "4", FilePath = "book2.cbz" };
        using (PaperbunkrDbContext context = CreateDbContext())
        {
            context.Series.Add(series);
            context.Issues.AddRange(skipped, normal);
            context.SaveChanges();
        }

        var comicVine = Cv(new FakeHttpMessageHandler(VolumeSearchJson, EmptyIssueSearchJson));
        var matchMemory = new ComicVineMatchMemory(CreateDbContext);
        var settings = new ScrapeSettings { AutoChooseTopMatch = true };
        var orchestrator = new ScrapeOrchestrator(comicVine, matchMemory, settings);

        var result = await orchestrator.ScrapeAsync(new[] { skipped, normal }, isInteractive: true, null, CreateDbContext);

        Assert.Equal((1, 0, 1, 0), (result.Applied, result.SkippedByUser, result.NoMatchFound, result.Failed));
        using PaperbunkrDbContext context2 = CreateDbContext();
        Assert.Null(context2.Issues.Single(i => i.Id == 1).Publisher);       // permanently skipped - untouched
        Assert.Equal("DC Comics", context2.Issues.Single(i => i.Id == 2).Publisher); // the other book still applied normally
    }
}
