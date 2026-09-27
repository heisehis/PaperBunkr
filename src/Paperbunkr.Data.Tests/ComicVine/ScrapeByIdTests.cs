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
        new[] { new ComicVineIdName(null, "Endgame Arc") }, new[] { new ComicVineIdName(null, "Spawn"), new ComicVineIdName(null, "Sam") },
        new[] { new ComicVineIdName(null, "Hellspawn") }, new[] { new ComicVineIdName(null, "Rat City") },
        new[] { new ComicVineCredit("Todd", "Writer"), new ComicVineCredit("Greg", "Colorist"), new ComicVineCredit("Todd", "Writer"), new ComicVineCredit("Nobody", null) });

    [Fact]
    public void MapsImprintAgeRatingIsbnUpcAndCommunityRating()
    {
        var issue = new Issue { Number = "263" };
        var details = Details() with { Imprint = "Vertigo", AgeRating = "Teen", Isbn = "978-1", Upc = "012345", AverageRating = 4.5, RatingCount = 10 };

        var changed = IssueDetailsApplier.Apply(issue, details, ScrapeFieldPolicy.Default);

        Assert.Equal("Vertigo", issue.Imprint);
        Assert.Equal("Teen", issue.AgeRating);
        Assert.Equal("978-1", issue.ISBN);
        Assert.Equal("012345", issue.Upc);
        Assert.Equal(4.5f, issue.CommunityRating);
        Assert.Equal(10, issue.CommunityRatingCount);
        Assert.Contains(ScrapeField.Imprint, changed);
        Assert.Contains(ScrapeField.CommunityRating, changed);
    }

    [Fact]
    public void GenreIsAdditive_WrittenAsIssueTags_NeverRemovesExisting()
    {
        var issue = new Issue { Number = "263" };
        issue.Tags.Add(new IssueTag { Field = IssueTagField.Genre, Value = "Existing Genre" });
        var details = Details() with { Genres = new[] { "Superhero", "Existing Genre" } };

        var changed = IssueDetailsApplier.Apply(issue, details, ScrapeFieldPolicy.Default);

        Assert.Equal(2, issue.Tags.Count(t => t.Field == IssueTagField.Genre));
        Assert.Contains(issue.Tags, t => t.Value == "Existing Genre");
        Assert.Contains(issue.Tags, t => t.Value == "Superhero");
        Assert.Contains(ScrapeField.Genre, changed);
    }

    [Fact]
    public void ForkFields_MainCharacter_SeriesGroup_ConceptTags_AndStoryArcOrder_AreWritten()
    {
        var issue = new Issue { Number = "263" };
        var details = Details() with { } ;
        details = details with { StoryArcs = new[] { new ComicVineIdName(1, "Endgame Arc"), new ComicVineIdName(2, "Hell Arc") } };
        var withConcepts = new ComicVineIssueDetails(
            details.Id, details.VolumeId, details.VolumeName, details.IssueNumber, details.Title, details.SiteDetailUrl,
            details.PublishedDate, details.ReleasedDate, details.Summary, details.StoryArcs, details.Characters, details.Teams,
            details.Locations, details.Credits) { Concepts = new[] { "Time Travel", "time travel", "Resurrection" } };

        var changed = IssueDetailsApplier.Apply(issue, withConcepts, ScrapeFieldPolicy.Default,
            new StoryArcPositions(new[] { "4", string.Empty }, AlternateCount: 12));

        Assert.Equal("Spawn", issue.MainCharacterOrTeam);                 // the first character
        Assert.Equal("Endgame Arc, Hell Arc", issue.SeriesGroup);
        Assert.Equal("4", issue.StoryArcNumber);                          // the arc with no known position is dropped
        Assert.Equal("4", issue.AlternateNumber);
        Assert.Equal(12, issue.AlternateCount);
        Assert.Equal(new[] { "Resurrection", "Time Travel" }, issue.Tags.Where(t => t.Field == IssueTagField.Tags).Select(t => t.Value).OrderBy(v => v));
        Assert.Contains(ScrapeField.StoryArcOrder, changed);
        Assert.Contains(ScrapeField.Concepts, changed);
    }

    [Fact]
    public void MainCharacterOrTeam_FallsBackToTheFirstTeam_WhenThereAreNoCharacters()
    {
        var issue = new Issue { Number = "1" };
        var noCharacters = Details() with { Characters = Array.Empty<ComicVineIdName>() };

        IssueDetailsApplier.Apply(issue, noCharacters, ScrapeFieldPolicy.Default);

        Assert.Equal("Hellspawn", issue.MainCharacterOrTeam);
    }

    [Fact]
    public void ForkFields_AreLeftAlone_WhenTheirTogglesAreOff()
    {
        var issue = new Issue { Number = "263" };
        var policy = new ScrapeFieldPolicy
        {
            Enabled = new HashSet<ScrapeField>(Enum.GetValues<ScrapeField>().Except(new[]
            {
                ScrapeField.MainCharacterOrTeam, ScrapeField.SeriesGroup, ScrapeField.Concepts, ScrapeField.StoryArcOrder,
            })),
        };
        var details = Details() with { Concepts = new[] { "Time Travel" } };

        IssueDetailsApplier.Apply(issue, details, policy, new StoryArcPositions(new[] { "4" }, 12));

        Assert.Null(issue.MainCharacterOrTeam);
        Assert.Null(issue.SeriesGroup);
        Assert.Null(issue.StoryArcNumber);
        Assert.Null(issue.AlternateCount);
        Assert.Empty(issue.Tags.Where(t => t.Field == IssueTagField.Tags));
    }

    [Fact]
    public void GenreDuplicatedWithinTheSameResponse_IsWrittenOnlyOnce()
    {
        // Real-world crash (2026-09-24): ComicVine returning the same genre name twice for one issue
        // used to add two IssueTag rows with the same (Field, Value), since the old code filtered
        // details.Genres against a HashSet snapshot taken once before the loop instead of updating it
        // as each genre was added. The duplicate row then crashed IssuePropertiesScreenViewModel's
        // ApplyTagRows the next time that issue was opened for editing (ToDictionary on a case-
        // insensitive duplicate key).
        var issue = new Issue { Number = "263" };
        var details = Details() with { Genres = new[] { "Super-Hero", "Super-Hero" } };

        var changed = IssueDetailsApplier.Apply(issue, details, ScrapeFieldPolicy.Default);

        Assert.Single(issue.Tags.Where(t => t.Field == IssueTagField.Genre));
        Assert.Contains(issue.Tags, t => t.Value == "Super-Hero");
        Assert.Contains(ScrapeField.Genre, changed);
    }

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
        Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), Array.Empty<ComicVineIdName>(), new[] { new ComicVineCredit("Todd", "Writer") });

    private (int WantedId, int IssueId) Seed(bool withIssue = true)
    {
        var issue = new Issue { Series = new Series { Name = "Spawn" }, Number = "263", FilePath = "C:/x/Spawn 263.cbz" };
        Context.Issues.Add(issue);
        var watched = new WatchedSeries { Name = "Spawn", ExternalVolumeId = 1 };
        Context.WatchedSeries.Add(watched);
        Context.SaveChanges();
        var wanted = new WantedIssue { WatchedSeriesId = watched.Id, ExternalIssueId = 4321, IssueNumber = "263", IssueId = withIssue ? issue.Id : null, CreatedAt = DateTime.UtcNow };
        Context.WantedIssues.Add(wanted);
        Context.SaveChanges();
        return (wanted.Id, issue.Id);
    }

    private ScrapeByIdService Service(IComicVineIssueDetailsSource? source) => new(NewContext, _ => source);

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
    public async Task ScrapeMaterializesTheDerivedCreatorIndex()
    {
        // docs/superpowers/specs/2026-09-23-metron-api-utilization-design.md - a scrape must index
        // Character/Team/Location/Creator/Publisher, not just write the flat Issue string fields.
        var (wantedId, issueId) = Seed();
        var source = new FakeSource(id => id == 4321 ? Details() : null);

        await Service(source).ScrapeAsync(wantedId, CancellationToken.None);

        var creator = Context.Creators.Include(c => c.Credits).AsNoTracking().Single();
        Assert.Equal("Todd", creator.Name);
        Assert.Contains(creator.Credits, c => c.IssueId == issueId && c.Role == "Writer");
    }

    [Fact]
    public async Task TheIssueRemembersWhichSourceItsDetailsCameFrom()
    {
        var (wantedId, issueId) = Seed();
        Context.WantedIssues.Single(w => w.Id == wantedId).Provider = ComicProvider.Metron;
        Context.SaveChanges();
        var source = new FakeSource(id => id == 4321 ? Details() : null);

        await Service(source).ScrapeAsync(wantedId, CancellationToken.None);

        Assert.Equal(ComicProvider.Metron, Context.Issues.AsNoTracking().Single(i => i.Id == issueId).MetadataSource);
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
