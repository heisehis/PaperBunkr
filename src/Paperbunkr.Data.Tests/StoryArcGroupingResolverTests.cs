using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="StoryArcGroupingResolver"/> (docs/superpowers/specs/2026-09-17-storyevent-
/// continuity-autopopulate-design.md, Phase 1) against a real SQLite database.
/// </summary>
public class StoryArcGroupingResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public StoryArcGroupingResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_arcgrouping_test_{Guid.NewGuid():N}.db");
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

    private static int SeedSeries(PaperbunkrDbContext context, string name)
    {
        var series = new Series { Name = name };
        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private static Issue SeedIssue(PaperbunkrDbContext context, int seriesId, string number, string? storyArc, string? storyArcNumber = null, string? publisher = null, int? year = null)
    {
        var issue = new Issue { SeriesId = seriesId, Number = number, StoryArc = storyArc, StoryArcNumber = storyArcNumber, Publisher = publisher, Year = year };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue;
    }

    [Fact]
    public void TwoIssuesSharingArcAndPublisher_AreGroupedTogether()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        SeedIssue(context, seriesId, "1", "Civil War", publisher: "Marvel");
        SeedIssue(context, seriesId, "2", "Civil War", publisher: "Marvel");

        var candidate = Assert.Single(StoryArcGroupingResolver.GetCandidates(context));

        Assert.Equal("Civil War", candidate.ArcName);
        Assert.Equal(2, candidate.Members.Count);
    }

    [Fact]
    public void SingleIssueArc_IsNotProposed()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        SeedIssue(context, seriesId, "1", "Only Once", publisher: "Marvel");

        Assert.Empty(StoryArcGroupingResolver.GetCandidates(context));
    }

    [Fact]
    public void SameArcName_DifferentPublisher_AreNotMerged()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int marvelSeries = SeedSeries(context, "Avengers");
        int dcSeries = SeedSeries(context, "Justice League");
        SeedIssue(context, marvelSeries, "1", "Rebirth", publisher: "Marvel");
        SeedIssue(context, marvelSeries, "2", "Rebirth", publisher: "Marvel");
        SeedIssue(context, dcSeries, "1", "Rebirth", publisher: "DC Comics");
        SeedIssue(context, dcSeries, "2", "Rebirth", publisher: "DC Comics");

        var candidates = StoryArcGroupingResolver.GetCandidates(context);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, c => c.Publisher == "Marvel");
        // "DC Comics" normalizes to "DC" - proves the alias map ran, not just a pass-through.
        Assert.Contains(candidates, c => c.Publisher == "DC");
    }

    [Fact]
    public void PublisherAliasVariants_AreCollapsedIntoOneGroup()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        SeedIssue(context, seriesId, "1", "Secret Invasion", publisher: "Marvel");
        SeedIssue(context, seriesId, "2", "Secret Invasion", publisher: "Marvel Comics");

        var candidate = Assert.Single(StoryArcGroupingResolver.GetCandidates(context));
        Assert.Equal(2, candidate.Members.Count);
    }

    [Fact]
    public void CommaSeparatedStoryArcAndNumber_ArePairedPositionally()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        // One issue tags two arcs at once, in the real ComicInfo.xml multi-arc convention.
        SeedIssue(context, seriesId, "1", "Civil War, Secret Invasion", "3, 1", publisher: "Marvel");
        SeedIssue(context, seriesId, "2", "Civil War", "4", publisher: "Marvel");
        SeedIssue(context, seriesId, "3", "Secret Invasion", "2", publisher: "Marvel");

        var candidates = StoryArcGroupingResolver.GetCandidates(context);

        var civilWar = Assert.Single(candidates, c => c.ArcName == "Civil War");
        Assert.Equal(2, civilWar.Members.Count);
        Assert.Equal(3, civilWar.Members.First(m => m.Issue.Number == "1").Position);
        Assert.Equal(4, civilWar.Members.First(m => m.Issue.Number == "2").Position);

        var secretInvasion = Assert.Single(candidates, c => c.ArcName == "Secret Invasion");
        Assert.Equal(2, secretInvasion.Members.Count);
        Assert.Equal(1, secretInvasion.Members.First(m => m.Issue.Number == "1").Position);
        Assert.Equal(2, secretInvasion.Members.First(m => m.Issue.Number == "3").Position);
    }

    [Fact]
    public void RaggedStoryArcNumberList_PairsMissingEntriesWithNullPosition()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        // Issue 1 tags two arcs but only has one StoryArcNumber token - Secret Invasion's position
        // is missing (ragged), not misaligned onto Civil War's slot.
        SeedIssue(context, seriesId, "1", "Civil War, Secret Invasion", "3", publisher: "Marvel");
        SeedIssue(context, seriesId, "2", "Civil War", publisher: "Marvel");
        SeedIssue(context, seriesId, "3", "Secret Invasion", publisher: "Marvel");

        var civilWar = Assert.Single(StoryArcGroupingResolver.GetCandidates(context), c => c.ArcName == "Civil War");
        Assert.Equal(3, civilWar.Members.First(m => m.Issue.Number == "1").Position);
        Assert.Null(civilWar.Members.First(m => m.Issue.Number == "2").Position);

        var secretInvasion = Assert.Single(StoryArcGroupingResolver.GetCandidates(context), c => c.ArcName == "Secret Invasion");
        Assert.Equal(2, secretInvasion.Members.Count);
        Assert.Null(secretInvasion.Members.First(m => m.Issue.Number == "1").Position);
        Assert.Null(secretInvasion.Members.First(m => m.Issue.Number == "3").Position);
    }

    [Fact]
    public void DismissedArc_IsExcludedFromFutureScans()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        SeedIssue(context, seriesId, "1", "Civil War", publisher: "Marvel");
        SeedIssue(context, seriesId, "2", "Civil War", publisher: "Marvel");

        StoryArcGroupingResolver.Dismiss(context, "Civil War", "Marvel");

        Assert.Empty(StoryArcGroupingResolver.GetCandidates(context));

        StoryArcGroupingResolver.Restore(context, "Civil War", "Marvel");
        Assert.Single(StoryArcGroupingResolver.GetCandidates(context));
    }

    [Fact]
    public void IssueAlreadyMemberOfMatchingStoryEvent_IsExcluded()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        var issue1 = SeedIssue(context, seriesId, "1", "Civil War", publisher: "Marvel");
        SeedIssue(context, seriesId, "2", "Civil War", publisher: "Marvel");
        SeedIssue(context, seriesId, "3", "Civil War", publisher: "Marvel");

        var storyEvent = new StoryEvent { Name = "Civil War", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        context.EventMemberships.Add(new EventMembership { StoryEventId = storyEvent.Id, IssueId = issue1.Id, Position = 0, Role = EventMembershipRole.Core });
        context.SaveChanges();

        // Issue 1 is already tracked under "Civil War" - the remaining 2 untracked issues still
        // form a valid (>=2) candidate group.
        var candidate = Assert.Single(StoryArcGroupingResolver.GetCandidates(context));
        Assert.Equal(2, candidate.Members.Count);
        Assert.DoesNotContain(candidate.Members, m => m.Issue.Id == issue1.Id);
    }

    [Fact]
    public void TwoStoryEventsSharingAName_DoNotCrashTheScan_AndBothMemberSetsAreExcluded()
    {
        // The Story Events screen's own "New" button creates repeated "New Story Event" rows, so
        // duplicate names are normal - a ToDictionary keyed on name would throw here.
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Avengers");
        var issue1 = SeedIssue(context, seriesId, "1", "Civil War", publisher: "Marvel");
        var issue2 = SeedIssue(context, seriesId, "2", "Civil War", publisher: "Marvel");
        SeedIssue(context, seriesId, "3", "Civil War", publisher: "Marvel");
        SeedIssue(context, seriesId, "4", "Civil War", publisher: "Marvel");

        foreach (var member in new[] { issue1, issue2 })
        {
            var storyEvent = new StoryEvent { Name = "Civil War", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            context.StoryEvents.Add(storyEvent);
            context.SaveChanges();
            context.EventMemberships.Add(new EventMembership { StoryEventId = storyEvent.Id, IssueId = member.Id, Position = 0, Role = EventMembershipRole.Core });
            context.SaveChanges();
        }

        var candidate = Assert.Single(StoryArcGroupingResolver.GetCandidates(context));

        Assert.Equal(2, candidate.Members.Count);
        Assert.DoesNotContain(candidate.Members, m => m.Issue.Id == issue1.Id || m.Issue.Id == issue2.Id);
    }
}
