using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>docs/superpowers/specs/2026-10-22-scrape-series-split-and-series-fields-design.md: a match to another source series splits, not renames.</summary>
[Collection("ScrapeOrchestratorStatics")]
public sealed class SeriesSplitTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("pb-split-").FullName;

    public SeriesSplitTests() => ScrapeOrchestrator.ScrapeDelayOverride = TimeSpan.Zero;

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

    /// <summary>"Secret Wars" with #1 and #2, the series linked to Metron series 100.</summary>
    private (int SeriesId, int First, int Second) Seed(bool keepTogether = false)
    {
        using var context = CreateDbContext();
        var series = new Series { Name = "Secret Wars", ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight };
        if (keepTogether)
        {
            SeriesSplit.SetKeepTogether(series, keep: true);
        }

        var first = new Issue { Series = series, Number = "1", FilePath = "C:/x/sw1.cbz" };
        var second = new Issue { Series = series, Number = "1", FilePath = "C:/x/sw2099-1.cbz" };
        context.Issues.AddRange(first, second);
        context.SaveChanges();
        context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = series.Id, Provider = ComicProvider.Metron, ExternalId = "100" });
        context.SaveChanges();
        return (series.Id, first.Id, second.Id);
    }

    private async Task MatchAsync(int issueId, ComicVineVolumeSearchResult volume)
    {
        List<Issue> books;
        using (var context = CreateDbContext())
        {
            books = context.Issues.Include(i => i.Series).Where(i => i.Id == issueId).ToList();
        }

        var orchestrator = new ScrapeOrchestrator(new OneVolume(volume), new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false }, ComicProvider.Metron);
        await orchestrator.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => Task.FromResult<ComicVineVolumeSearchResult?>(volume), CreateDbContext);
    }

    private static readonly ComicVineVolumeSearchResult SecretWars2099 = new(200, "Secret Wars 2099", "2015", "Marvel", 5, null);

    [Fact]
    public async Task A_comic_matched_to_another_source_series_moves_to_a_new_series_and_the_original_keeps_its_name_and_link()
    {
        var (seriesId, first, second) = Seed();

        await MatchAsync(second, SecretWars2099);

        using var context = CreateDbContext();
        var original = context.Series.Single(s => s.Id == seriesId);
        var moved = context.Issues.Include(i => i.Series).Single(i => i.Id == second);
        Assert.Equal("Secret Wars", original.Name);
        Assert.Equal(seriesId, context.Issues.Single(i => i.Id == first).SeriesId);
        Assert.NotEqual(seriesId, moved.SeriesId);
        Assert.Equal("Secret Wars 2099", moved.Series!.Name);
        Assert.Equal(ReadingMode.LeftToRight, moved.Series.ReadingMode);
        Assert.Equal("100", SeriesSplit.LinkOf(context, seriesId, ComicProvider.Metron));
        Assert.Equal("200", SeriesSplit.LinkOf(context, moved.SeriesId, ComicProvider.Metron));
    }

    [Fact]
    public async Task It_joins_the_series_already_linked_to_that_source_series()
    {
        var (_, _, second) = Seed();
        int existing;
        using (var context = CreateDbContext())
        {
            var other = new Series { Name = "Secret Wars 2099 (my name)" };
            context.Series.Add(other);
            context.SaveChanges();
            context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Series, EntityId = other.Id, Provider = ComicProvider.Metron, ExternalId = "200" });
            context.SaveChanges();
            existing = other.Id;
        }

        await MatchAsync(second, SecretWars2099);

        using var check = CreateDbContext();
        Assert.Equal(existing, check.Issues.Single(i => i.Id == second).SeriesId);
    }

    [Fact]
    public async Task A_kept_together_series_is_never_split_renamed_or_relinked()
    {
        var (seriesId, _, second) = Seed(keepTogether: true);

        await MatchAsync(second, SecretWars2099);

        using var context = CreateDbContext();
        Assert.Equal(seriesId, context.Issues.Single(i => i.Id == second).SeriesId);
        Assert.Equal("Secret Wars", context.Series.Single(s => s.Id == seriesId).Name);
        Assert.Equal("100", SeriesSplit.LinkOf(context, seriesId, ComicProvider.Metron));
    }

    [Fact]
    public async Task An_agreeing_match_renames_and_the_old_name_can_be_restored()
    {
        var (seriesId, first, _) = Seed();

        await MatchAsync(first, new ComicVineVolumeSearchResult(100, "Secret Wars (2015)", "2015", "Marvel", 9, null));

        using var context = CreateDbContext();
        var series = context.Series.Include(s => s.Titles).Single(s => s.Id == seriesId);
        Assert.Equal("Secret Wars (2015)", series.Name);
        Assert.Equal("Secret Wars", SeriesSplit.NameBeforeScrape(series));
        Assert.Equal("Marvel", series.Publisher); // series-level field filled by the owning match
    }

    [Fact]
    public async Task An_unlinked_mixed_series_is_sorted_in_one_scrape()
    {
        // Reported 2026-10-22 ("it doesn't work all the time"): "Secret Wars" holding Secret Wars and Secret Wars 2099, never scraped before.
        int seriesId, plain, nine;
        using (var context = CreateDbContext())
        {
            var series = new Series { Name = "Secret Wars" };
            var a = new Issue { Series = series, Number = "1", FilePath = "C:/x/Secret Wars 001 (2015).cbz" };
            var b = new Issue { Series = series, Number = "1", FilePath = "C:/x/Secret_Wars_2099_001_(2015).cbz" };
            context.Issues.AddRange(a, b);
            context.SaveChanges();
            (seriesId, plain, nine) = (series.Id, a.Id, b.Id);
        }

        List<Issue> books;
        using (var context = CreateDbContext())
        {
            books = context.Issues.Include(i => i.Series).OrderByDescending(i => i.Id).ToList(); // the 2099 comic first: the old "first match owns it" trap
        }

        var source = new TwoSeries();
        var orchestrator = new ScrapeOrchestrator(source, new ComicVineMatchMemory(CreateDbContext), new ScrapeSettings { AutoChooseTopMatch = false, ConfirmIssueMatch = false }, ComicProvider.Metron);
        await orchestrator.ScrapeAsync(books, isInteractive: true,
            (label, query, candidates, search, ct) => Task.FromResult<ComicVineVolumeSearchResult?>(candidates[0].Volume), CreateDbContext);

        using var check = CreateDbContext();
        Assert.Contains("Secret Wars 2099", source.Queries);                             // the 2099 comic searched for itself
        Assert.Equal("Secret Wars", check.Series.Single(s => s.Id == seriesId).Name);   // never renamed to 2099
        Assert.Equal(seriesId, check.Issues.Single(i => i.Id == plain).SeriesId);
        var moved = check.Issues.Include(i => i.Series).Single(i => i.Id == nine);
        Assert.Equal("Secret Wars 2099", moved.Series!.Name);
        Assert.Equal("200", SeriesSplit.LinkOf(check, moved.SeriesId, ComicProvider.Metron));
        Assert.Equal("100", SeriesSplit.LinkOf(check, seriesId, ComicProvider.Metron));
    }

    private sealed class TwoSeries : IScrapeComicVine
    {
        public List<string> Queries { get; } = new();

        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var plain = new ComicVineVolumeSearchResult(100, "Secret Wars", "2015", "Marvel", 9, null);
            var nine = new ComicVineVolumeSearchResult(200, "Secret Wars 2099", "2015", "Marvel", 5, null);
            IReadOnlyList<ComicVineVolumeSearchResult> found = page > 1 ? Array.Empty<ComicVineVolumeSearchResult>()
                : query.Contains("2099") ? new[] { nine, plain } : new[] { plain, nine };
            return Task.FromResult(found);
        }

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineVolumeDetails?>(null);

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(page == 1 ? new[] { new ComicVineIssueSummary(volumeId * 10 + 1, "1", null, null) } : Array.Empty<ComicVineIssueSummary>());

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineIssueDetails?>(null);
    }

    private sealed class OneVolume(ComicVineVolumeSearchResult volume) : IScrapeComicVine
    {
        public Task<IReadOnlyList<ComicVineVolumeSearchResult>> SearchVolumesAsync(string query, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineVolumeSearchResult>>(page == 1 ? new[] { volume } : Array.Empty<ComicVineVolumeSearchResult>());

        public Task<ComicVineVolumeDetails?> GetVolumeDetailsAsync(int volumeId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineVolumeDetails?>(null);

        public Task<IReadOnlyList<ComicVineIssueSummary>> SearchIssuesAsync(int volumeId, int page = 1, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ComicVineIssueSummary>>(Array.Empty<ComicVineIssueSummary>());

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken = default) => Task.FromResult<ComicVineIssueDetails?>(null);
    }
}
