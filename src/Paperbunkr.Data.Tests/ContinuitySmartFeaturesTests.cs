using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Events;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Smart features S5 (docs/superpowers/specs/2026-10-06-smart-features-design.md §7): shared-character continuity suggestions, the
/// reading-list chronology check, the series status event raised by the context, and continuity / story-event completion.
/// </summary>
public class ContinuitySmartFeaturesTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public ContinuitySmartFeaturesTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_continuity_smart_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private Series AddSeries(string name, int issues = 1, bool read = false)
    {
        var series = new Series { Name = name };
        for (int i = 1; i <= issues; i++)
        {
            series.Issues.Add(new Issue { Number = i.ToString(), PageCount = 20, LastPageRead = read ? 19 : null, FilePath = $"C:\\x\\{name}-{i}.cbz" });
        }

        _context.Series.Add(series);
        _context.SaveChanges();
        return series;
    }

    private Continuity AddContinuity(string name, params Series[] members)
    {
        var continuity = new Continuity { Name = name, CreatedAt = Now, UpdatedAt = Now };
        _context.Continuities.Add(continuity);
        _context.SaveChanges();
        foreach (var series in members)
        {
            ContinuityResolver.AddSeriesToContinuity(_context, series.Id, continuity.Id);
        }

        return continuity;
    }

    private void Appear(Series series, params string[] characters)
    {
        foreach (string name in characters)
        {
            var character = _context.Characters.FirstOrDefault(c => c.Name == name);
            if (character is null)
            {
                character = new Character { Name = name };
                _context.Characters.Add(character);
                _context.SaveChanges();
            }

            _context.CharacterAppearances.Add(new CharacterAppearance { CharacterId = character.Id, IssueId = series.Issues[0].Id });
        }

        _context.SaveChanges();
    }

    private StoryEvent AddEvent(string name, params Issue[] members)
    {
        var storyEvent = new StoryEvent { Name = name };
        _context.StoryEvents.Add(storyEvent);
        _context.SaveChanges();
        int position = 0;
        foreach (var issue in members)
        {
            _context.EventMemberships.Add(new EventMembership { StoryEventId = storyEvent.Id, IssueId = issue.Id, Position = position++ });
        }

        _context.SaveChanges();
        return storyEvent;
    }

    private void Relate(StoryEvent source, StoryEvent target, RelationType type)
    {
        _context.EventRelations.Add(new EventRelation { SourceEventId = source.Id, TargetEventId = target.Id, RelationType = type });
        _context.SaveChanges();
    }

    private ReadingList AddList(string name, params Issue[] issues)
    {
        var list = new ReadingList { Name = name, CreatedAt = Now, UpdatedAt = Now };
        int order = 0;
        foreach (var issue in issues)
        {
            list.Items.Add(new ReadingListItem { IssueId = issue.Id, SortOrder = order++ });
        }

        _context.ReadingLists.Add(list);
        _context.SaveChanges();
        return list;
    }

    // ----- Shared-character suggestions -----

    [Fact]
    public void Suggestions_OfferASeriesSharingThreeCharactersWithAMember_AndSayWhichAndWithWhom()
    {
        var member = AddSeries("Amazing Spider-Man");
        var candidate = AddSeries("Venom");
        var cameo = AddSeries("Daredevil");
        var continuity = AddContinuity("Earth-616", member);
        Appear(member, "Spider-Man", "Venom", "Carnage", "Mary Jane");
        Appear(candidate, "Spider-Man", "Venom", "Carnage");
        Appear(cameo, "Spider-Man");

        var suggestion = Assert.Single(ContinuityCharacterSuggestionResolver.GetSuggestions(_context));

        Assert.Equal(continuity.Id, suggestion.ContinuityId);
        Assert.Equal(candidate.Id, suggestion.SeriesId);
        Assert.Equal(3, suggestion.SharedCount);
        Assert.Equal("Shares Carnage, Spider-Man, Venom with Amazing Spider-Man", suggestion.Reason);
    }

    [Fact]
    public void Suggestions_SkipMembers_AndDismissedPairs()
    {
        var member = AddSeries("Amazing Spider-Man");
        var alsoMember = AddSeries("Venom");
        var dismissed = AddSeries("Carnage");
        var continuity = AddContinuity("Earth-616", member, alsoMember);
        foreach (var series in new[] { member, alsoMember, dismissed })
        {
            Appear(series, "Spider-Man", "Venom", "Carnage");
        }

        HealthDismissals.Dismiss(_context, HealthDismissals.ContinuitySuggestion, $"{continuity.Id}:{dismissed.Id}");

        Assert.Empty(ContinuityCharacterSuggestionResolver.GetSuggestions(_context));
    }

    [Fact]
    public void OtherContinuityLines_NameTheOtherContinuitiesASeriesIsIn()
    {
        var saga = AddSeries("Saga");
        var lone = AddSeries("Lone");
        var here = AddContinuity("Here", saga);
        AddContinuity("Elsewhere", saga);
        AddContinuity("Another", saga);

        var lines = ContinuityCharacterSuggestionResolver.OtherContinuityLines(_context, [saga.Id, lone.Id], here.Id);

        Assert.Equal("Also in: Another, Elsewhere", lines[saga.Id]);
        Assert.False(lines.ContainsKey(lone.Id));
    }

    // ----- Chronology check -----

    [Fact]
    public void Chronology_FlagsEntriesWhoseEventsAreTheWrongWayRound_AndReorderFixesOnlyThose()
    {
        var series = AddSeries("Crisis", issues: 4);
        var first = AddEvent("First Crisis", series.Issues[0]);
        var second = AddEvent("Second Crisis", series.Issues[1]);
        Relate(first, second, RelationType.Prequel);                       // First comes before Second
        // The list has the Second Crisis issue before the First Crisis one; issues 3 and 4 belong to no event.
        var list = AddList("Mixed up", series.Issues[2], series.Issues[1], series.Issues[3], series.Issues[0]);

        var result = ReadingListChronologyCheck.Check(_context, list.Id);

        Assert.NotNull(result);
        Assert.Equal(2, result!.RankedCount);
        Assert.Equal(1, result.OutOfOrderCount);
        var inversion = Assert.Single(result.Inversions);
        Assert.Equal("Crisis #2", inversion.First);
        Assert.Equal("Second Crisis", inversion.FirstEvent);
        Assert.Equal("Crisis #1", inversion.Second);
        Assert.True(result.CanReorder);

        Assert.True(ReadingListChronologyCheck.ReorderToChronology(_context, list.Id));

        var order = _context.ReadingListItems.AsNoTracking().Where(i => i.ReadingListId == list.Id).OrderBy(i => i.SortOrder).Select(i => i.IssueId).ToList();
        Assert.Equal([series.Issues[2].Id, series.Issues[0].Id, series.Issues[3].Id, series.Issues[1].Id], order);     // unranked entries did not move
        Assert.False(ReadingListChronologyCheck.Check(_context, list.Id)!.HasFindings);
    }

    [Fact]
    public void Chronology_EventsWithNoDirectionalRelation_SayNothingAboutEachOther()
    {
        var series = AddSeries("Anthology", issues: 2);
        var a = AddEvent("A", series.Issues[0]);
        var b = AddEvent("B", series.Issues[1]);
        Relate(a, b, RelationType.Crossover);
        var list = AddList("Either way", series.Issues[1], series.Issues[0]);

        var result = ReadingListChronologyCheck.Check(_context, list.Id);

        Assert.False(result!.HasFindings);
        Assert.Equal(0, result.RankedCount);
    }

    [Fact]
    public void Chronology_ReportsALoop_AndOffersNoReorder()
    {
        var series = AddSeries("Loop", issues: 2);
        var a = AddEvent("Alpha", series.Issues[0]);
        var b = AddEvent("Beta", series.Issues[1]);
        Relate(a, b, RelationType.Prequel);
        Relate(b, a, RelationType.Prequel);
        var list = AddList("Looped", series.Issues[0], series.Issues[1]);

        var result = ReadingListChronologyCheck.Check(_context, list.Id);

        Assert.Equal("Alpha → Beta → Alpha", Assert.Single(result!.Loops));
        Assert.True(result.HasFindings);
        Assert.False(result.CanReorder);
        Assert.False(ReadingListChronologyCheck.ReorderToChronology(_context, list.Id));
    }

    [Fact]
    public void Chronology_IsSkipped_ForPublicationOrderLists_AndListsMarkedAsIntended()
    {
        var series = AddSeries("Crisis", issues: 2);
        var first = AddEvent("First", series.Issues[0]);
        var second = AddEvent("Second", series.Issues[1]);
        Relate(first, second, RelationType.Prequel);
        var publication = AddList("Publication", series.Issues[1], series.Issues[0]);
        publication.ContinuityOrderKind = ContinuityOrderKind.PublicationOrder;
        var intended = AddList("Intended", series.Issues[1], series.Issues[0]);
        _context.SaveChanges();
        HealthDismissals.Dismiss(_context, HealthDismissals.ListOrder, ReadingListChronologyCheck.DismissalKey(intended.Id));

        Assert.Null(ReadingListChronologyCheck.Check(_context, publication.Id));
        Assert.Null(ReadingListChronologyCheck.Check(_context, intended.Id));
    }

    [Fact]
    public void FindCycles_ListsEachLoopOnce_AndIgnoresRelationsWithNoDirection()
    {
        var relations = new (int, int, RelationType)[]
        {
            (1, 2, RelationType.Prequel), (2, 3, RelationType.Prequel), (3, 1, RelationType.Prequel),
            (4, 5, RelationType.Sequel), (5, 4, RelationType.Crossover),
        };

        var cycles = EventChronology.FindCycles([1, 2, 3, 4, 5], relations);

        Assert.Equal([1, 2, 3], Assert.Single(cycles));
    }

    // ----- Series status event -----

    [Fact]
    public void ChangingAnExistingSeriesStatus_LogsItAndRaisesTheEventOnce_ButCreatingASeriesWithOneDoesNot()
    {
        // The context raises on the app-wide hub, which other test classes running in parallel share - so listen for this series' own
        // unique name, not for an id another test's database could also hand out.
        string name = $"Status test {Guid.NewGuid():N}";
        var raised = new List<SeriesStatusChangedEvent>();
        void Handler(SeriesStatusChangedEvent e)
        {
            if (e.SeriesName == name)
            {
                lock (raised)
                {
                    raised.Add(e);
                }
            }
        }

        LibraryEvents.Default.SeriesStatusChanged += Handler;
        try
        {
            var series = new Series { Name = name, Status = SeriesStatus.Ongoing };     // created with a status: not a change
            _context.Series.Add(series);
            _context.SaveChanges();
            Assert.Empty(raised);

            series.Status = SeriesStatus.Completed;
            _context.SaveChanges();
            series.Status = SeriesStatus.Completed;                                         // unchanged: nothing
            series.Summary = "edited";
            _context.SaveChanges();

            var change = Assert.Single(raised, e => e.SeriesId == series.Id);
            Assert.Equal(SeriesStatus.Ongoing, change.OldStatus);
            Assert.Equal(SeriesStatus.Completed, change.NewStatus);
            var logged = Assert.Single(_context.SeriesActivityEvents.Where(a => a.SeriesId == series.Id && a.Kind == SeriesActivityEventKind.StatusChanged).ToList());
            Assert.Equal("Status changed from Ongoing to Completed", logged.Detail);
        }
        finally
        {
            LibraryEvents.Default.SeriesStatusChanged -= Handler;
        }
    }

    // ----- Completion -----

    [Fact]
    public void FinishingTheLastUnreadIssue_CompletesTheContinuityOnce_AndNewIssuesReArmIt()
    {
        var done = AddSeries("Done", issues: 2, read: true);
        var almost = AddSeries("Almost", issues: 2, read: true);
        var last = almost.Issues[1];
        last.LastPageRead = null;
        _context.SaveChanges();
        var continuity = AddContinuity("Earth-616", done, almost);

        // Not complete yet: finishing an issue of Done does nothing.
        Assert.Empty(CollectionCompletion.OnIssueFinished(_context, done.Issues[0].Id, Now));

        // The finish is announced before the position save lands, so the row still says unread here.
        var completed = Assert.Single(CollectionCompletion.OnIssueFinished(_context, last.Id, Now));
        Assert.Equal(CompletedCollectionKind.Continuity, completed.Kind);
        Assert.Equal(continuity.Id, completed.Id);
        Assert.Equal(4, completed.IssueCount);

        last.LastPageRead = 19;
        _context.SaveChanges();
        Assert.Empty(CollectionCompletion.OnIssueFinished(_context, last.Id, Now));     // a re-read does not announce it again

        // A scan brings a new issue: the continuity is unfinished again, and finishing that issue completes it again.
        var fresh = new Issue { SeriesId = almost.Id, Number = "3", PageCount = 20, FilePath = "C:\\x\\Almost-3.cbz" };
        _context.Issues.Add(fresh);
        _context.SaveChanges();
        Assert.Equal(1, CollectionCompletion.Rearm(_context));
        Assert.Null(_context.Continuities.AsNoTracking().Single(c => c.Id == continuity.Id).CompletedNotifiedAt);
        Assert.Single(CollectionCompletion.OnIssueFinished(_context, fresh.Id, Now));
    }

    [Fact]
    public void FinishingAStoryEvent_IsAnnouncedAsAStoryEvent()
    {
        var series = AddSeries("Crisis", issues: 3, read: true);
        series.Issues[2].LastPageRead = null;
        _context.SaveChanges();
        var storyEvent = AddEvent("First Crisis", series.Issues[0], series.Issues[2]);

        var completed = Assert.Single(CollectionCompletion.OnIssueFinished(_context, series.Issues[2].Id, Now));

        Assert.Equal(CompletedCollectionKind.StoryEvent, completed.Kind);
        Assert.Equal(storyEvent.Id, completed.Id);
        Assert.Equal("First Crisis", completed.Name);
        Assert.Equal(2, completed.IssueCount);
        Assert.NotNull(_context.StoryEvents.AsNoTracking().Single(e => e.Id == storyEvent.Id).CompletedNotifiedAt);
    }

    [Fact]
    public void AddingAnUnreadSeries_ToAFinishedContinuity_ReArmsIt_ButAFullyReadOneDoesNot()
    {
        var done = AddSeries("Done", issues: 1, read: true);
        var continuity = AddContinuity("Small", done);
        continuity.CompletedNotifiedAt = Now;
        _context.SaveChanges();

        ContinuityResolver.AddSeriesToContinuity(_context, AddSeries("Also read", issues: 1, read: true).Id, continuity.Id);
        Assert.NotNull(_context.Continuities.AsNoTracking().Single(c => c.Id == continuity.Id).CompletedNotifiedAt);

        ContinuityResolver.AddSeriesToContinuity(_context, AddSeries("Unread", issues: 1).Id, continuity.Id);
        Assert.Null(_context.Continuities.AsNoTracking().Single(c => c.Id == continuity.Id).CompletedNotifiedAt);
    }
}
