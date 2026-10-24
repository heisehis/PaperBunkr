using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests.ContinuityScreen;

/// <summary>
/// The App half of docs/superpowers/specs/2026-10-12-continuity-sources-design.md: "Check story events" with the new reading-order sources,
/// the map's event order from CBRO ranks, the designation on the continuity page. No network.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ContinuitySourcesAppTests : ContinuityScreenTestBase
{
    private sealed class FakeCbro : ICbroTimeline
    {
        public List<CbroTimelineEntry> Entries { get; } = new();

        public Task<IReadOnlyList<CbroTimelineEntry>> GetEntriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CbroTimelineEntry>>(Entries.ToList());
    }

    private sealed class FakeMetron : IMetronEventLists, IMetronUniverses
    {
        public Dictionary<string, MetronEventListInfo> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<int, MetronUniverseInfo> Universes { get; } = new();

        public Task<IReadOnlyList<MetronUniverseInfo>> SearchUniversesAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetronUniverseInfo>>(Universes.Values.Where(u => u.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || u.Designation?.Contains(query, StringComparison.OrdinalIgnoreCase) == true).ToList());

        public Task<MetronEventListInfo?> FindEventListAsync(string eventName, CancellationToken cancellationToken) =>
            Task.FromResult(Lists.GetValueOrDefault(eventName));

        public Task<MetronUniverseInfo?> GetUniverseAsync(int universeId, CancellationToken cancellationToken) =>
            Task.FromResult(Universes.GetValueOrDefault(universeId));
    }

    private static void MarkProviderEvents()
    {
        using var context = PaperbunkrDb.CreateContext();
        foreach (var e in context.StoryEvents)
        {
            e.Origin = StoryEventOrigin.Provider;
        }

        context.SaveChanges();
    }

    [Fact]
    public async Task CheckStoryEvents_AsksTheSources_LinksFromTheMetronChain_AndFillsTheDesignation()
    {
        int planetIssue = SeedIssue("Incredible Hulk", "92", year: 2006);
        int warIssue = SeedIssue("World War Hulk", "1", year: 2007);
        int planet = SeedEvent("Planet Hulk", null, null, planetIssue);
        int war = SeedEvent("World War Hulk", null, null, warIssue);
        MarkProviderEvents();
        int continuity = SeedContinuity("Earth-616");
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Continuities.Single(c => c.Id == continuity).MetronId = "42";
            context.SaveChanges();
        }

        var cbro = new FakeCbro();
        cbro.Entries.Add(new CbroTimelineEntry("marvel", 40, "Planet Hulk", 2006, null));
        var metron = new FakeMetron();
        metron.Lists["Planet Hulk"] = new MetronEventListInfo("77", "Planet Hulk", null, "World War Hulk");
        metron.Universes[42] = new MetronUniverseInfo(42, "Earth-616", "Earth-616", "Marvel");

        var result = await StoryEventChecks.RunDetailedAsync(
            new Dictionary<ComicProvider, IArcIdentitySource>(), new StoryEventChecks.EventOrderInputs(cbro, metron, metron), null, CancellationToken.None);

        Assert.Equal(1, result.Order.CbroMatched);
        Assert.Equal(1, result.Order.MetronMatched);
        Assert.Equal(1, result.UniversesUpdated);
        using var check = PaperbunkrDb.CreateContext();
        var relation = Assert.Single(check.EventRelations.Include(r => r.Evidence));
        Assert.Equal((planet, war, RelationEvidenceProvider.Metron), (relation.SourceEventId, relation.TargetEventId, relation.Evidence.Single().Provider));
        Assert.Equal(40, check.EventOrderEvidence.Single(e => e.Provider == EventOrderProvider.Cbro && e.StoryEventId == planet).Rank);
        Assert.Equal("Earth-616", check.Continuities.Single(c => c.Id == continuity).Designation);
        Assert.Contains("found in Metron reading lists", result.Text);
    }

    [Fact]
    public async Task CheckStoryEvents_WithNoSources_StillRunsTheLocalSteps()
    {
        SeedEvent("Alone", null, null, SeedIssue("Solo", "1", year: 2001));

        var result = await StoryEventChecks.RunDetailedAsync(
            new Dictionary<ComicProvider, IArcIdentitySource>(), StoryEventChecks.EventOrderInputs.None, null, CancellationToken.None);

        Assert.Equal(0, result.UniversesUpdated);
        Assert.Contains("reading orders up to date", result.Text);
    }

    [Fact]
    public void TheMap_PlacesRankedEventsInCbroOrder_EvenWhenTheirOwnDatesDisagree()
    {
        int s = SeedSeries("Avengers", ("1", 1995), ("2", 1994), ("3", 1996));
        int continuity = SeedContinuity("Earth-616", s);
        int[] issues;
        using (var context = PaperbunkrDb.CreateContext())
        {
            issues = context.Issues.Where(i => i.SeriesId == s).OrderBy(i => i.Id).Select(i => i.Id).ToArray();
        }

        int later = SeedEvent("Listed first, dated later", null, null, issues[0]);
        int earlier = SeedEvent("Listed second, dated earlier", null, null, issues[1]);
        int last = SeedEvent("Listed last", null, null, issues[2]);
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var (id, rank) in new[] { (later, 1), (earlier, 2), (last, 3) })
            {
                context.EventOrderEvidence.Add(new EventOrderEvidence { StoryEventId = id, Provider = EventOrderProvider.Cbro, SourceId = $"marvel:{rank}", ListKey = "marvel", Rank = rank });
            }

            context.SaveChanges();
        }

        using var read = PaperbunkrDb.CreateContext();
        var data = ContinuityMapLoader.LoadData(read, continuity)!;
        var source = ContinuityMapBuilder.Build(data, ContinuityMapOptions.Default);

        Assert.Equal(new[] { later, earlier, last }, source.Blocks.Where(b => b.EventId is not null).Select(b => b.EventId!.Value));
        Assert.All(data.Events, e => Assert.NotNull(e.SortKey));
    }

    [Fact]
    public void ContinuityPage_ShowsTheDesignationWhenMetronGaveOne()
    {
        int named = SeedContinuity("Earth-616");
        int plain = SeedContinuity("Handmade");
        using (var context = PaperbunkrDb.CreateContext())
        {
            context.Continuities.Single(c => c.Id == named).Designation = "Earth-616";
            context.Continuities.Single(c => c.Id == plain).Designation = string.Empty;       // asked Metron, it has none
            context.SaveChanges();
        }

        var screen = CreateScreen();
        screen.LoadContinuity(named);
        Assert.True(screen.ContinuityPage.HasDesignation);
        Assert.Equal("Earth-616", screen.ContinuityPage.Designation);

        screen.LoadContinuity(plain);
        Assert.False(screen.ContinuityPage.HasDesignation);
    }
}
