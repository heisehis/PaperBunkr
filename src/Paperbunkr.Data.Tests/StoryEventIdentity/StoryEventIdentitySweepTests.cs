using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using static Paperbunkr.Data.Tests.StoryEventIdentity.StoryEventIdCompletionTests;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>
/// The sweep end to end, and the "never re-create a merged duplicate" rules in <see cref="StoryEventResolver"/> and
/// <see cref="StoryArcGroupingResolver"/> (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §4, Q8/Q11).
/// </summary>
public class StoryEventIdentitySweepTests : IdentityTestDb
{
    [Fact]
    public async Task Sweep_CompletesIds_ThenMergesTheNowSharedArc()
    {
        var hulk = AddSeries("Hulk", "92", "93");
        var tieIn = AddSeries("Incredible Hulk Annual", "1");
        int cvEvent = AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk, comicVineId: "4512");
        int metronEvent = AddEvent("Planet Hulk", StoryEventOrigin.Provider, tieIn, metronId: "77");   // no shared issues: only the ids can prove it
        var metron = new FakeSource(ComicProvider.Metron);
        metron.CvIdByArc["77"] = "4512";
        var sources = new Dictionary<ComicProvider, IArcIdentitySource> { [ComicProvider.Metron] = metron, [ComicProvider.ComicVine] = new FakeSource(ComicProvider.ComicVine) };

        var summary = await StoryEventIdentitySweep.RunAsync(Factory, sources, null, StoryEventIdentitySweep.DefaultBatchSize, null, CancellationToken.None);

        var merge = Assert.Single(summary.Merges);
        Assert.Equal(cvEvent, merge.SurvivorId);
        Assert.Equal("Planet Hulk", merge.RemovedName);
        Assert.Contains("1 duplicate merged", summary.Text);
        using var context = NewContext();
        var survivor = context.StoryEvents.Include(e => e.Members).Single();
        Assert.Equal(("4512", "77"), (survivor.ComicVineArcId, survivor.MetronArcId));
        Assert.Equal(3, survivor.Members.Count);
        Assert.False(context.StoryEvents.Any(e => e.Id == metronEvent));
    }

    [Fact]
    public async Task Sweep_ScopedToOneEvent_LeavesUnrelatedSilentPairsAlone()
    {
        var a = AddSeries("Hulk", "1");
        var b = AddSeries("Thor", "1");
        int mine = AddEvent("Planet Hulk", StoryEventOrigin.Provider, a, comicVineId: "1");
        AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, a, comicVineId: "1");
        AddEvent("Ragnarok", StoryEventOrigin.Provider, b, comicVineId: "2");
        AddEvent("Thor: Ragnarok", StoryEventOrigin.Provider, b, comicVineId: "2");

        var summary = await StoryEventIdentitySweep.RunAsync(Factory, new Dictionary<ComicProvider, IArcIdentitySource>(), new[] { mine }, 10, null, CancellationToken.None);

        Assert.Single(summary.Merges);
        using var context = NewContext();
        Assert.Equal(3, context.StoryEvents.Count());
        Assert.Contains("ComicVine and Metron not configured", summary.Text);
    }

    [Fact]
    public void GetOrCreate_ReusesAnEvent_ThroughThePrefixFreeName()
    {
        var hulk = AddSeries("Hulk", "92");
        int existing = AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk);

        using var context = NewContext();
        var found = StoryEventResolver.GetOrCreate(context, "Planet Hulk", new[] { "Hulk" });

        Assert.Equal(existing, found.Id);
        Assert.Equal(1, context.StoryEvents.Count());
    }

    [Fact]
    public void GetOrCreate_ReusesAnEvent_ThroughAnAlias_AndMarksNewOnesProvider()
    {
        var hulk = AddSeries("Hulk", "92");
        int existing = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk);
        using (var context = NewContext())
        {
            context.StoryEventAliases.Add(new StoryEventAlias { StoryEventId = existing, Name = "Planet Hulk Saga", Key = EventNameKeys.Key("Planet Hulk Saga"), Source = StoryEventAliasSource.Merge });
            context.SaveChanges();
        }

        using var check = NewContext();
        Assert.Equal(existing, StoryEventResolver.GetOrCreate(check, "Planet Hulk Saga").Id);
        var created = StoryEventResolver.GetOrCreate(check, "World War Hulk");
        Assert.NotEqual(existing, created.Id);
        Assert.Equal(StoryEventOrigin.Provider, created.Origin);
    }

    [Fact]
    public void Grouping_DoesNotReProposeAMergedAwaySpelling()
    {
        var hulk = AddSeries("Hulk", "92", "93");
        using (var context = NewContext())
        {
            foreach (var issue in context.Issues)
            {
                issue.StoryArc = "Hulk: Planet Hulk";      // the ComicVine spelling on the issues
                issue.Publisher = "Marvel";
            }

            context.SaveChanges();
        }

        int cv = AddEvent("Hulk: Planet Hulk", StoryEventOrigin.Provider, hulk);
        int metron = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk);
        using (var context = NewContext())
        {
            StoryEventMerger.Merge(context, cv, metron);
        }

        using var check = NewContext();
        Assert.Equal("Planet Hulk", check.StoryEvents.Single().Name);
        Assert.Empty(StoryArcGroupingResolver.GetCandidates(check));
    }
}
