using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Tests.Acquisition;

namespace Paperbunkr.Data.Tests.ComicVine;

/// <summary>
/// Exercises <see cref="ComicProviderAdopt"/> (docs/superpowers/specs/2026-09-23-metron-api-
/// utilization-design.md) against fake providers and a real SQLite database.
/// </summary>
public class ComicProviderAdoptTests : AcquisitionTestBase
{
    private sealed class FakeSource(Func<int, ComicVineIssueDetails?> respond) : IComicVineIssueDetailsSource
    {
        public int Calls;

        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(issueId));
        }
    }

    private static ComicVineIssueDetails MetronDetails() => new(
        900, 10, "Test Series", "1", "Endgame", null, new ComicVineDatePart(2018, 7, 1), new ComicVineDatePart(2018, 5, 9), "Metron summary",
        Array.Empty<ComicVineIdName>(), new[] { new ComicVineIdName(7, "Captain America") }, Array.Empty<ComicVineIdName>(),
        Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineCredit>());

    private static ComicVineIssueDetails ComicVineDetails() => new(
        4321, 10, "Test Series", "1", "Endgame", null, new ComicVineDatePart(2018, 7, 1), new ComicVineDatePart(2018, 5, 9), "CV summary",
        Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(),
        new[] { new ComicVineIdName(701, "Rat City") }, Array.Empty<ComicVineCredit>());

    private int SeedIssue()
    {
        var issue = new Issue { Series = new Series { Name = "Test Series" }, Number = "1" };
        Context.Issues.Add(issue);
        Context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public async Task AdoptAsync_MergesBothProviders_WhenBothIdsAreKnown()
    {
        int issueId = SeedIssue();
        var metronSource = new FakeSource(id => id == 900 ? MetronDetails() : null);
        var comicVineSource = new FakeSource(id => id == 4321 ? ComicVineDetails() : null);

        var outcome = await ComicProviderAdopt.AdoptAsync(
            NewContext,
            provider => provider == ComicProvider.Metron ? metronSource : comicVineSource,
            issueId, metronIssueId: 900, comicVineIssueId: 4321, CancellationToken.None);

        Assert.Equal(ScrapeResultKind.Scraped, outcome.Kind);
        var issue = Context.Issues.AsNoTracking().Single(i => i.Id == issueId);
        Assert.Equal("Captain America", issue.Characters);
        Assert.Equal("Rat City", issue.Locations);
        Assert.Equal(1, metronSource.Calls);
        Assert.Equal(1, comicVineSource.Calls);
    }

    [Fact]
    public async Task AdoptAsync_NoComicVineId_MergesMetronAlone()
    {
        int issueId = SeedIssue();
        var metronSource = new FakeSource(id => id == 900 ? MetronDetails() : null);
        var comicVineSource = new FakeSource(_ => ComicVineDetails());

        var outcome = await ComicProviderAdopt.AdoptAsync(
            NewContext,
            provider => provider == ComicProvider.Metron ? metronSource : comicVineSource,
            issueId, metronIssueId: 900, comicVineIssueId: null, CancellationToken.None);

        Assert.Equal(ScrapeResultKind.Scraped, outcome.Kind);
        Assert.Equal(0, comicVineSource.Calls);   // never called - no id supplied
        Assert.Null(Context.Issues.AsNoTracking().Single(i => i.Id == issueId).Locations);
    }

    [Fact]
    public async Task AdoptAsync_MetronCredentialsMissing_IsTerminal()
    {
        int issueId = SeedIssue();

        var outcome = await ComicProviderAdopt.AdoptAsync(NewContext, _ => null, issueId, 900, null, CancellationToken.None);

        Assert.Equal(ScrapeResultKind.Terminal, outcome.Kind);
        Assert.Contains("Connections", outcome.Message);
    }

    [Fact]
    public async Task AdoptAsync_ComicVineFetchThrows_StillSucceedsWithMetronAlone()
    {
        int issueId = SeedIssue();
        var metronSource = new FakeSource(id => id == 900 ? MetronDetails() : null);
        var throwingComicVine = new ThrowingSource(new ComicVineException("boom"));

        var outcome = await ComicProviderAdopt.AdoptAsync(
            NewContext,
            provider => provider == ComicProvider.Metron ? metronSource : throwingComicVine,
            issueId, metronIssueId: 900, comicVineIssueId: 4321, CancellationToken.None);

        Assert.Equal(ScrapeResultKind.Scraped, outcome.Kind);
        Assert.Equal("Captain America", Context.Issues.AsNoTracking().Single(i => i.Id == issueId).Characters);
    }

    private sealed class ThrowingSource(ComicVineException error) : IComicVineIssueDetailsSource
    {
        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken) => throw error;
    }
}
