using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>
/// <see cref="WikidataEventLinks"/> against a fake Wikidata and <see cref="EventConnectorSweep"/> end to end
/// (docs/superpowers/specs/2026-09-27-continuity-map-design.md §2, Q11/Q16/Q19). No network.
/// </summary>
public class EventConnectorTests : IdentityTestDb
{
    private sealed class FakeWikidata : IWikidataLookup
    {
        public Dictionary<string, WikidataEntity> Entities { get; } = new();

        public Dictionary<string, List<string>> Search { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<string>> ByComicVineId { get; } = new();

        public int Calls { get; private set; }

        public Task<IReadOnlyList<WikidataSearchResult>> SearchEntitiesAsync(string name, CancellationToken cancellationToken)
        {
            Calls++;
            var hits = Search.GetValueOrDefault(name) ?? new List<string>();
            return Task.FromResult<IReadOnlyList<WikidataSearchResult>>(hits.Select(q => new WikidataSearchResult(q, Entities[q].Label)).ToList());
        }

        public Task<WikidataEntity?> GetEntityAsync(string qid, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Entities.GetValueOrDefault(qid));
        }

        public Task<IReadOnlyList<string>> FindByComicVineIdAsync(string comicVineId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>(ByComicVineId.GetValueOrDefault(comicVineId) ?? new List<string>());
        }

        public void Add(string qid, string label, string type, string? publisher = null, string[]? follows = null, string[]? followedBy = null, string[]? cvIds = null) =>
            Entities[qid] = new WikidataEntity(qid, label, null, new[] { type }, null, null,
                publisher is null ? Array.Empty<string>() : new[] { publisher }, Array.Empty<string>(), follows, followedBy, cvIds);
    }

    /// <summary>Planet Hulk → World War Hulk as Wikidata really has it (checked 2026-09-27), placeholder "nothing" included.</summary>
    private static FakeWikidata RealHulkData()
    {
        var wd = new FakeWikidata();
        wd.Add("Q173496", "Marvel Comics", "Q1320047");
        wd.Add("Q154242", "nothing", "Q35120");
        wd.Add("Q2526264", "Planet Hulk", "Q47461344", "Q173496", follows: new[] { "Q154242" }, followedBy: new[] { "Q1048144", "Q154242" });
        wd.Add("Q1048144", "World War Hulk", "Q3297186", "Q173496", follows: new[] { "Q2526264" });
        wd.Search["Planet Hulk"] = new() { "Q2526264" };
        wd.Search["World War Hulk"] = new() { "Q1048144" };
        return wd;
    }

    private (int Planet, int War) SeedHulkEvents()
    {
        using (var context = NewContext())
        {
            // Unrelated issue numbers and no shared series: only Wikidata can connect these two.
            context.Series.Add(new Series { Name = "Incredible Hulk", Issues = { new Issue { Number = "92", Year = 2006, Month = 4, Publisher = "Marvel Comics" } } });
            context.Series.Add(new Series { Name = "World War Hulk", Issues = { new Issue { Number = "1", Year = 2007, Month = 6, Publisher = "Marvel" } } });
            context.SaveChanges();
        }

        using var read = NewContext();
        var hulk = read.Issues.Single(i => i.Number == "92");
        var war = read.Issues.Single(i => i.Number == "1");
        return (AddEvent("Planet Hulk", StoryEventOrigin.Provider, new[] { hulk }), AddEvent("World War Hulk", StoryEventOrigin.Provider, new[] { war }));
    }

    [Fact]
    public async Task Wikidata_FollowedBy_BecomesAPrequelRelation_PlaceholdersIgnored()
    {
        var (planet, war) = SeedHulkEvents();

        var summary = await EventConnectorSweep.RunAsync(Factory, RealHulkData(), 40, CancellationToken.None);

        Assert.Equal(2, summary.WikidataMatched);
        using var context = NewContext();
        var relation = Assert.Single(context.EventRelations.Include(r => r.Evidence));
        Assert.Equal((planet, war, RelationType.Prequel), (relation.SourceEventId, relation.TargetEventId, relation.RelationType));
        var evidence = Assert.Single(relation.Evidence);
        Assert.Equal(RelationEvidenceProvider.Wikidata, evidence.Provider);
        Assert.Equal("Q2526264", evidence.ProviderSourceId);
        Assert.Equal("Q2526264", context.StoryEvents.Find(planet)!.WikidataQid);
        Assert.NotNull(context.StoryEvents.Find(war)!.ChronologyCheckedAt);
    }

    [Fact]
    public async Task Wikidata_WrongTypeOrPublisher_IsNotMatched()
    {
        SeedHulkEvents();
        var wd = RealHulkData();
        wd.Add("Q2526264", "Planet Hulk", "Q11424" /* film */, "Q173496", followedBy: new[] { "Q1048144" });
        wd.Add("Q1048144", "World War Hulk", "Q3297186", "Q99999" /* another publisher */, follows: new[] { "Q2526264" });
        wd.Add("Q99999", "Some Other Press", "Q1320047");

        var summary = await EventConnectorSweep.RunAsync(Factory, wd, 40, CancellationToken.None);

        Assert.Equal(0, summary.WikidataMatched);
        using var context = NewContext();
        Assert.Empty(context.EventRelations);
    }

    [Fact]
    public async Task Wikidata_ComicVineIdMatch_BeatsTheNameSearch()
    {
        var (planet, _) = SeedHulkEvents();
        using (var context = NewContext())
        {
            context.StoryEvents.Find(planet)!.ComicVineArcId = "56000";
            context.SaveChanges();
        }

        var wd = RealHulkData();
        wd.Search.Remove("Planet Hulk");                         // the name search would find nothing
        wd.ByComicVineId["4045-56000"] = new() { "Q2526264" };

        await EventConnectorSweep.RunAsync(Factory, wd, 40, CancellationToken.None);

        using var check = NewContext();
        Assert.Equal("Q2526264", check.StoryEvents.Find(planet)!.WikidataQid);
        Assert.Single(check.EventRelations);
    }

    [Fact]
    public async Task Wikidata_IsNotAskedAgainWithinThirtyDays()
    {
        SeedHulkEvents();
        var wd = RealHulkData();
        await EventConnectorSweep.RunAsync(Factory, wd, 40, CancellationToken.None);
        int calls = wd.Calls;

        var summary = await EventConnectorSweep.RunAsync(Factory, wd, 40, CancellationToken.None);

        Assert.Equal(calls, wd.Calls);
        Assert.Equal(0, summary.WikidataChecked);
        using var context = NewContext();
        Assert.Single(context.EventRelations);                  // still there: not re-checked, not removed
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

        var first = await EventConnectorSweep.RunAsync(Factory, null, 40, CancellationToken.None);

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

        var second = await EventConnectorSweep.RunAsync(Factory, null, 40, CancellationToken.None);

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
        await EventConnectorSweep.RunAsync(Factory, null, 40, CancellationToken.None);

        using (var context = NewContext())
        {
            EventConnectorSweep.RemoveRelation(context, context.EventRelations.Single().Id);
        }

        await EventConnectorSweep.RunAsync(Factory, null, 40, CancellationToken.None);

        using var check = NewContext();
        Assert.Empty(check.EventRelations);
        Assert.Single(check.EventRelationDismissals);
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
