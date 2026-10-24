using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>
/// <see cref="EventConnectorSweep"/> end to end (docs/superpowers/specs/2026-10-12-continuity-sources-design.md §3-§4): strong local
/// inferences, the Metron reading-list chain, the removal of what only Wikidata ever supported. No network.
/// </summary>
public class EventConnectorTests : IdentityTestDb
{
    private (int Planet, int War) SeedHulkEvents()
    {
        using (var context = NewContext())
        {
            // Unrelated issue numbers and no shared series: nothing local can connect these two.
            context.Series.Add(new Series { Name = "Incredible Hulk", Issues = { new Issue { Number = "92", Year = 2006, Month = 4, Publisher = "Marvel Comics" } } });
            context.Series.Add(new Series { Name = "World War Hulk", Issues = { new Issue { Number = "1", Year = 2007, Month = 6, Publisher = "Marvel" } } });
            context.SaveChanges();
        }

        using var read = NewContext();
        var hulk = read.Issues.Single(i => i.Number == "92");
        var war = read.Issues.Single(i => i.Number == "1");
        return (AddEvent("Planet Hulk", StoryEventOrigin.Provider, new[] { hulk }), AddEvent("World War Hulk", StoryEventOrigin.Provider, new[] { war }));
    }

    private void AddMetronList(int eventId, string listId, string listName, string? previous = null, string? next = null)
    {
        using var context = NewContext();
        context.EventOrderEvidence.Add(new EventOrderEvidence
        {
            StoryEventId = eventId, Provider = EventOrderProvider.MetronList, SourceId = listId, SourceName = listName, PreviousName = previous, NextName = next,
        });
        context.SaveChanges();
    }

    [Fact]
    public async Task MetronChain_NextList_BecomesAPrequelRelation_WithTheListAsEvidence()
    {
        var (planet, war) = SeedHulkEvents();
        AddMetronList(planet, "77", "Planet Hulk", next: "World War Hulk");

        var summary = await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        Assert.Equal(1, summary.Created);
        using var context = NewContext();
        var relation = Assert.Single(context.EventRelations.Include(r => r.Evidence));
        Assert.Equal((planet, war, RelationType.Prequel), (relation.SourceEventId, relation.TargetEventId, relation.RelationType));
        var evidence = Assert.Single(relation.Evidence);
        Assert.Equal(RelationEvidenceProvider.Metron, evidence.Provider);
        Assert.Equal("77", evidence.ProviderSourceId);
        Assert.True(EventConnectorSweep.IsAutomatic(relation));
    }

