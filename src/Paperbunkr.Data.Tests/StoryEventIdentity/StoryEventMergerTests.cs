using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>Covers <see cref="StoryEventMerger"/> (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §2, Q5).</summary>
public class StoryEventMergerTests : IdentityTestDb
{
    [Fact]
    public void ProviderPair_KeepsTheBiggerEvent_TakesThePrefixFreeName_AndRemembersTheOther()
    {
        var hulk = AddSeries("Hulk", "92", "93", "94", "95");
        int cv = AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk, comicVineId: "4512");
        int metron = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk.Take(2), metronId: "77");

        using (var context = NewContext())
        {
            var result = StoryEventMerger.Merge(context, cv, metron);
            Assert.Equal(cv, result.SurvivorId);
            Assert.Equal("Planet Hulk", result.SurvivorName);
        }

        using (var context = NewContext())
        {
            var survivor = context.StoryEvents.Include(e => e.Aliases).Include(e => e.Members).Single();
            Assert.Equal("Planet Hulk", survivor.Name);
            Assert.Equal("4512", survivor.ComicVineArcId);
            Assert.Equal("77", survivor.MetronArcId);
            Assert.Equal(StoryEventOrigin.Provider, survivor.Origin);
            Assert.Contains(survivor.Aliases, a => a.Name == "Hulk: Planet Hulk" && a.Key == "hulkplanethulk");
            Assert.Equal(4, survivor.Members.Count);
            Assert.Null(survivor.IdentityMemberKey);
        }
    }

    [Fact]
    public void UserEvent_Survives_WithItsOwnName_EvenWhenSmaller()
    {
        var hulk = AddSeries("Hulk", "92", "93", "94");
        int provider = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk);
        int mine = AddEvent("My Planet Hulk run", StoryEventOrigin.User, hulk.Take(1));

        using var context = NewContext();
        var result = StoryEventMerger.Merge(context, provider, mine);

        Assert.Equal(mine, result.SurvivorId);
        var survivor = context.StoryEvents.Include(e => e.Aliases).Single();
        Assert.Equal("My Planet Hulk run", survivor.Name);
        Assert.Equal(StoryEventOrigin.User, survivor.Origin);
        Assert.Contains(survivor.Aliases, a => a.Name == "Planet Hulk");
    }

    [Fact]
    public void Members_SurvivorOrderKept_LoserOnlyIssuesSlottedByCoverDate_UndatedAppended()
    {
        var a = AddSeries("Hulk", ("1", 2006, 4), ("2", 2006, 6), ("3", 2006, 8));
        var b = AddSeries("Planet Hulk Tie-in", ("1", 2006, 5), ("2", null, null));
        int survivor = AddEvent("Planet Hulk", StoryEventOrigin.Provider, a);
        int loser = AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, new[] { a[0], b[1], b[0] });

        using (var context = NewContext())
        {
            StoryEventMerger.Merge(context, survivor, loser);
        }

        using (var context = NewContext())
        {
            var order = context.EventMemberships.Include(m => m.Issue).ThenInclude(i => i!.Series)
                .OrderBy(m => m.Position).Select(m => m.Issue!.Series!.Name + " #" + m.Issue.Number).ToList();
            Assert.Equal(new[] { "Hulk #1", "Planet Hulk Tie-in #1", "Hulk #2", "Hulk #3", "Planet Hulk Tie-in #2" }, order);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, context.EventMemberships.OrderBy(m => m.Position).Select(m => m.Position));
        }
    }

    [Fact]
    public void SharedIssue_KeepsTheStrongerRole()
    {
        var hulk = AddSeries("Hulk", "1", "2");
        int survivor = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk);                                   // Core
        int loser = AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk.Take(1), role: EventMembershipRole.Prologue);

        using (var context = NewContext())
        {
            StoryEventMerger.Merge(context, survivor, loser);
        }

        using var check = NewContext();
        Assert.Equal(EventMembershipRole.Prologue, check.EventMemberships.Single(m => m.IssueId == hulk[0].Id).Role);
        Assert.Equal(2, check.EventMemberships.Count());
    }

    [Fact]
    public void FieldsWiden_DescriptionLonger_DatesCovered()
    {
        var hulk = AddSeries("Hulk", "1", "2");
        int survivor = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk);
        int loser = AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk.Take(1));
        using (var context = NewContext())
        {
            var s = context.StoryEvents.Find(survivor)!;
            s.Description = "Short.";
            s.StartDate = new DateTime(2006, 4, 1);
            s.EndDate = new DateTime(2007, 1, 1);
            var l = context.StoryEvents.Find(loser)!;
            l.Description = "A much longer description of the arc.";
            l.StartDate = new DateTime(2006, 1, 1);
            l.EndDate = new DateTime(2007, 6, 1);
            context.SaveChanges();
            StoryEventMerger.Merge(context, survivor, loser);
        }

        using var check = NewContext();
        var merged = check.StoryEvents.Single();
        Assert.Equal("A much longer description of the arc.", merged.Description);
        Assert.Equal(new DateTime(2006, 1, 1), merged.StartDate);
        Assert.Equal(new DateTime(2007, 6, 1), merged.EndDate);
    }

    [Fact]
    public void References_ArePointedAtTheSurvivor_SelfAndDuplicateRelationsDropped()
    {
        var hulk = AddSeries("Hulk", "1", "2");
        var other = AddSeries("World War Hulk", "1");
        int survivor = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk);
        int loser = AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk.Take(1));
        int sequel = AddEvent("World War Hulk", StoryEventOrigin.Provider, other);
        int unrelated = AddEvent("Something else", StoryEventOrigin.User, other);
        int listId;
        using (var context = NewContext())
        {
            context.EventRelations.AddRange(
                new EventRelation { SourceEventId = loser, TargetEventId = survivor, RelationType = RelationType.Related },
                new EventRelation { SourceEventId = survivor, TargetEventId = sequel, RelationType = RelationType.Sequel },
                new EventRelation { SourceEventId = loser, TargetEventId = sequel, RelationType = RelationType.Sequel });
            context.EventSuggestionDismissals.AddRange(
                new EventSuggestionDismissal { StoryEventId = loser, IssueId = other[0].Id },
                new EventSuggestionDismissal { StoryEventId = survivor, IssueId = hulk[1].Id },
                new EventSuggestionDismissal { StoryEventId = loser, IssueId = hulk[1].Id });
            var list = new ReadingList { Name = "Planet Hulk order", StoryEventId = loser };
            context.ReadingLists.Add(list);
            context.StoryEventDuplicateDismissals.Add(new StoryEventDuplicateDismissal { LowerEventId = Math.Min(loser, unrelated), HigherEventId = Math.Max(loser, unrelated) });
            context.SaveChanges();
            listId = list.Id;

            StoryEventMerger.Merge(context, survivor, loser);
        }

        using var check = NewContext();
        var relation = Assert.Single(check.EventRelations);
        Assert.Equal((survivor, sequel, RelationType.Sequel), (relation.SourceEventId, relation.TargetEventId, relation.RelationType));
        Assert.Equal(new[] { hulk[1].Id, other[0].Id }.OrderBy(i => i),
            check.EventSuggestionDismissals.Where(d => d.StoryEventId == survivor).Select(d => d.IssueId).OrderBy(i => i));
        Assert.Equal(survivor, check.ReadingLists.Single(l => l.Id == listId).StoryEventId);
        Assert.Empty(check.StoryEventDuplicateDismissals);
        Assert.False(check.StoryEvents.Any(e => e.Id == loser));
    }
}
