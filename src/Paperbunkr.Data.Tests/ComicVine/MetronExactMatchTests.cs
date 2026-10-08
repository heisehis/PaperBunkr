using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>
/// A Metron scrape's exact-id step and its use of Metron's published cover hash (docs/superpowers/specs/
/// 2026-10-05-metron-api-efficiency-and-matching-design.md sections 2 and 3), through the real
/// <see cref="ScrapeOrchestrator"/> with a scripted source.
/// </summary>
public sealed class MetronExactMatchTests : IDisposable
{
    private readonly string _testRoot = Directory.CreateTempSubdirectory("pb-metron-exact-").FullName;
    private readonly string _dbPath;
    private readonly string _coverPath;

    public MetronExactMatchTests()
    {
        _dbPath = Path.Combine(_testRoot, "test.db");
        _coverPath = Path.Combine(_testRoot, "cover.png");
        File.WriteAllBytes(_coverPath, Convert.FromBase64String(MetronCoverHashTests.FixtureA));
        ScrapeOrchestrator.ScrapeDelayOverride = TimeSpan.Zero;
    }

    public void Dispose()
    {
        ScrapeOrchestrator.ScrapeDelayOverride = null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_testRoot, recursive: true); } catch (IOException) { }
    }

    private PaperbunkrDbContext CreateDbContext()
    {
        var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class Source : IScrapeComicVine, IComicIssueLookup
    {
        public List<ComicVineVolumeSearchResult> Volumes { get; } = new() { new(900, "Batman", "1990", "Metron House", 50, null) };
        public Dictionary<int, List<ComicVineIssueSummary>> Issues { get; } = new();
        public Dictionary<int, int> SeriesOfIssue { get; } = new() { [7003] = 900 };
        public Dictionary<int, List<ComicIssueHit>> ByComicVineId { get; } = new();
        public Dictionary<string, List<ComicIssueHit>> ByUpc { get; } = new();
        public Exception? LookupThrows { get; set; }
        public int Searches { get; private set; }
        public int Lookups { get; private set; }
        public List<int> DetailCalls { get; } = new();

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default)
        {
            Searches++;
            return Task.FromResult<IReadOnlyList<ComicVineVolumeSearchResult>>(page > 1 ? Array.Empty<ComicVineVolumeSearchResult>() : Volumes);
        }

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Volumes.Where(v => v.Id == volumeId).Select(v => new ComicVineVolumeDetails(v.Id, v.Name, v.StartYear, v.Publisher, v.CountOfIssues, v.ImageUrl)).FirstOrDefault());

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(page == 1 && Issues.TryGetValue(volumeId, out var list) ? list : Array.Empty<ComicVineIssueSummary>());

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default)
        {
            DetailCalls.Add(issueId);
            var none = new ComicVineDatePart(null, null, null);
            return Task.FromResult<ComicVineIssueDetails?>(SeriesOfIssue.TryGetValue(issueId, out int seriesId)
                ? new ComicVineIssueDetails(issueId, seriesId, "Batman", "3", $"Story {issueId}", null, none, none, null,
                    Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineCredit>())
                : null);
        }

        public Task<IReadOnlyList<ComicIssueHit>> FindIssuesByComicVineIdAsync(int comicVineIssueId, CancellationToken cancellationToken)
        {
            Lookups++;
            if (LookupThrows is not null) throw LookupThrows;
            return Task.FromResult<IReadOnlyList<ComicIssueHit>>(ByComicVineId.TryGetValue(comicVineIssueId, out var hits) ? hits : new List<ComicIssueHit>());
        }

        public Task<IReadOnlyList<ComicIssueHit>> FindIssuesByUpcAsync(string upc, CancellationToken cancellationToken)
        {
            Lookups++;
            if (LookupThrows is not null) throw LookupThrows;
            return Task.FromResult<IReadOnlyList<ComicIssueHit>>(ByUpc.TryGetValue(upc, out var hits) ? hits : new List<ComicIssueHit>());
        }
    }

    private Issue Seed(Action<Issue>? configure = null, params (ComicProvider Provider, string Id)[] links)
    {
        using var context = CreateDbContext();
        var series = new Series { Id = 1, Name = "Batman" };
        context.Series.Add(series);
        var issue = new Issue { Id = 1, SeriesId = 1, Number = "3", FilePath = "book.cbz" };
        configure?.Invoke(issue);
        context.Issues.Add(issue);
        foreach (var (provider, id) in links)
        {
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Issue, EntityId = 1, Provider = provider, ExternalId = id });
        }

        context.SaveChanges();
        issue.Series = series;
        return issue;
    }

    private ScrapeOrchestrator Orchestrator(Source source, bool autoChoose = true, bool withCover = false, ComicProvider provider = ComicProvider.Metron) =>
        new(source, new ComicVineMatchMemory(CreateDbContext, provider), new ScrapeSettings { AutoChooseTopMatch = autoChoose }, provider,
            getCoverPath: withCover ? _ => _coverPath : null);

    private Task<ScrapeBatchResult> Run(ScrapeOrchestrator orchestrator, Issue issue, bool interactive = false) =>
        orchestrator.ScrapeAsync(new[] { issue }, interactive,
            interactive ? (_, _, ranked, _, _) => Task.FromResult<ComicVineVolumeSearchResult?>(ranked.Count > 0 ? ranked[0].Volume : null) : null,
            CreateDbContext);

    private Issue Saved()
    {
        using var context = CreateDbContext();
        return context.Issues.Single();
    }

    [Fact]
    public async Task ABookWithAMetronId_IsFetchedByIt_WithNoSearchAndNoLookup()
    {
        var issue = Seed(null, (ComicProvider.Metron, "7003"));
        var source = new Source();

        var result = await Run(Orchestrator(source), issue);

        Assert.Equal(1, result.Applied);
        Assert.Contains("its Metron id", result.Outcomes.Single().Reason);
        Assert.Equal(0, source.Searches);
        Assert.Equal(0, source.Lookups);
        Assert.Equal("Story 7003", Saved().Title);
        Assert.Equal(ComicProvider.Metron, Saved().MetadataSource);
    }

    [Fact]
    public async Task AComicVineId_IsLookedUp_AndUsedWhenItNamesExactlyOneIssue()
    {
        var issue = Seed(null, (ComicProvider.ComicVine, "555"));
        var source = new Source();
        source.ByComicVineId[555] = new() { new ComicIssueHit(7003, 900) };

        var result = await Run(Orchestrator(source), issue);

        Assert.Equal(1, result.Applied);
        Assert.Contains("its ComicVine id", result.Outcomes.Single().Reason);
        Assert.Equal(0, source.Searches);
        Assert.Equal("Story 7003", Saved().Title);

        // The Metron id is kept, so the next scrape needs no lookup at all.
        using var context = CreateDbContext();
        Assert.Equal("7003", context.ComicMetadataExternalIds.Single(e => e.EntityKind == ComicMetadataEntityKind.Issue && e.Provider == ComicProvider.Metron).ExternalId);
    }

    [Fact]
    public async Task ABarcode_IsTheLastResort_AfterAComicVineIdThatFoundNothing()
    {
        var issue = Seed(i => i.Upc = "76194130593600311", (ComicProvider.ComicVine, "555"));
        var source = new Source();
        source.ByUpc["76194130593600311"] = new() { new ComicIssueHit(7003, 900) };

        var result = await Run(Orchestrator(source), issue);

        Assert.Contains("its barcode", result.Outcomes.Single().Reason);
        Assert.Equal(2, source.Lookups);
        Assert.Equal(0, source.Searches);
    }

    [Theory]
    [InlineData("12345")]            // too short to be a barcode
    [InlineData("7619413059360O311")] // not all digits
    public async Task SomethingThatIsNotABarcode_IsNeverSent(string upc)
    {
        var issue = Seed(i => i.Upc = upc);
        var source = new Source();

        await Run(Orchestrator(source), issue);

        Assert.Equal(0, source.Lookups);
        Assert.Equal(1, source.Searches);
    }

    [Fact]
    public async Task MoreThanOneHit_IsAmbiguous_SoTheNameSearchRuns()
    {
        var issue = Seed(null, (ComicProvider.ComicVine, "555"));
        var source = new Source();
        source.ByComicVineId[555] = new() { new ComicIssueHit(7003, 900), new ComicIssueHit(7004, 900) };

        var result = await Run(Orchestrator(source), issue);

        Assert.Equal(1, source.Searches);
        Assert.Equal(1, result.Applied);
        Assert.DoesNotContain("its ComicVine id", result.Outcomes.Single().Reason);
    }

    [Fact]
    public async Task AFailedLookup_FallsThroughToTheNameSearch()
    {
        var issue = Seed(null, (ComicProvider.ComicVine, "555"));
        var source = new Source { LookupThrows = new ComicVineException("Metron's rate limit was reached; try again in a minute.", 107) };

        var result = await Run(Orchestrator(source), issue);

        Assert.Equal(1, source.Searches);
        Assert.Equal(1, result.Applied);
    }

    [Fact]
    public async Task ARunWhereTheUserConfirmsMatches_StillSearchesAndAsks()
    {
        var issue = Seed(null, (ComicProvider.Metron, "7003"));
        var source = new Source();

        await Run(Orchestrator(source, autoChoose: false), issue, interactive: true);

        Assert.Equal(1, source.Searches);
        Assert.DoesNotContain(7003, source.DetailCalls);
    }

    [Fact]
    public async Task AComicVineScrape_NeverUsesTheLookups()
    {
        var issue = Seed(i => i.Upc = "76194130593600311", (ComicProvider.Metron, "7003"));
        var source = new Source();

        await Run(Orchestrator(source, provider: ComicProvider.ComicVine), issue);

        Assert.Equal(0, source.Lookups);
        Assert.Equal(1, source.Searches);
    }

    [Fact]
    public async Task ThePublishedCoverHash_ConfirmsTheMatch_WithoutDownloadingAnything()
    {
        var issue = Seed();
        var source = new Source();
        source.Issues[900] = new() { new ComicVineIssueSummary(7003, "3", null, "https://unreachable.invalid/cover.jpg", MetronCoverHash.Format(MetronCoverHashTests.HashA)) };

        var result = await Run(Orchestrator(source, withCover: true), issue);

        Assert.Equal(1, result.Applied);
        Assert.Equal("Story 7003", Saved().Title);
    }

    [Fact]
    public async Task ACoverThatIsNotTheBooks_FailsTheCheck()
    {
        var issue = Seed();
        var source = new Source();
        source.Issues[900] = new() { new ComicVineIssueSummary(7003, "3", null, null, MetronCoverHash.Format(MetronCoverHashTests.HashB)) };

        var result = await Run(Orchestrator(source, withCover: true), issue);

        Assert.Equal(0, result.Applied);
        Assert.Null(Saved().Title);
    }

    [Fact]
    public async Task WhenTheTopCandidateFails_TheRunnerUpWithTheBooksCover_IsTaken()
    {
        var issue = Seed();
        var source = new Source();
        source.Volumes.Add(new(901, "Batman Adventures", "1992", "Runner Up House", 36, null));
        source.SeriesOfIssue[8003] = 901;
        source.Issues[900] = new() { new ComicVineIssueSummary(7003, "3", null, null, MetronCoverHash.Format(MetronCoverHashTests.HashB)) };
        source.Issues[901] = new() { new ComicVineIssueSummary(8003, "3", null, null, MetronCoverHash.Format(MetronCoverHashTests.HashA)) };

        var result = await Run(Orchestrator(source, withCover: true), issue);

        Assert.Equal(1, result.Applied);
        Assert.Equal("Story 8003", Saved().Title);
        Assert.Equal("Runner Up House", Saved().Publisher);
    }

    [Fact]
    public async Task TwoRunnerUpsWithTheSameCover_AreNotGuessedBetween()
    {
        var issue = Seed();
        var source = new Source();
        source.Volumes.Add(new(901, "Batman Adventures", "1992", "Runner Up House", 36, null));
        source.Volumes.Add(new(902, "Batman Chronicles", "1995", "Third House", 23, null));
        string same = MetronCoverHash.Format(MetronCoverHashTests.HashA);
        source.Issues[900] = new() { new ComicVineIssueSummary(7003, "3", null, null, MetronCoverHash.Format(MetronCoverHashTests.HashB)) };
        source.Issues[901] = new() { new ComicVineIssueSummary(8003, "3", null, null, same) };
        source.Issues[902] = new() { new ComicVineIssueSummary(9003, "3", null, null, same) };

        var result = await Run(Orchestrator(source, withCover: true), issue);

        Assert.Equal(0, result.Applied);
    }
}
