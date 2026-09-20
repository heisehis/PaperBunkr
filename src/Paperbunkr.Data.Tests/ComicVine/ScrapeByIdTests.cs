using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Tests.Acquisition;

namespace Paperbunkr.Data.Tests.ComicVine;

public class IssueDetailsApplierTests
{
    private static ComicVineIssueDetails Details(string? summary = "A story.") => new(
        4321, 91273, "Spawn", "263", "Endgame", "https://cv/263", new ComicVineDatePart(2016, 5, 1), new ComicVineDatePart(2016, 5, 4), summary,
        new[] { "Endgame Arc" }, new[] { "Spawn", "Sam" }, new[] { "Hellspawn" }, new[] { "Rat City" },
        new[] { new ComicVineCredit("Todd", "Writer"), new ComicVineCredit("Greg", "Colorist"), new ComicVineCredit("Todd", "Writer"), new ComicVineCredit("Nobody", null) });

    [Fact]
    public void FillsEveryPerIssueField_AndReportsWhatChanged()
    {
        var issue = new Issue { Number = "263" };

        var changed = IssueDetailsApplier.Apply(issue, Details(), ScrapeFieldPolicy.Default);

        Assert.Equal("Endgame", issue.Title);
        Assert.Equal("A story.", issue.Summary);
        Assert.Equal("https://cv/263", issue.Web);
        Assert.Equal("Endgame Arc", issue.StoryArc);
        Assert.Equal("Spawn, Sam", issue.Characters);
        Assert.Equal("Hellspawn", issue.Teams);
        Assert.Equal("Rat City", issue.Locations);
        Assert.Equal((2016, 5, 1), (issue.Year, issue.Month, issue.Day));
        Assert.Equal(new DateTime(2016, 5, 4), issue.ReleasedTime);
        Assert.Equal("Todd", issue.Writer);                       // a repeated credit is listed once
        Assert.Equal("Greg", issue.Colorist);
        Assert.Null(issue.Penciller);                             // ComicVine named nobody for it
        Assert.DoesNotContain(ScrapeField.Number, changed);       // already equal: not a change
        Assert.Contains(ScrapeField.Title, changed);
        Assert.Contains(ScrapeField.Writer, changed);
    }

    [Fact]
    public void ABlankComicVineValue_NeverBlanksAnExistingOne()
    {
        var issue = new Issue { Summary = "Mine.", Title = "Mine" };

        IssueDetailsApplier.Apply(issue, Details(summary: null) with { Title = null }, ScrapeFieldPolicy.Default);

        Assert.Equal("Mine.", issue.Summary);
        Assert.Equal("Mine", issue.Title);
    }

    [Fact]
    public void DisabledFields_AreLeftAlone_AndWithoutOverwriteExistingValuesSurvive()
    {
        var issue = new Issue { Title = "Mine" };
        var policy = new ScrapeFieldPolicy { OverwriteExisting = false, Enabled = new HashSet<ScrapeField> { ScrapeField.Title, ScrapeField.Summary } };

        var changed = IssueDetailsApplier.Apply(issue, Details(), policy);

        Assert.Equal("Mine", issue.Title);                        // has a value, overwrite off
        Assert.Equal("A story.", issue.Summary);                  // empty, so filled
        Assert.Null(issue.Characters);                            // not enabled
        Assert.Equal(new[] { ScrapeField.Summary }, changed);
    }

    [Fact]
    public void AnImpossibleDayFallsBackToTheFirst_InsteadOfLosingTheReleaseDate()
    {
        var issue = new Issue();
        var details = Details() with { ReleasedDate = new ComicVineDatePart(2016, 2, 31) };

        IssueDetailsApplier.Apply(issue, details, ScrapeFieldPolicy.Default);

        Assert.Equal(new DateTime(2016, 2, 1), issue.ReleasedTime);
    }
}