    [Fact]
    public async Task MetronChain_PreviousList_PointsTheOtherWay_AndTheSamePairFromBothEndsIsOneRelation()
    {
        var (planet, war) = SeedHulkEvents();
        AddMetronList(war, "78", "World War Hulk", previous: "Planet Hulk");
        AddMetronList(planet, "77", "Planet Hulk", next: "World War Hulk");

        await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);
        var second = await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        using var context = NewContext();
        var relation = Assert.Single(context.EventRelations);
        Assert.Equal((planet, war), (relation.SourceEventId, relation.TargetEventId));
        Assert.Equal(0, second.Created);
    }

    [Fact]
    public async Task MetronChain_NeighbourThatMatchesNoEventOrSeveral_IsSkipped()
    {
        var (planet, _) = SeedHulkEvents();
        AddEvent("World War Hulk (2007)", StoryEventOrigin.User, AddSeries("WWH Prologue", "1"));      // a second event with the same name key
        AddMetronList(planet, "77", "Planet Hulk", previous: "Some Event We Do Not Have", next: "World War Hulk");

        await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        using var context = NewContext();
        Assert.Empty(context.EventRelations);
    }

    [Fact]
    public async Task MetronChain_RelationGoesWhenItsEvidenceDoes_AndYourOwnRelationBlocksIt()
    {
        var (planet, war) = SeedHulkEvents();
        AddMetronList(planet, "77", "Planet Hulk", next: "World War Hulk");
        await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        using (var context = NewContext())
        {
            context.EventOrderEvidence.RemoveRange(context.EventOrderEvidence);
            context.SaveChanges();
        }

        var removed = await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        Assert.Equal(1, removed.Removed);
        using (var context = NewContext())
        {
            Assert.Empty(context.EventRelations);
            EventRelationResolver.TryCreate(context, war, planet, RelationType.Prequel);        // yours, the other way round
        }

        AddMetronList(planet, "77", "Planet Hulk", next: "World War Hulk");
        await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        using var check = NewContext();
        var left = Assert.Single(check.EventRelations.Include(r => r.Evidence));
        Assert.Equal((war, planet), (left.SourceEventId, left.TargetEventId));
        Assert.False(EventConnectorSweep.IsAutomatic(left));
    }

    [Fact]
    public async Task RelationsOnlyWikidataSupported_AreRemoved_YoursAreNot()
    {
        var (planet, war) = SeedHulkEvents();
        int other = AddEvent("Other", StoryEventOrigin.User, AddSeries("Other", "1"));
        using (var context = NewContext())
        {
            context.EventRelations.Add(new EventRelation
            {
                SourceEventId = planet, TargetEventId = war, RelationType = RelationType.Prequel,
                Evidence = { new EventRelationEvidence { Provider = RelationEvidenceProvider.Wikidata, ProviderSourceId = "Q2526264", Confidence = 0.9m } },
            });
            EventRelationResolver.TryCreate(context, war, other, RelationType.Sequel);
        }

        var summary = await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        Assert.Equal(1, summary.Removed);
        using var check = NewContext();
        var left = Assert.Single(check.EventRelations);
        Assert.Equal((war, other), (left.SourceEventId, left.TargetEventId));
    }

    [Fact]
    public async Task StrongInference_IsSaved_AndRemovedWhenItsEvidenceGoes_YoursNeverAre()
    {
        var hulk = AddSeries("Hulk", ("105", 2007, 5), ("106", 2007, 6));
        int planet = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk.Take(1));
        int war = AddEvent("World War Hulk", StoryEventOrigin.Provider, hulk.Skip(1));
        var thor = AddSeries("Thor", ("1", 2008, 1));
        int t1 = AddEvent("Thor One", StoryEventOrigin.User, thor);
        int t2 = AddEvent("Thor Two", StoryEventOrigin.User, AddSeries("Thor Annual", ("1", 2009, 1)));
        using (var context = NewContext())
        {
            EventRelationResolver.TryCreate(context, t1, t2, RelationType.Prequel);     // yours
        }

        var first = await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        Assert.Equal(1, first.Created);
        using (var context = NewContext())
        {
            var inferred = context.EventRelations.Include(r => r.Evidence).Single(r => r.Evidence.Any(e => e.Provider == RelationEvidenceProvider.Inferred));
            Assert.Equal((war, planet, RelationType.Continuation), (inferred.SourceEventId, inferred.TargetEventId, inferred.RelationType));
            Assert.Equal("Continues straight on: Hulk #105 → #106", inferred.Evidence.Single().ProviderRelationType);

            // The evidence goes: World War Hulk loses its issue.
            context.EventMemberships.RemoveRange(context.EventMemberships.Where(m => m.StoryEventId == war));
            context.SaveChanges();
        }

        var second = await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        Assert.Equal(1, second.Removed);
        using var check = NewContext();
        var left = Assert.Single(check.EventRelations);
        Assert.Equal((t1, t2), (left.SourceEventId, left.TargetEventId));
    }

    [Fact]
    public async Task DeletingAnInferredRelation_DismissesThePair_SoItIsNeverRecreated()
    {
        var hulk = AddSeries("Hulk", ("105", 2007, 5), ("106", 2007, 6));
        AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk.Take(1));
        AddEvent("World War Hulk", StoryEventOrigin.Provider, hulk.Skip(1));
        await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        using (var context = NewContext())
        {
            EventConnectorSweep.RemoveRelation(context, context.EventRelations.Single().Id);
        }

        await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        using var check = NewContext();
        Assert.Empty(check.EventRelations);
        Assert.Single(check.EventRelationDismissals);
    }

    [Fact]
    public async Task DeletingAMetronRelation_DismissesThePair_TooSoItIsNotReadAgain()
    {
        var (planet, _) = SeedHulkEvents();
        AddMetronList(planet, "77", "Planet Hulk", next: "World War Hulk");
        await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);
        using (var context = NewContext())
        {
            EventConnectorSweep.RemoveRelation(context, context.EventRelations.Single().Id);
        }

        await EventConnectorSweep.RunAsync(Factory, CancellationToken.None);

        using var check = NewContext();
        Assert.Empty(check.EventRelations);
    }

    [Fact]
    public void RemovingYourOwnRelation_DoesNotDismiss()
    {
        int a = AddEvent("A", StoryEventOrigin.User, AddSeries("A", "1"));
        int b = AddEvent("B", StoryEventOrigin.User, AddSeries("B", "1"));
        using var context = NewContext();
        EventRelationResolver.TryCreate(context, a, b, RelationType.Sequel);

        EventConnectorSweep.RemoveRelation(context, context.EventRelations.Single().Id);

        Assert.Empty(context.EventRelationDismissals);
    }
}
