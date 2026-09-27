using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>Reading order within a story arc, ported from the user's fork of the ComicVine Scraper (book/arcorder.py, database/comicvine/cvdb.py).</summary>
public class StoryArcOrderResolverTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("arcorder-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly ComicVineIdName Arc = new(77, "Doom Arc");

    private static StoryArcIssue Issue(int id, string number, string? store, string? cover) => new(id, number, store, cover);

    private static StoryArcOrderResolver Resolver(IReadOnlyList<StoryArcIssue> issues, Action? onFetch = null, string? overrides = null) =>
        new((_, _) => { onFetch?.Invoke(); return Task.FromResult(issues); }, overrides);

    [Fact]
    public async Task SortsByStoreDate_ThenCoverDate_ThenNumber_AndCountsTheWholeArc()
    {
        var issues = new[]
        {
            Issue(3, "1", "2025-03-05", "2025-05-01"),
            Issue(1, "10", "2025-01-01", "2025-03-01"),
            Issue(2, "2", "2025-03-05", "2025-04-01"),   // same store date as #3: the earlier cover date wins
        };

        var positions = await Resolver(issues).ComputeAsync([Arc], issueId: 2, default);

        Assert.Equal(["2"], positions.Numbers);           // 1 is the January issue, 2 the earlier-cover March one
        Assert.Equal(3, positions.AlternateCount);
    }

    [Fact]
    public async Task UndatedIssuesSortLast_AndTieBreakOnNaturalIssueNumber()
    {
        var issues = new[]
        {
            Issue(9, "10", null, null),
            Issue(8, "2", null, null),
            Issue(7, "1", "2025-01-01", "2025-02-01"),
        };

        var resolver = Resolver(issues);

        Assert.Equal(["1"], (await resolver.ComputeAsync([Arc], 7, default)).Numbers);
        Assert.Equal(["2"], (await resolver.ComputeAsync([Arc], 8, default)).Numbers);   // 2 before 10, not string order
        Assert.Equal(["3"], (await resolver.ComputeAsync([Arc], 9, default)).Numbers);
    }

    [Fact]
    public async Task OneEntryPerArc_AndOnlyTheFirstArcsSizeBecomesTheAlternateCount()
    {
        var resolver = new StoryArcOrderResolver((arcId, _) => Task.FromResult<IReadOnlyList<StoryArcIssue>>(arcId == 1
            ? [Issue(5, "1", "2025-01-01", null), Issue(6, "2", "2025-02-01", null)]
            : [Issue(5, "1", "2025-01-01", null), Issue(6, "2", "2025-02-01", null), Issue(7, "3", "2025-03-01", null)]));

        var positions = await resolver.ComputeAsync([new ComicVineIdName(1, "A"), new ComicVineIdName(2, "B")], 6, default);

        Assert.Equal(["2", "2"], positions.Numbers);
        Assert.Equal(2, positions.AlternateCount);        // arc A's size, not B's
    }

    [Fact]
    public async Task AnIssueMissingFromItsOwnArc_OrAnArcWithNoId_YieldsAnEmptyEntry()
    {
        var positions = await Resolver([Issue(1, "1", "2025-01-01", null)])
            .ComputeAsync([Arc, new ComicVineIdName(null, "No id")], issueId: 99, default);

        Assert.Equal([string.Empty, string.Empty], positions.Numbers);
        Assert.Null(positions.AlternateCount);   // as in the fork: not found means (-1, -1), no count either
        Assert.True(positions.IsEmpty);
    }

    [Fact]
    public async Task EachArcIsFetchedOncePerResolver()
    {
        int fetches = 0;
        var resolver = Resolver([Issue(1, "1", "2025-01-01", null), Issue(2, "2", "2025-02-01", null)], () => fetches++);

        await resolver.ComputeAsync([Arc], 1, default);
        await resolver.ComputeAsync([Arc], 2, default);

        Assert.Equal(1, fetches);
    }

    [Fact]
    public async Task AFailedFetch_LeavesTheEntryEmpty_AndIsRetriedForTheNextBook()
    {
        int calls = 0;
        var resolver = new StoryArcOrderResolver((_, _) =>
            ++calls == 1 ? throw new ComicVineException("boom") : Task.FromResult<IReadOnlyList<StoryArcIssue>>([Issue(1, "1", "2025-01-01", null)]));

        Assert.Equal([string.Empty], (await resolver.ComputeAsync([Arc], 1, default)).Numbers);
        Assert.Equal(["1"], (await resolver.ComputeAsync([Arc], 1, default)).Numbers);
    }

    [Fact]
    public async Task AnOverrideFileWinsOverTheComputedPosition_ForJustTheIssuesItNames()
    {
        string path = Path.Combine(_dir, "arc_overrides.json");
        File.WriteAllText(path, """{ "77": { "2": 1 } }""");
        var resolver = Resolver([Issue(1, "1", "2025-01-01", null), Issue(2, "2", "2025-02-01", null)], overrides: path);

        Assert.Equal(["1"], (await resolver.ComputeAsync([Arc], 2, default)).Numbers);    // overridden: computed would be 2
        Assert.Equal(["1"], (await resolver.ComputeAsync([Arc], 1, default)).Numbers);    // untouched: computed
    }

    [Fact]
    public async Task AnUnreadableOverrideFile_IsIgnored()
    {
        string path = Path.Combine(_dir, "arc_overrides.json");
        File.WriteAllText(path, "{ not json");

        var positions = await Resolver([Issue(1, "1", "2025-01-01", null)], overrides: path).ComputeAsync([Arc], 1, default);

        Assert.Equal(["1"], positions.Numbers);
    }
}
