using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests;

/// <summary>Role detection end to end against a real (SQLite) database: the source parser keeping its annotations, the arc builder applying and
/// suggesting roles, the "Detect roles" actions, and the guarantee that a role the user set is never overwritten
/// (docs/superpowers/specs/2026-09-25-reading-list-role-detection-design.md).</summary>
public sealed class RoleDetectionIntegrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_roles_test_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;

    public RoleDetectionIntegrationTests()
    {
        _context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext Fresh() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private static IReadOnlyList<ArcIssue> AnnotatedArc() => new[]
    {
        new ArcIssue("Amazing Spider-Man", "1", 2020, null, Annotation: "Prelude"),
        new ArcIssue("Amazing Spider-Man", "2", 2020, null),
        new ArcIssue("Amazing Spider-Man", "3", 2020, null, Annotation: "Tie-Ins | Takes place during issue 2"),
        new ArcIssue("Amazing Spider-Man", "4", 2020, null),
        new ArcIssue("Thor", "5", 2020, null),
    };

    // -- parser ------------------------------------------------------------------------------------------------

    [Fact]
    public void TheParser_KeepsSectionHeadingsAndNotes_AndAttachesThemToTheIssuesTheyDescribe()
    {
        const string html = "<h1>Civil War Reading Order</h1><h2>Prelude</h2><p>Series A #1 (2006)</p><p>Series A #2 (2006)</p>" +
                            "<h2>Main Story</h2><span style=\"color: #0000ff;\">Tie-In: see Series B</span><p>Series B #3 (2006)</p><p>Series A #4 (2006)</p>";

        var issues = ComicBookReadingOrdersSource.ParseIssuesFromHtml(html);

        Assert.Equal(4, issues.Count);
        Assert.Equal("Prelude", issues[0].Annotation);
        Assert.Equal("Prelude", issues[1].Annotation);
        Assert.Equal("Main Story | Tie-In: see Series B", issues[2].Annotation);      // the note precedes, and belongs to, this issue
        Assert.Equal("Main Story", issues[3].Annotation);                             // and only that one
    }

    [Fact]
    public void ThePageTitleHeading_IsNotTreatedAsASection()
    {
        var issues = ComicBookReadingOrdersSource.ParseIssuesFromHtml("<h1>Spider-Man: Aftermath Reading Order</h1><p>Series A #1 (2006)</p>");

        Assert.Null(Assert.Single(issues).Annotation);
    }

    // -- arc builder ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task CreatingAListFromAnArc_AppliesTheRolesTheSourceIsSureOf_AndSuggestsTheRest()
    {
        var source = new FakeReadingListSource("Fake", AnnotatedArc(), null);

        var list = await ArcReadingListBuilder.CreateFromArcAsync(_context, source, new ArcSearchResult("a", "Arc", null, null, 5), CancellationToken.None);

        var items = list.Items.OrderBy(i => i.SortOrder).ToList();
        Assert.Equal(EventMembershipRole.Prologue, items[0].Role);
        Assert.Equal(RoleAssignmentSource.Auto, items[0].RoleSource);
        Assert.Contains("Prelude", items[0].RoleReason);
        Assert.Null(items[1].Role);                                                    // nothing says what issue 2 is
        Assert.Equal(EventMembershipRole.TieIn, items[2].Role);
        Assert.Null(items[4].Role);                                                    // Thor #5: only a low-confidence guess ...
        Assert.Equal(EventMembershipRole.TieIn, items[4].SuggestedRole);               // ... a different series than the rest of the arc
        Assert.Null(items[4].RoleSource);
    }

    [Fact]
    public async Task ARoleTheUserSetSurvivesARefresh_AndIsOnlyEverSuggestedAgainst()
    {
        var source = new FakeReadingListSource("Fake", AnnotatedArc(), null);
        var list = await ArcReadingListBuilder.CreateFromArcAsync(_context, source, new ArcSearchResult("a", "Arc", null, null, 5), CancellationToken.None);
        int firstItemId = list.Items.OrderBy(i => i.SortOrder).First().Id;

        var item = _context.ReadingListItems.First(i => i.Id == firstItemId);
        item.Role = EventMembershipRole.Core;                                            // the user overrides the automatic Prologue
        MemberRoleApplier.MarkUserSet(item);
        _context.SaveChanges();

        var result = await ArcReadingListBuilder.RefreshAsync(_context, list.Id, CancellationToken.None, source);

        var after = Fresh().ReadingListItems.First(i => i.Id == firstItemId);
        Assert.Equal(EventMembershipRole.Core, after.Role);
        Assert.Equal(RoleAssignmentSource.User, after.RoleSource);
        Assert.Equal(EventMembershipRole.Prologue, after.SuggestedRole);                  // the source still says Prelude, so it is offered
        Assert.NotNull(result.Roles);
    }

    [Fact]
    public async Task ADismissedSuggestion_IsNotOfferedAgainByARefresh()
    {
        var source = new FakeReadingListSource("Fake", AnnotatedArc(), null);
        var list = await ArcReadingListBuilder.CreateFromArcAsync(_context, source, new ArcSearchResult("a", "Arc", null, null, 5), CancellationToken.None);
        var last = _context.ReadingListItems.Where(i => i.ReadingListId == list.Id).OrderByDescending(i => i.SortOrder).First();
        Assert.Equal(EventMembershipRole.TieIn, last.SuggestedRole);

        MemberRoleApplier.Dismiss(last);
        _context.SaveChanges();
        await ArcReadingListBuilder.RefreshAsync(_context, list.Id, CancellationToken.None, source);

        var after = Fresh().ReadingListItems.First(i => i.Id == last.Id);
        Assert.Null(after.SuggestedRole);
        Assert.True(after.RoleSuggestionDismissed);
    }

    // -- Detect roles actions ---------------------------------------------------------------------------------------

    private Issue AddIssue(string series, string number, string? title = null, string? format = null)
    {
        var seriesRow = _context.Series.FirstOrDefault(s => s.Name == series) ?? _context.Series.Add(new Series { Name = series, SortName = series }).Entity;
        _context.SaveChanges();
        var issue = new Issue { SeriesId = seriesRow.Id, Number = number, Title = title, Format = format, Year = 2020 };
        _context.Issues.Add(issue);
        _context.SaveChanges();
        return issue;
    }

    [Fact]
    public void DetectRolesOnAList_FillsEmptyItems_SuggestsForTheUsersAndLeavesTheirChoicesAlone()
    {
        var prologue = AddIssue("Spider-Man", "1", format: "Prologue");
        var aftermath = AddIssue("Spider-Man", "2", title: "Aftermath");
        var users = AddIssue("Spider-Man", "3", title: "Epilogue");
        var plain = AddIssue("Spider-Man", "4");
        var list = new ReadingList { Name = "L", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        int order = 0;
        foreach (var issue in new[] { prologue, aftermath, users, plain })
        {
            list.Items.Add(new ReadingListItem { IssueId = issue.Id, SortOrder = order++ });
        }

        list.Items[2].Role = EventMembershipRole.Core;                                   // legacy user choice, no source recorded
        _context.ReadingLists.Add(list);
        _context.SaveChanges();

        var summary = MemberRoleDetection.DetectForList(_context, list.Id);

        var items = Fresh().ReadingListItems.Where(i => i.ReadingListId == list.Id).OrderBy(i => i.SortOrder).ToList();
        Assert.Equal(EventMembershipRole.Prologue, items[0].Role);
        Assert.Equal(EventMembershipRole.Aftermath, items[1].Role);
        Assert.Equal(EventMembershipRole.Core, items[2].Role);                            // untouched
        Assert.Equal(EventMembershipRole.Epilogue, items[2].SuggestedRole);               // offered instead
        Assert.Null(items[3].Role);
        Assert.Equal(new RoleDetectionSummary(2, 1), summary);
        Assert.Equal("2 roles detected, 1 needs review.", summary.ToString());
    }

    [Fact]
    public void DetectRolesOnAnEvent_OnlySuggestsForExistingMembers()
    {
        var prologue = AddIssue("Spider-Man", "1", format: "Prologue");
        var storyEvent = new StoryEvent { Name = "Big Event" };
        _context.StoryEvents.Add(storyEvent);
        _context.SaveChanges();
        _context.EventMemberships.Add(new EventMembership { StoryEventId = storyEvent.Id, IssueId = prologue.Id, Position = 0, Role = EventMembershipRole.Core });
        _context.SaveChanges();

        var summary = MemberRoleDetection.DetectForEvent(_context, storyEvent.Id);

        var member = Fresh().EventMemberships.Single();
        Assert.Equal(EventMembershipRole.Core, member.Role);                              // Core was the old default: indistinguishable from a choice
        Assert.Equal(EventMembershipRole.Prologue, member.SuggestedRole);
        Assert.Equal(new RoleDetectionSummary(0, 1), summary);
    }

    [Fact]
    public void AddingAnArcAsAnEvent_GivesEachNewMemberTheRoleTheDetectorIsSureOf()
    {
        var first = AddIssue("Spider-Man", "1", format: "Prologue");
        var middle = AddIssue("Spider-Man", "2");
        var storyEvent = new StoryEvent { Name = "Big Event" };
        _context.StoryEvents.Add(storyEvent);
        _context.SaveChanges();

        var summary = MemberRoleDetection.AddDetectedMembers(_context, storyEvent.Id, new[] { first.Id, middle.Id });

        var members = Fresh().EventMemberships.OrderBy(m => m.Position).ToList();
        Assert.Equal(EventMembershipRole.Prologue, members[0].Role);
        Assert.Equal(RoleAssignmentSource.Auto, members[0].RoleSource);
        Assert.Equal(EventMembershipRole.Core, members[1].Role);
        Assert.Null(members[1].RoleSource);                                                // the old default, unchanged
        Assert.Equal(new RoleDetectionSummary(1, 0), summary);
    }

    [Fact]
    public void RowsWrittenBeforeRoleDetectionExisted_ReadBackAsUserOwned()
    {
        var issue = AddIssue("Spider-Man", "1");
        _context.ReadingLists.Add(new ReadingList
        {
            Name = "Legacy", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Items = { new ReadingListItem { IssueId = issue.Id, Role = EventMembershipRole.TieIn } },
        });
        _context.SaveChanges();

        var item = Fresh().ReadingListItems.Single();

        Assert.Equal(EventMembershipRole.TieIn, item.Role);
        Assert.Null(item.RoleSource);
        Assert.Null(item.SuggestedRole);
        Assert.False(item.RoleSuggestionDismissed);
    }

    [Fact]
    public void TheNewColumns_RoundTripThroughTheDatabase()
    {
        var issue = AddIssue("Spider-Man", "1");
        _context.ReadingLists.Add(new ReadingList
        {
            Name = "L", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Items =
            {
                new ReadingListItem
                {
                    IssueId = issue.Id, Role = EventMembershipRole.Aftermath, RoleSource = RoleAssignmentSource.Auto, RoleReason = "why",
                    SuggestedRole = EventMembershipRole.TieIn, SuggestedReason = "because", RoleSuggestionDismissed = true,
                },
            },
        });
        _context.SaveChanges();

        var item = Fresh().ReadingListItems.Single();

        Assert.Equal(RoleAssignmentSource.Auto, item.RoleSource);
        Assert.Equal("why", item.RoleReason);
        Assert.Equal(EventMembershipRole.TieIn, item.SuggestedRole);
        Assert.Equal("because", item.SuggestedReason);
        Assert.True(item.RoleSuggestionDismissed);
    }
}
