using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="EventMembershipResolver"/> (docs/superpowers/specs/2026-08-17-metadata-
/// model-phase4b-story-events-design.md) against a real SQLite database - same rationale as
/// <see cref="MediaRelationResolverTests"/>/<see cref="ContinuityResolverTests"/>.
/// </summary>
public class EventMembershipResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public EventMembershipResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_eventmembership_test_{Guid.NewGuid():N}.db");
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

    private static int SeedIssue(PaperbunkrDbContext context, int seriesId, string number)
    {
        var issue = new Issue { SeriesId = seriesId, Number = number };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static int SeedEvent(PaperbunkrDbContext context, string name)
    {
        var storyEvent = new StoryEvent { Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();
        return storyEvent.Id;
    }

    [Fact]
    public void AddMember_NewPair_Succeeds_AssignsIncrementingPosition()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Green Lantern");
        int issueA = SeedIssue(context, seriesId, "13");
        int issueB = SeedIssue(context, seriesId, "14");
        int eventId = SeedEvent(context, "Rise of the Third Army");

        EventMembershipResolver.AddMember(context, eventId, issueA, EventMembershipRole.Prologue);
        EventMembershipResolver.AddMember(context, eventId, issueB, EventMembershipRole.Core);

        var members = EventMembershipResolver.GetOrderedMembers(context, eventId);
        Assert.Equal(2, members.Count);
        Assert.Equal(0, members[0].Position);
        Assert.Equal(1, members[1].Position);
        Assert.Equal(EventMembershipRole.Prologue, members[0].Role);
    }

    [Fact]
    public void AddMember_ExactDuplicate_RejectedNoWrite()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Green Lantern");
        int issueId = SeedIssue(context, seriesId, "13");
        int eventId = SeedEvent(context, "Rise of the Third Army");
        EventMembershipResolver.AddMember(context, eventId, issueId, EventMembershipRole.Core);

        bool added = EventMembershipResolver.AddMember(context, eventId, issueId, EventMembershipRole.TieIn);

        Assert.False(added);
        Assert.Single(context.EventMemberships);
    }

    [Fact]
    public void GetOrderedMembers_SortedByPosition()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Green Lantern");
        int issueA = SeedIssue(context, seriesId, "13");
        int issueB = SeedIssue(context, seriesId, "14");
        int eventId = SeedEvent(context, "Rise of the Third Army");
        EventMembershipResolver.AddMember(context, eventId, issueA, EventMembershipRole.Prologue);
        EventMembershipResolver.AddMember(context, eventId, issueB, EventMembershipRole.Core);

        var members = EventMembershipResolver.GetOrderedMembers(context, eventId);

        Assert.Equal(issueA, members[0].IssueId);
        Assert.Equal(issueB, members[1].IssueId);
    }

    [Fact]
    public void Reorder_SwapsAdjacentPositions()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Green Lantern");
        int issueA = SeedIssue(context, seriesId, "13");
        int issueB = SeedIssue(context, seriesId, "14");
        int eventId = SeedEvent(context, "Rise of the Third Army");
        EventMembershipResolver.AddMember(context, eventId, issueA, EventMembershipRole.Prologue);
        EventMembershipResolver.AddMember(context, eventId, issueB, EventMembershipRole.Core);
        int secondMemberId = EventMembershipResolver.GetOrderedMembers(context, eventId)[1].Id;

        EventMembershipResolver.Reorder(context, secondMemberId, offset: -1);

        var members = EventMembershipResolver.GetOrderedMembers(context, eventId);
        Assert.Equal(issueB, members[0].IssueId);
        Assert.Equal(issueA, members[1].IssueId);
    }

    [Fact]
    public void Reorder_AtBoundary_NoOp()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Green Lantern");
        int issueId = SeedIssue(context, seriesId, "13");
        int eventId = SeedEvent(context, "Rise of the Third Army");
        EventMembershipResolver.AddMember(context, eventId, issueId, EventMembershipRole.Core);
        int memberId = EventMembershipResolver.GetOrderedMembers(context, eventId)[0].Id;

        EventMembershipResolver.Reorder(context, memberId, offset: -1);

        var members = EventMembershipResolver.GetOrderedMembers(context, eventId);
        Assert.Equal(0, members[0].Position);
    }

    [Fact]
    public void RemoveMember_DeletesRow()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Green Lantern");
        int issueId = SeedIssue(context, seriesId, "13");
        int eventId = SeedEvent(context, "Rise of the Third Army");
        EventMembershipResolver.AddMember(context, eventId, issueId, EventMembershipRole.Core);
        int memberId = EventMembershipResolver.GetOrderedMembers(context, eventId)[0].Id;

        EventMembershipResolver.RemoveMember(context, memberId);

        Assert.Empty(context.EventMemberships);
    }

    [Fact]
    public void RemoveMember_NonExistent_NoOp_DoesNotThrow()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);

        EventMembershipResolver.RemoveMember(context, 999);
    }

    [Fact]
    public void GetOtherSeriesInSharedEvents_ExcludesQueriedSeries_IncludesOthers()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesAId = SeedSeries(context, "Green Lantern");
        int seriesBId = SeedSeries(context, "Green Lantern Corps");
        int seriesCId = SeedSeries(context, "Batman"); // not in the event
        int issueA = SeedIssue(context, seriesAId, "13");
        int issueB = SeedIssue(context, seriesBId, "13");
        int eventId = SeedEvent(context, "Rise of the Third Army");
        EventMembershipResolver.AddMember(context, eventId, issueA, EventMembershipRole.Core);
        EventMembershipResolver.AddMember(context, eventId, issueB, EventMembershipRole.Core);

        var result = EventMembershipResolver.GetOtherSeriesInSharedEvents(context, seriesAId);

        var other = Assert.Single(result);
        Assert.Equal(seriesBId, other.Id);
        Assert.DoesNotContain(result, s => s.Id == seriesCId);
    }

    [Fact]
    public void GetOtherSeriesInSharedEvents_SameSeriesDifferentIssuesSameEvent_ExcludesSelf()
    {
        // Two issues from the SAME series in the same event with different roles - the resolver
        // must not report the series as "another" series sharing the event with itself.
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesAId = SeedSeries(context, "Green Lantern");
        int issueA1 = SeedIssue(context, seriesAId, "13");
        int issueA2 = SeedIssue(context, seriesAId, "14");
        int eventId = SeedEvent(context, "Rise of the Third Army");
        EventMembershipResolver.AddMember(context, eventId, issueA1, EventMembershipRole.Prologue);
        EventMembershipResolver.AddMember(context, eventId, issueA2, EventMembershipRole.Core);

        var result = EventMembershipResolver.GetOtherSeriesInSharedEvents(context, seriesAId);

        Assert.Empty(result);
    }

    [Fact]
    public void GetOtherSeriesInSharedEvents_NoMembership_ReturnsEmpty()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesAId = SeedSeries(context, "Green Lantern");

        var result = EventMembershipResolver.GetOtherSeriesInSharedEvents(context, seriesAId);

        Assert.Empty(result);
    }

    [Fact]
    public void DeletingStoryEvent_CascadesMembership()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Green Lantern");
        int issueId = SeedIssue(context, seriesId, "13");
        int eventId = SeedEvent(context, "Rise of the Third Army");
        EventMembershipResolver.AddMember(context, eventId, issueId, EventMembershipRole.Core);

        context.StoryEvents.Remove(context.StoryEvents.Single());
        context.SaveChanges();

        Assert.Empty(context.EventMemberships);
        Assert.NotNull(context.Issues.Find(issueId));
    }

    [Fact]
    public void DeletingIssue_WithEventMembership_ThrowsRestrictViolation()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, "Green Lantern");
        int issueId = SeedIssue(context, seriesId, "13");
        int eventId = SeedEvent(context, "Rise of the Third Army");
        EventMembershipResolver.AddMember(context, eventId, issueId, EventMembershipRole.Core);

        // Restrict is enforced client-side (fail-fast, before ever reaching the database) once
        // both the principal (Issue) and the Restrict-configured dependent (EventMembership) are
        // tracked in the same context - EF can neither null the required FK nor cascade, so
        // Remove() itself throws InvalidOperationException rather than waiting for SaveChanges.
        context.EventMemberships.Load();
        Assert.Throws<InvalidOperationException>(() => context.Issues.Remove(context.Issues.Single()));
    }
}
