using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Caching;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>docs/superpowers/specs/2026-10-21-scraper-organizer-b-speed-core-design.md: cache, series-first reuse, pacing, resumable jobs.</summary>
[Collection("ScrapeOrchestratorStatics")]
public sealed class ScrapeSpeedCoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pb-speedcore-").FullName;

    public void Dispose()
    {
        ScrapeOrchestrator.ScrapeDelayOverride = null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private PaperbunkrDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={Path.Combine(_root, "test.db")}").Options;
        var context = new PaperbunkrDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    // ---- ProviderCache ----

    [Fact]
    public void Cache_round_trips_and_survives_a_new_instance()
    {
        var cache = new ProviderCache(Path.Combine(_root, "cache"));
        cache.Set("Metron", ProviderCacheKind.VolumeIssueList, "42", new List<ComicVineIssueSummary> { new(7, "3", "Title", null, "abc") });

        var again = new ProviderCache(Path.Combine(_root, "cache"));
        Assert.True(again.TryGet<List<ComicVineIssueSummary>>("Metron", ProviderCacheKind.VolumeIssueList, "42", out var hit));
        Assert.Equal("abc", Assert.Single(hit!).CoverHash);
    }

    [Fact]
    public void Expired_entries_miss_but_stay_available_as_stale()
    {
        var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        var cache = new ProviderCache(Path.Combine(_root, "cache"), () => now);
        cache.Set("Metron", ProviderCacheKind.SeriesSearch, "batman", new List<string> { "x" });

        now = now.AddHours(25);
        Assert.False(cache.TryGet<List<string>>("Metron", ProviderCacheKind.SeriesSearch, "batman", out _));
        Assert.True(cache.TryGetStale<List<string>>("Metron", ProviderCacheKind.SeriesSearch, "batman", out var stale));
        Assert.Equal("x", Assert.Single(stale!));
    }

    [Fact]
    public void Cover_hashes_never_expire_and_Clear_forgets_everything()
    {
        var now = DateTime.UtcNow;
        var cache = new ProviderCache(Path.Combine(_root, "cache"), () => now);
        cache.Set("Covers", ProviderCacheKind.CoverHash, "http://x/1.jpg", 12345UL);

        now = now.AddYears(5);
        Assert.True(cache.TryGet<ulong>("Covers", ProviderCacheKind.CoverHash, "http://x/1.jpg", out var hash));
        Assert.Equal(12345UL, hash);

        Assert.Equal(1, cache.Clear());
        Assert.False(cache.TryGet<ulong>("Covers", ProviderCacheKind.CoverHash, "http://x/1.jpg", out _));
    }

    [Fact]
    public async Task The_adapter_asks_the_provider_once_for_the_same_series_issue_list_and_details()
    {
        var client = new CountingClient();
        var adapter = new ScrapeComicVineAdapter(client, client, new ProviderCache(Path.Combine(_root, "cache")));

        await adapter.SearchVolumesAsync("Batman");
        await adapter.SearchVolumesAsync("batman ");
        await adapter.SearchIssuesAsync(1);
        await adapter.SearchIssuesAsync(1);
        var first = await adapter.GetIssueDetailsAsync(9);
        var second = await adapter.GetIssueDetailsAsync(9);

        Assert.Equal(1, client.Searches);
        Assert.Equal(1, client.IssueLists);
        Assert.Equal(1, client.Details);
        Assert.Equal(first!.Title, second!.Title);
        Assert.Equal("Robin", Assert.Single(second.Characters).Name);
    }

    [Fact]
    public async Task A_rate_limited_provider_is_answered_from_a_stale_entry()
    {
        var now = DateTime.UtcNow;
        var cache = new ProviderCache(Path.Combine(_root, "cache"), () => now);
        var client = new CountingClient();
        var adapter = new ScrapeComicVineAdapter(client, client, cache);
        await adapter.SearchIssuesAsync(1);

        now = now.AddDays(8);
        client.RateLimited = true;
        var issues = await adapter.SearchIssuesAsync(1);

        Assert.Single(issues);
    }

    [Fact]
    public async Task ComicVine_is_searched_through_volumes_first_and_a_degraded_answer_is_not_kept()
    {
        // 2026-10-22: ComicVine's /search timed out or returned no publisher, 0 issues and the blank cover for every volume.
        var client = new ComicVineKindClient();
        var cache = new ProviderCache(Path.Combine(_root, "cache"));
        var adapter = new ScrapeComicVineAdapter(client, client, cache, searchVolumesFirst: true);

        var found = await adapter.SearchVolumesAsync("X-Men");
        Assert.Equal("Marvel", Assert.Single(found).Publisher);
        Assert.Equal((1, 0), (client.VolumeSearches, client.FuzzySearches));

        client.VolumesEmpty = true;   // nothing by name: /search is the fallback, and its degraded answer is used but not cached
        await adapter.SearchVolumesAsync("Uncanny Men X");
        await adapter.SearchVolumesAsync("Uncanny Men X");
        Assert.Equal(2, client.FuzzySearches);
    }

    [Fact]
    public async Task A_series_found_through_the_issue_number_joins_the_candidates_and_wins()
    {
        // 2026-10-22: "X-Men" #37 (2024 run) - the name search returned only older or longer-named series.
        var books = SeedSeries("37");
        books[0].Series!.Name = "X-Men";
        books[0].Year = 2026;
        var source = new IssueFirstSource();
        var orchestrator = new ScrapeOrchestrator(source, new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false });
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>? offered = null;

        await orchestrator.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => { offered = candidates; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.Equal(("X-Men", "37", 2026), source.Asked);
        Assert.Equal(7914, offered![0].Volume.Id);
    }

    private sealed class IssueFirstSource : IScrapeComicVine, IScrapeIssueFirstSearch
    {
        public (string, string, int?) Asked;

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineVolumeSearchResult>>(page == 1
                ? new[] { new ComicVineVolumeSearchResult(2136, "Astonishing X-Men", "2004", "Marvel", 68, null), new ComicVineVolumeSearchResult(1581, "The X-Men", "1963", "Marvel", 141, null) }
                : Array.Empty<ComicVineVolumeSearchResult>());

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> FindSeriesByIssueAsync(string seriesName, string issueNumber, int? year, CancellationToken cancellationToken = default)
        {
            Asked = (seriesName, issueNumber, year);
            return Task.FromResult<IReadOnlyList<ComicVineVolumeSearchResult>>(new[]
            {
                new ComicVineVolumeSearchResult(1839, "X-Men", "1991", null, null, null),
                new ComicVineVolumeSearchResult(7914, "X-Men", "2024", null, null, null),
            });
        }

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineVolumeDetails?>(null);

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(Array.Empty<ComicVineIssueSummary>());

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineIssueDetails?>(null);
    }

    [Fact]
    public async Task A_special_named_in_the_file_finds_its_own_series()
    {
        // 2026-10-22: "The_Amazing_Spider-Man_(2025-)_#Halloween_Special_2026.cbz", filed under "The Amazing Spider-Man"; Metron has it as its own series.
        int id;
        using (var context = CreateDbContext())
        {
            var issue = new Issue { Series = new Series { Name = "The Amazing Spider-Man" }, Number = "1", Year = 2026, FilePath = "C:/x/The_Amazing_Spider-Man_(2025-)_#Halloween_Special_2026.cbz" };
            context.Issues.Add(issue);
            context.SaveChanges();
            id = issue.Id;
        }

        List<Issue> books;
        using (var context = CreateDbContext())
        {
            books = context.Issues.Include(i => i.Series).Where(i => i.Id == id).ToList();
        }

        var source = new SpecialSource();
        var orchestrator = new ScrapeOrchestrator(source, new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false });
        IReadOnlyList<(ComicVineVolumeSearchResult Volume, double Score)>? offered = null;

        await orchestrator.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => { offered = candidates; return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        Assert.Contains("Halloween Special", source.Queries);
        Assert.Equal(16936, offered![0].Volume.Id);
    }

    private sealed class SpecialSource : IScrapeComicVine
    {
        public List<string> Queries { get; } = new();

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            IReadOnlyList<ComicVineVolumeSearchResult> found = page > 1 ? Array.Empty<ComicVineVolumeSearchResult>()
                : query.Contains("Halloween") ? new[] { new ComicVineVolumeSearchResult(16936, "Amazing Spider-Man: Halloween Special", "2026", "Marvel", 1, null) }
                : new[] { new ComicVineVolumeSearchResult(9000, "The Amazing Spider-Man", "2025", "Marvel", 20, null) };
            return Task.FromResult(found);
        }

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineVolumeDetails?>(null);

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(Array.Empty<ComicVineIssueSummary>());

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineIssueDetails?>(null);
    }

    [Fact]
    public void Name_variants_go_from_the_full_name_to_its_parts()
    {
        Assert.Equal(new[] { "The Punisher Showdowns: Black Widow", "Punisher Showdowns: Black Widow", "Punisher Showdowns", "Black Widow" },
            ScrapeComicVineAdapter.NameVariants("The Punisher Showdowns: Black Widow"));
        Assert.Equal(new[] { "X-Men" }, ScrapeComicVineAdapter.NameVariants("X-Men"));
    }

    private sealed class ComicVineKindClient : IComicProvider
    {
        public int VolumeSearches, FuzzySearches;
        public bool VolumesEmpty;

        public ComicProvider Kind => ComicProvider.ComicVine;

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            VolumeSearches++;
            return Task.FromResult<IReadOnlyList<ComicVineVolume>>(VolumesEmpty ? Array.Empty<ComicVineVolume>() : new[] { new ComicVineVolume(2133, "The X-Men", "Marvel", 1963, 141, "cover.jpg") });
        }

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken) => SearchVolumesAsync(query, 25, cancellationToken);

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesFuzzyAsync(string query, CancellationToken cancellationToken)
        {
            FuzzySearches++;
            return Task.FromResult<IReadOnlyList<ComicVineVolume>>(new[] { new ComicVineVolume(2133, "The X-Men", null, 1963, 0, "blank.png") });
        }

        public Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken) => Task.FromResult<ComicVineVolume?>(null);

        public Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ComicVineIssue>>(Array.Empty<ComicVineIssue>());

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken) => Task.FromResult<ComicVineIssueDetails?>(null);
    }

    // ---- Settings ----

    [Fact]
    public void A_row_saved_before_Metron_became_the_default_switches_once()
    {
        using var context = CreateDbContext();
        context.ScrapeSettingsRows.Add(new ScrapeSettingsRow { Id = 1, Json = """{"DefaultProvider":0,"AutoChooseTopMatch":true}""" });
        context.SaveChanges();

        var loaded = ScrapeSettings.Load(context);
        Assert.Equal(ComicProvider.Metron, loaded.DefaultProvider);
        Assert.True(loaded.AutoChooseTopMatch);

        loaded.DefaultProvider = ComicProvider.ComicVine; // the user switches back
        loaded.Save(context);
        Assert.Equal(ComicProvider.ComicVine, ScrapeSettings.Load(context).DefaultProvider);
    }

    [Theory]
    [InlineData(ScrapeStrictness.Lenient, 0.82, 0.82)]
    [InlineData(ScrapeStrictness.Balanced, 0.87, 0.77)]
    [InlineData(ScrapeStrictness.Strict, 0.92, 0.72)]
    public void Strictness_moves_the_cover_thresholds(ScrapeStrictness strictness, double match, double runnerUp)
    {
        Assert.Equal(match, ConfidencePolicy.CoverMatch(strictness));
        Assert.Equal(runnerUp, ConfidencePolicy.RunnerUpLimit(strictness));
    }

    // ---- Orchestrator ----

    [Fact]
    public async Task A_series_is_asked_about_once_and_its_other_issues_follow_that_match()
    {
        var series = new Series { Name = "Batman Beyond" };
        int[] ids;
        using (var context = CreateDbContext())
        {
            var books = Enumerable.Range(1, 3).Select(n => new Issue { Series = series, Number = n.ToString(), FilePath = $"C:/x/{n}.cbz" }).ToList();
            context.Issues.AddRange(books);
            context.SaveChanges();
            ids = books.Select(b => b.Id).ToArray();
        }

        List<Issue> loaded;
        using (var context = CreateDbContext())
        {
            loaded = context.Issues.Include(i => i.Series).Where(i => ids.Contains(i.Id)).OrderBy(i => i.Id).ToList();
        }

        var source = new FakeSource();
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false };
        var orchestrator = new ScrapeOrchestrator(source, new ComicVineMatchMemory(CreateDbContext), settings);
        int dialogs = 0;

        var result = await orchestrator.ScrapeAsync(loaded, isInteractive: true,
            (label, query, candidates, search, ct) => { dialogs++; return Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume); },
            CreateDbContext);

        Assert.Equal(1, dialogs);
        Assert.Equal(1, source.Searches);
        Assert.Equal(3, result.Applied);
    }

    private List<Issue> SeedSeries(params string[] numbers)
    {
        var series = new Series { Name = "Batman Beyond" };
        int[] ids;
        using (var context = CreateDbContext())
        {
            var books = numbers.Select(n => new Issue { Series = series, Number = n, FilePath = $"C:/x/{n}.cbz" }).ToList();
            context.Issues.AddRange(books);
            context.SaveChanges();
            ids = books.Select(b => b.Id).ToArray();
        }

        using (var context = CreateDbContext())
        {
            return context.Issues.Include(i => i.Series).Where(i => ids.Contains(i.Id)).OrderBy(i => i.Id).ToList();
        }
    }

    [Fact]
    public async Task With_the_default_issue_confirmation_on_only_the_first_comic_of_a_series_asks()
    {
        // Reported 2026-10-21: "once 1 comic in the same series is matched then the rest should follow". ConfirmIssueMatch is on by default.
        var books = SeedSeries("1", "2", "3");
        var orchestrator = new ScrapeOrchestrator(new FakeSource(), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = true });
        int seriesDialogs = 0, issueDialogs = 0;

        var result = await orchestrator.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => { seriesDialogs++; return Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume); },
            CreateDbContext,
            interactiveIssueReview: (label, volume, issues, autoMatched, ct) => { issueDialogs++; return Task.FromResult(ComicVineIssueReviewResult.Confirmed(autoMatched!)); });

        Assert.Equal((1, 1), (seriesDialogs, issueDialogs));
        Assert.Equal(3, result.Applied);
    }

    [Fact]
    public async Task A_comic_whose_number_the_volume_lacks_stops_following_and_is_asked_about()
    {
        var books = SeedSeries("1", "2", "Annual 1");
        var orchestrator = new ScrapeOrchestrator(new FakeSource(), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false });
        var asked = new List<string>();

        await orchestrator.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => { asked.Add(label); return Task.FromResult<ComicVineVolumeSearchResult?>(null); },
            CreateDbContext);

        // Book 1 is skipped in its dialog, so nothing is chosen and every comic is asked about; then the same run with a choice:
        Assert.Equal(3, asked.Count);

        asked.Clear();
        var second = new ScrapeOrchestrator(new FakeSource(), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false });
        await second.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => { asked.Add(label); return Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume); },
            CreateDbContext);

        Assert.Equal(2, asked.Count); // #1 (first of the series) and the annual (no "Annual 1" in the volume); #2 followed
    }

    [Fact]
    public async Task An_annual_filed_under_the_regular_series_does_not_follow_its_match()
    {
        var series = new Series { Name = "Lobo" };
        int[] ids;
        using (var context = CreateDbContext())
        {
            var books = new[]
            {
                new Issue { Series = series, Number = "1", FilePath = "C:/x/Lobo 001.cbz" },
                new Issue { Series = series, Number = "1", FilePath = "C:/x/Lobo Annual 001.cbz" },
            };
            context.Issues.AddRange(books);
            context.SaveChanges();
            ids = books.Select(b => b.Id).ToArray();
        }

        List<Issue> loaded;
        using (var context = CreateDbContext())
        {
            loaded = context.Issues.Include(i => i.Series).Where(i => ids.Contains(i.Id)).OrderBy(i => i.Id).ToList();
        }

        var orchestrator = new ScrapeOrchestrator(new FakeSource(), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false });
        var asked = new List<string>();
        await orchestrator.ScrapeAsync(loaded, isInteractive: true,
            (label, query, candidates, search, ct) => { asked.Add(label); return Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume); },
            CreateDbContext);

        Assert.Equal(2, asked.Count); // the annual got its own question
    }

    [Fact]
    public async Task A_dry_run_writes_nothing_and_its_proposals_apply_without_asking_again()
    {
        var books = SeedSeries("1", "2");
        var orchestrator = new ScrapeOrchestrator(new DetailedSource(), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false })
        {
            DryRun = true,
        };
        int dialogs = 0;

        await orchestrator.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => { dialogs++; return Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume); },
            CreateDbContext);

        using (var context = CreateDbContext())
        {
            Assert.All(context.Issues.ToList(), i => Assert.Null(i.Title)); // nothing written yet
        }

        Assert.Equal(2, orchestrator.Proposals.Count);
        var first = orchestrator.Proposals[0];
        Assert.Contains(first.Changes, c => c.Field == "Title" && c.After == "Issue 1001");
        Assert.Contains(first.Changes, c => c.Field == "Publisher" && c.After == "DC Comics");

        var applied = await orchestrator.ApplyProposalsAsync(new[] { first }, CreateDbContext);

        Assert.Equal(new[] { first.IssueId }, applied);
        Assert.Equal(1, dialogs);
        using (var context = CreateDbContext())
        {
            Assert.Equal("Issue 1001", context.Issues.Single(i => i.Id == first.IssueId).Title);
            Assert.Null(context.Issues.Single(i => i.Id == orchestrator.Proposals[1].IssueId).Title); // not kept, not written
        }
    }

    [Fact]
    public async Task Blanks_Metron_left_are_filled_from_ComicVine_and_nothing_is_replaced()
    {
        var books = SeedSeries("1");
        var settings = new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false, FillBlanksFromComicVine = true };
        var none = new ComicVineDatePart(null, null, null);
        int asked = 0;
        var orchestrator = new ScrapeOrchestrator(new DetailedSource(), new ComicVineMatchMemory(CreateDbContext), settings, ComicProvider.Metron)
        {
            SecondaryDetails = (primary, metron, ct) =>
            {
                int cvId = metron.ComicVineId!.Value;
                asked = cvId;
                return Task.FromResult<ComicVineIssueDetails?>(new ComicVineIssueDetails(cvId, 9, "Batman Beyond", "1", "A ComicVine title", null, none, none, "ComicVine summary",
                    Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(),
                    new[] { new ComicVineIdName(5, "Neo-Gotham") }, Array.Empty<ComicVineCredit>()));
            },
        };

        await orchestrator.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext);

        Assert.Equal(55001, asked);
        using var context = CreateDbContext();
        var issue = context.Issues.Single();
        Assert.Equal("Issue 1001", issue.Title);           // Metron's value kept
        Assert.Equal("ComicVine summary", issue.Summary);  // Metron had none
        Assert.Equal("Neo-Gotham", issue.Locations);       // Metron has no locations at all
    }

    [Fact]
    public async Task A_field_set_to_ComicVine_takes_ComicVine_value_and_the_rest_stay_Metron()
    {
        var books = SeedSeries("1");
        var settings = new ScrapeSettings
        {
            AutoChooseTopMatch = false,
            ConfirmIssueMatch = false,
            FieldSourcePreference = new() { [ScrapeField.Title] = ComicProvider.ComicVine, [ScrapeField.Summary] = ComicProvider.ComicVine },
        };
        var none = new ComicVineDatePart(null, null, null);
        var orchestrator = new ScrapeOrchestrator(new DetailedSource(), new ComicVineMatchMemory(CreateDbContext), settings, ComicProvider.Metron)
        {
            SecondaryDetails = (primary, metron, ct) => Task.FromResult<ComicVineIssueDetails?>(new ComicVineIssueDetails(metron.ComicVineId!.Value, 9, "Batman Beyond", "1",
                "ComicVine's title", null, none, none, null,   // ComicVine has no summary: Metron's empty one isn't "won" by a blank
                Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), new[] { new ComicVineIdName(5, "Neo-Gotham") }, Array.Empty<ComicVineCredit>())),
        };

        await orchestrator.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext);

        using var context = CreateDbContext();
        var issue = context.Issues.Single();
        Assert.Equal("ComicVine's title", issue.Title);
        Assert.Null(issue.Locations); // not preferred and "fill blanks" is off: ComicVine's locations are not taken
    }

    [Theory]
    [InlineData(0.85, null, true, false, false)]   // between Lenient's 0.82 and Balanced's 0.87
    [InlineData(0.90, null, true, true, false)]
    [InlineData(0.95, 0.80, true, false, false)]   // a runner-up at 0.80 is too close for Balanced (0.77) and Strict (0.72), not for Lenient (0.82)
    [InlineData(0.95, 0.75, true, true, false)]
    [InlineData(0.95, 0.70, true, true, true)]
    public void Each_strictness_level_judges_the_same_measurements(double similarity, double? runnerUp, bool lenient, bool balanced, bool strict)
    {
        var check = new CoverCheck(similarity, runnerUp, null);
        Assert.Equal(lenient, ConfidencePolicy.Passes(ScrapeStrictness.Lenient, check));
        Assert.Equal(balanced, ConfidencePolicy.Passes(ScrapeStrictness.Balanced, check));
        Assert.Equal(strict, ConfidencePolicy.Passes(ScrapeStrictness.Strict, check));
    }

    [Fact]
    public void The_history_keeps_the_latest_checks_and_counts_what_each_level_would_apply()
    {
        var history = new ScrapeDecisionHistory(Path.Combine(_root, "decisions.json"));
        for (int i = 0; i < ScrapeDecisionHistory.Capacity + 10; i++)
        {
            history.Record(new CoverCheck(0.85, null, null));
        }

        history.Record(new CoverCheck(0.95, null, null));
        history.Record(new CoverCheck(null, null, true));
        history.Flush();

        var reloaded = new ScrapeDecisionHistory(Path.Combine(_root, "decisions.json")).Checks();
        Assert.Equal(ScrapeDecisionHistory.Capacity, reloaded.Count);
        var would = ScrapeDecisionHistory.WouldApply(reloaded);
        Assert.Equal(ScrapeDecisionHistory.Capacity, would[ScrapeStrictness.Lenient]);
        Assert.Equal(2, would[ScrapeStrictness.Balanced]);
        Assert.Equal(2, would[ScrapeStrictness.Strict]);
    }

    private sealed class DetailedSource : IScrapeComicVine
    {
        private readonly FakeSource _inner = new();

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default) =>
            _inner.SearchVolumesAsync(query, page, cancellationToken);

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineVolumeDetails?>(null);

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            _inner.SearchIssuesAsync(volumeId, page, cancellationToken);

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default)
        {
            var none = new ComicVineDatePart(null, null, null);
            return Task.FromResult<ComicVineIssueDetails?>(new ComicVineIssueDetails(issueId, 77, "Batman Beyond", (issueId - 1000).ToString(), $"Issue {issueId}", null, none, none, null,
                Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineCredit>())
            {
                ComicVineId = 55000 + issueId - 1000,
            });
        }
    }

    [Fact]
    public async Task A_refused_token_ends_the_batch_instead_of_writing_only_the_series_fields()
    {
        // Reported 2026-10-21: a Metron token sent with the wrong scheme was refused on every issue lookup, which was read as "issue not found",
        // so each comic got only Publisher/Imprint/Volume/Count/series.
        var books = SeedSeries("1", "2");
        var orchestrator = new ScrapeOrchestrator(new RefusingSource(), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false });

        var ex = await Assert.ThrowsAsync<ComicVineException>(() => orchestrator.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume),
            CreateDbContext));

        Assert.True(ScrapeOrchestrator.IsCredentialFailure(ex));
        using var context = CreateDbContext();
        Assert.All(context.Issues.ToList(), i => Assert.Null(i.Publisher));
    }

    private sealed class RefusingSource : IScrapeComicVine
    {
        private readonly FakeSource _inner = new();

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default) =>
            _inner.SearchVolumesAsync(query, page, cancellationToken);

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineVolumeDetails?>(null);

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            throw new ComicVineException("Metron rejected your API token.", 100) { HttpStatus = 401 };

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default) =>
            throw new ComicVineException("Metron rejected your API token.", 100) { HttpStatus = 401 };
    }

    [Fact]
    public async Task Balanced_speed_never_pauses_between_comics_but_Careful_does()
    {
        ScrapeOrchestrator.ScrapeDelayOverride = null;
        ScrapeOrchestrator.DelayInvocationCount = 0;
        var books = new[] { new Issue { Id = 1, Number = "1" }, new Issue { Id = 2, Number = "2" } };

        await new ScrapeOrchestrator(new FakeSource(), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { Speed = ScrapeSpeed.Balanced })
            .ScrapeAsync(books, isInteractive: false, interactiveReview: null, CreateDbContext);
        Assert.Equal(0, ScrapeOrchestrator.DelayInvocationCount);

        ScrapeOrchestrator.ScrapeDelayOverride = TimeSpan.Zero; // keep the Careful run instant; the count still says it paused
        await new ScrapeOrchestrator(new FakeSource(), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { Speed = ScrapeSpeed.Careful })
            .ScrapeAsync(books, isInteractive: false, interactiveReview: null, CreateDbContext);
        Assert.Equal(1, ScrapeOrchestrator.DelayInvocationCount);
    }

    // ---- Job store and review queue ----

    [Fact]
    public void An_unfinished_job_is_offered_again_and_a_finished_one_is_gone()
    {
        var store = new ScrapeJobStore(Path.Combine(_root, "jobs"));
        var job = new ScrapeJobRecord { Kind = "UnmatchedAll", Total = 30, Pending = Enumerable.Range(10, 20).ToList(), Done = 10 };
        store.Save(job);

        var unfinished = Assert.Single(store.LoadUnfinished());
        Assert.Equal(20, unfinished.Pending.Count);

        store.Delete(job.Id);
        Assert.Empty(store.LoadUnfinished());
    }

    [Fact]
    public void The_review_queue_keeps_one_entry_per_comic()
    {
        var queue = new ScrapeReviewQueue(Path.Combine(_root, "review.json"));
        queue.Add(new[] { new ScrapeReviewItem(1, "Batman #1", "Couldn't confirm the cover match", DateTime.UtcNow), new ScrapeReviewItem(2, "Batman #2", null, DateTime.UtcNow) });
        queue.Add(new[] { new ScrapeReviewItem(1, "Batman #1", "No search results", DateTime.UtcNow) });

        Assert.Equal(2, queue.Load().Count);
        Assert.Equal("No search results", queue.Load().Single(i => i.IssueId == 1).Reason);

        queue.Remove(new[] { 1 });
        Assert.Equal(2, Assert.Single(queue.Load()).IssueId);
    }

    private sealed class FakeSource : IScrapeComicVine
    {
        public int Searches;

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default)
        {
            Searches++;
            return Task.FromResult<IReadOnlyList<ComicVineVolumeSearchResult>>(page == 1
                ? new[] { new ComicVineVolumeSearchResult(77, "Batman Beyond", "2023", "DC Comics", 3, null) }
                : Array.Empty<ComicVineVolumeSearchResult>());
        }

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineVolumeDetails?>(null);

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(page == 1
                ? new[] { new ComicVineIssueSummary(1001, "1", null, null), new ComicVineIssueSummary(1002, "2", null, null), new ComicVineIssueSummary(1003, "3", null, null) }
                : Array.Empty<ComicVineIssueSummary>());

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineIssueDetails?>(null);
    }

    private sealed class CountingClient : IComicVineClient, IComicVineIssueDetailsSource
    {
        public int Searches, IssueLists, Details;
        public bool RateLimited;

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesAsync(string query, CancellationToken cancellationToken) => SearchVolumesFuzzyAsync(query, cancellationToken);

        public Task<IReadOnlyList<ComicVineVolume>> SearchVolumesFuzzyAsync(string query, CancellationToken cancellationToken)
        {
            Searches++;
            return Task.FromResult<IReadOnlyList<ComicVineVolume>>(new[] { new ComicVineVolume(1, "Batman", "DC Comics", 1990, 50, null) });
        }

        public Task<ComicVineVolume?> GetVolumeAsync(int volumeId, CancellationToken cancellationToken) => Task.FromResult<ComicVineVolume?>(null);

        public Task<IReadOnlyList<ComicVineIssue>> GetVolumeIssuesAsync(int volumeId, CancellationToken cancellationToken)
        {
            if (RateLimited)
            {
                throw new ComicVineException("slow down", 107);
            }

            IssueLists++;
            return Task.FromResult<IReadOnlyList<ComicVineIssue>>(new[] { new ComicVineIssue(5, "1", null, null, null, null, volumeId) });
        }

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken)
        {
            Details++;
            var none = new ComicVineDatePart(null, null, null);
            return Task.FromResult<ComicVineIssueDetails?>(new ComicVineIssueDetails(issueId, 1, "Batman", "1", "A title", null, none, none, null,
                Array.Empty<ComicVineIdName>(), new[] { new ComicVineIdName(3, "Robin") }, Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineCredit>()));
        }
    }
}