public class ScrapeByIdServiceTests : AcquisitionTestBase
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

    private sealed class ThrowingSource(ComicVineException error) : IComicVineIssueDetailsSource
    {
        public Task<ComicVineIssueDetails?> GetIssueDetailsAsync(int issueId, CancellationToken cancellationToken) => throw error;
    }

    private static ComicVineIssueDetails Details() => new(
        4321, 1, "Spawn", "263", "Endgame", null, new ComicVineDatePart(2016, 5, 1), new ComicVineDatePart(null, null, null), "Story",
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), new[] { new ComicVineCredit("Todd", "Writer") });

    private (int WantedId, int IssueId) Seed(bool withIssue = true)
    {
        var issue = new Issue { Series = new Series { Name = "Spawn" }, Number = "263", FilePath = "C:/x/Spawn 263.cbz" };
        Context.Issues.Add(issue);
        var watched = new WatchedSeries { Name = "Spawn", ComicVineVolumeId = 1 };
        Context.WatchedSeries.Add(watched);
        Context.SaveChanges();
        var wanted = new WantedIssue { WatchedSeriesId = watched.Id, ComicVineIssueId = 4321, IssueNumber = "263", IssueId = withIssue ? issue.Id : null, CreatedAt = DateTime.UtcNow };
        Context.WantedIssues.Add(wanted);
        Context.SaveChanges();
        return (wanted.Id, issue.Id);
    }

    private ScrapeByIdService Service(IComicVineIssueDetailsSource? source) => new(NewContext, () => source);

    [Fact]
    public async Task Scrapes_ByTheKnownComicVineId_WithoutSearching()
    {
        var (wantedId, issueId) = Seed();
        var source = new FakeSource(id => id == 4321 ? Details() : null);

        var outcome = await Service(source).ScrapeAsync(wantedId, CancellationToken.None);

        Assert.Equal(ScrapeResultKind.Scraped, outcome.Kind);
        Assert.Equal(issueId, outcome.IssueId);
        Assert.Equal(1, source.Calls);
        var saved = Context.Issues.AsNoTracking().Single(i => i.Id == issueId);
        Assert.Equal("Endgame", saved.Title);
        Assert.Equal("Todd", saved.Writer);
    }

    [Fact]
    public async Task AMissingKey_IsTerminal_AndTellsTheUserWhereToFixIt()
    {
        var (wantedId, _) = Seed();

        var outcome = await Service(null).ScrapeAsync(wantedId, CancellationToken.None);

        Assert.Equal(ScrapeResultKind.Terminal, outcome.Kind);
        Assert.Contains("Connections", outcome.Message);
    }

    [Fact]
    public async Task ComicVineHavingNoSuchIssue_IsTerminal_ButAHiccupIsRetryable()
    {
        var (wantedId, _) = Seed();

        Assert.Equal(ScrapeResultKind.Terminal, (await Service(new FakeSource(_ => null)).ScrapeAsync(wantedId, CancellationToken.None)).Kind);
        Assert.Equal(ScrapeResultKind.Retryable, (await Service(new ThrowingSource(new ComicVineException("ComicVine request failed"))).ScrapeAsync(wantedId, CancellationToken.None)).Kind);
        Assert.Equal(ScrapeResultKind.Retryable, (await Service(new ThrowingSource(new ComicVineException("Rate limit", 107))).ScrapeAsync(wantedId, CancellationToken.None)).Kind);
        Assert.Equal(ScrapeResultKind.Terminal, (await Service(new ThrowingSource(new ComicVineException("bad key", 100))).ScrapeAsync(wantedId, CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task AWantWithNoLocalIssue_IsTerminal_AndNeverCallsComicVine()
    {
        var (wantedId, _) = Seed(withIssue: false);
        var source = new FakeSource(_ => Details());

        var outcome = await Service(source).ScrapeAsync(wantedId, CancellationToken.None);

        Assert.Equal(ScrapeResultKind.Terminal, outcome.Kind);
        Assert.Equal(0, source.Calls);
    }
}
