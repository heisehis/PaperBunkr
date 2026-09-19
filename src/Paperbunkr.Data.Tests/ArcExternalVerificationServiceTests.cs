using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="ArcExternalVerificationService"/> (docs/superpowers/specs/2026-09-17-
/// storyevent-continuity-autopopulate-design.md, Phase 1) against a real SQLite database and hand-
/// rolled <see cref="IReadingListSource"/> fakes - <see cref="ComicVineSource"/>/<see cref="MetronSource"/>
/// have no HTTP test seam of their own, so the fakes stand in via the internal (context, candidates,
/// comicVine, metron, ct) overload.
/// </summary>
public class ArcExternalVerificationServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ArcExternalVerificationServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_arcverify_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static StoryEventCandidate MakeCandidate(string arcName, string publisher, params (string Number, int Year)[] issues)
    {
        var members = issues.Select(i => new StoryEventCandidateMember(new Issue { Number = i.Number, Year = i.Year, StoryArc = arcName, Publisher = publisher }, (int?)null)).ToList();
        return new StoryEventCandidate(arcName, publisher, members, FormatSignalStrength.Weak, $"{members.Count} issues tagged with Story Arc \"{arcName}\"");
    }

    [Fact]
    public async Task NoSourcesConfigured_ReturnsCandidatesUnchanged()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var candidates = new[] { MakeCandidate("Civil War", "Marvel", ("1", 2006), ("2", 2006)) };

        var result = await ArcExternalVerificationService.VerifyAsync(context, candidates, comicVine: null, metron: null, CancellationToken.None);

        Assert.Equal(FormatSignalStrength.Weak, Assert.Single(result).Strength);
    }

    [Fact]
    public async Task ComicVineMatch_UpgradesToStrong_AndFillsPosition()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var candidates = new[] { MakeCandidate("Civil War", "Marvel", ("1", 2006), ("2", 2006)) };
        var comicVine = new FakeSource("ComicVine", "arc-1", new[] { new ArcIssue("Amazing Spider-Man", "2", 2006, null), new ArcIssue("Amazing Spider-Man", "1", 2006, null) });

        var result = await ArcExternalVerificationService.VerifyAsync(context, candidates, comicVine, metron: null, CancellationToken.None);

        var verified = Assert.Single(result);
        Assert.Equal(FormatSignalStrength.Strong, verified.Strength);
        Assert.Equal("arc-1", verified.ComicVineArcId);
        Assert.Equal(0, verified.Members.First(m => m.Issue.Number == "2").Position);
        Assert.Equal(1, verified.Members.First(m => m.Issue.Number == "1").Position);
    }

    [Fact]
    public async Task ComicVineThrows_FallsBackToMetron()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var candidates = new[] { MakeCandidate("Civil War", "Marvel", ("1", 2006), ("2", 2006)) };
        var comicVine = new FakeSource("ComicVine", throwOnSearch: true);
        var metron = new FakeSource("Metron", "arc-9", new[] { new ArcIssue("Amazing Spider-Man", "1", 2006, null), new ArcIssue("Amazing Spider-Man", "2", 2006, null) });

        var result = await ArcExternalVerificationService.VerifyAsync(context, candidates, comicVine, metron, CancellationToken.None);

        var verified = Assert.Single(result);
        Assert.Equal(FormatSignalStrength.Strong, verified.Strength);
        Assert.Equal("arc-9", verified.MetronArcId);
    }

    [Fact]
    public async Task NoNameMatch_RecordsNegativeCache_AndSkipsOnNextCall()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var candidates = new[] { MakeCandidate("Obscure Arc", "Marvel", ("1", 2006), ("2", 2006)) };
        var comicVine = new FakeSource("ComicVine", searchResults: Array.Empty<ArcSearchResult>());

        await ArcExternalVerificationService.VerifyAsync(context, candidates, comicVine, metron: null, CancellationToken.None);

        Assert.Single(context.StoryEventVerificationNegativeCaches);
        Assert.Equal(0, comicVine.OverviewCalls);

        // Second call should skip ComicVine entirely for this arc - only the first call's
        // SearchAsync should have run.
        await ArcExternalVerificationService.VerifyAsync(context, candidates, comicVine, metron: null, CancellationToken.None);
        Assert.Equal(1, comicVine.SearchCalls);
    }

    [Fact]
    public async Task MoreThanFifteenCandidates_OnlyVerifiesLargestFifteen()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var candidates = Enumerable.Range(0, 20)
            .Select(i => MakeCandidate($"Arc {i}", "Marvel", ("1", 2006), ("2", 2006)))
            .ToList();
        // Make the last 5 the biggest groups, so we can assert exactly those get verified.
        for (int i = 15; i < 20; i++)
        {
            candidates[i] = candidates[i] with { Members = candidates[i].Members.Append(new StoryEventCandidateMember(new Issue { Number = "3", Year = 2006 }, null)).ToList() };
        }

        var comicVine = new FakeSource("ComicVine", searchResults: Array.Empty<ArcSearchResult>());
        await ArcExternalVerificationService.VerifyAsync(context, candidates, comicVine, metron: null, CancellationToken.None);

        Assert.Equal(15, comicVine.SearchCalls);
    }

    private sealed class FakeSource : IReadingListSource
    {
        private readonly string? _matchId;
        private readonly IReadOnlyList<ArcIssue>? _orderedIssues;
        private readonly IReadOnlyList<ArcSearchResult>? _explicitSearchResults;
        private readonly bool _throwOnSearch;

        public int SearchCalls { get; private set; }
        public int OverviewCalls { get; private set; }

        public FakeSource(string sourceKey, string matchId, IReadOnlyList<ArcIssue> orderedIssues)
        {
            SourceKey = sourceKey;
            _matchId = matchId;
            _orderedIssues = orderedIssues;
        }

        public FakeSource(string sourceKey, IReadOnlyList<ArcSearchResult> searchResults)
        {
            SourceKey = sourceKey;
            _explicitSearchResults = searchResults;
        }

        public FakeSource(string sourceKey, bool throwOnSearch)
        {
            SourceKey = sourceKey;
            _throwOnSearch = throwOnSearch;
        }

        public string SourceKey { get; }
        public string DisplayName => SourceKey;
        public bool RequiresCredentials => true;
        public bool HasBrowsableCatalog => false;

        public Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
        {
            SearchCalls++;
            if (_throwOnSearch)
            {
                throw new ReadingListSourceException(DisplayName, "simulated failure");
            }

            if (_explicitSearchResults is not null)
            {
                return Task.FromResult(_explicitSearchResults);
            }

            IReadOnlyList<ArcSearchResult> results = new[] { new ArcSearchResult(_matchId!, query, null, null, _orderedIssues!.Count) };
            return Task.FromResult(results);
        }

        public Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken) =>
            Task.FromResult(_orderedIssues ?? Array.Empty<ArcIssue>());

        public Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken)
        {
            OverviewCalls++;
            return Task.FromResult<ArcOverviewInfo?>(null);
        }
    }
}
