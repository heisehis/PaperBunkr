using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>Covers <see cref="StoryEventIdCompletion"/> against fake providers - no live network (docs/superpowers/specs/2026-09-27-story-event-resolver-design.md §3, Q14/Q16/Q17).</summary>
public class StoryEventIdCompletionTests : IdentityTestDb
{
    internal sealed class FakeSource : IArcIdentitySource
    {
        public FakeSource(ComicProvider provider) => Provider = provider;

        public ComicProvider Provider { get; }

        public Dictionary<int, List<ComicVineIdName>> IssueArcs { get; } = new();

        public Dictionary<string, string> CvIdByArc { get; } = new();

        public Dictionary<string, List<ArcSearchResult>> Search { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<ArcIssue>> ArcIssues { get; } = new();

        public bool Fail { get; set; }

        public List<string> Calls { get; } = new();

        public Task<IReadOnlyList<ComicVineIdName>> GetIssueArcsAsync(int providerIssueId, CancellationToken cancellationToken)
        {
            Calls.Add($"issue:{providerIssueId}");
            if (Fail) throw new ComicVineException("down");
            return Task.FromResult<IReadOnlyList<ComicVineIdName>>(IssueArcs.GetValueOrDefault(providerIssueId) ?? new List<ComicVineIdName>());
        }

        public Task<string?> GetComicVineIdForArcAsync(string arcId, CancellationToken cancellationToken)
        {
            Calls.Add($"cvid:{arcId}");
            if (Fail) throw new ComicVineException("down");
            return Task.FromResult(CvIdByArc.GetValueOrDefault(arcId));
        }

        public Task<IReadOnlyList<ArcSearchResult>> SearchArcsAsync(string query, CancellationToken cancellationToken)
        {
            Calls.Add($"search:{query}");
            if (Fail) throw new ReadingListSourceException(Provider.ToString(), "down");
            return Task.FromResult<IReadOnlyList<ArcSearchResult>>(Search.GetValueOrDefault(query) ?? new List<ArcSearchResult>());
        }

        public Task<IReadOnlyList<ArcIssue>> GetArcIssuesAsync(string arcId, CancellationToken cancellationToken)
        {
            Calls.Add($"arc:{arcId}");
            return Task.FromResult<IReadOnlyList<ArcIssue>>(ArcIssues.GetValueOrDefault(arcId) ?? new List<ArcIssue>());
        }
    }

    private static Dictionary<ComicProvider, IArcIdentitySource> Sources(params FakeSource[] sources) =>
        sources.ToDictionary(s => s.Provider, s => (IArcIdentitySource)s);

    private async Task<IdCompletionOutcome> Complete(int eventId, Dictionary<ComicProvider, IArcIdentitySource> sources, DateTime? now = null, bool force = false)
    {
        using var context = NewContext();
        return await StoryEventIdCompletion.CompleteAsync(context, eventId, sources, now ?? DateTime.UtcNow, CancellationToken.None, force);
    }

    private StoryEvent Load(int id)
    {
        using var context = NewContext();
        return context.StoryEvents.Include(e => e.Aliases).Single(e => e.Id == id);
    }

    [Fact]
    public async Task IssueArcCredits_FillTheId_AndTheOtherSpellingBecomesAnAlias()
    {
        var hulk = AddSeries("Hulk", "92", "93", "94");
        for (int i = 0; i < hulk.Count; i++) AttachIssueId(hulk[i], ComicProvider.ComicVine, (1000 + i).ToString());
        int id = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk, metronId: "77");
        var cv = new FakeSource(ComicProvider.ComicVine);
        for (int i = 0; i < hulk.Count; i++) cv.IssueArcs[1000 + i] = new() { new(4512, "Hulk: Planet Hulk"), new(9, "Unrelated Arc") };

        var outcome = await Complete(id, Sources(cv));

        Assert.Equal(IdCompletionStatus.Checked, outcome.Status);
        Assert.Equal(1, outcome.IdsFilled);
        var e = Load(id);
        Assert.Equal("4512", e.ComicVineArcId);
        Assert.Contains(e.Aliases, a => a.Name == "Hulk: Planet Hulk" && a.Source == StoryEventAliasSource.Provider);
        Assert.NotNull(e.IdentityCheckedAt);
        Assert.NotNull(e.IdentityMemberKey);
    }

    [Fact]
    public async Task MetronCvId_FillsTheComicVineId()
    {
        var hulk = AddSeries("Hulk", "92");
        int id = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk, metronId: "77");
        var metron = new FakeSource(ComicProvider.Metron);
        metron.CvIdByArc["77"] = "4512";

        await Complete(id, Sources(metron, new FakeSource(ComicProvider.ComicVine)));

        Assert.Equal("4512", Load(id).ComicVineArcId);
    }

    [Fact]
    public async Task NameSearch_NeedsHalfTheIssuesInTheArcList()
    {
        var hulk = AddSeries("Hulk", "92", "93", "94", "95");
        int good = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk, comicVineId: "4512");
        var metron = new FakeSource(ComicProvider.Metron);
        metron.Search["Planet Hulk"] = new() { new ArcSearchResult("50", "Planet Hulk", null, null, 0), new ArcSearchResult("77", "Planet Hulk", null, null, 0) };
        metron.ArcIssues["50"] = new() { new ArcIssue("Hulk (2006)", "1", 2006, null) };                                    // 0 of 4
        metron.ArcIssues["77"] = new() { new ArcIssue("Hulk", "92", 2006, null), new ArcIssue("Hulk", "93", 2006, null) };   // 2 of 4

        await Complete(good, Sources(metron));

        Assert.Equal("77", Load(good).MetronArcId);
    }

    [Fact]
    public async Task NameSearch_Miss_GoesToTheNegativeCache_AndIsNotRepeated()
    {
        var hulk = AddSeries("Hulk", "92");
        int id = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk);
        var metron = new FakeSource(ComicProvider.Metron);

        await Complete(id, Sources(metron));
        metron.Calls.Clear();
        await Complete(id, Sources(metron), force: true);

        Assert.DoesNotContain(metron.Calls, c => c.StartsWith("search:", StringComparison.Ordinal));
        using var context = NewContext();
        Assert.Contains(context.StoryEventVerificationNegativeCaches, c => c.ArcName == "Planet Hulk" && c.Source == ArcVerificationSource.Metron);
    }

    [Fact]
    public async Task StrongerSourceWins_AndAContradictedStoredIdIsAConflict_NeverOverwritten()
    {
        var hulk = AddSeries("Hulk", "92", "93");
        for (int i = 0; i < hulk.Count; i++) AttachIssueId(hulk[i], ComicProvider.ComicVine, (1000 + i).ToString());
        int id = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk, comicVineId: "6620");
        var cv = new FakeSource(ComicProvider.ComicVine);
        cv.IssueArcs[1000] = new() { new(4512, "Planet Hulk") };
        cv.IssueArcs[1001] = new() { new(4512, "Planet Hulk") };

        var outcome = await Complete(id, Sources(cv));

        var e = Load(id);
        Assert.Equal("6620", e.ComicVineArcId);
        Assert.Equal("ComicVine arc 6620 vs 4512", e.IdentityConflict);
        Assert.Equal("ComicVine arc 6620 vs 4512", outcome.Conflict);
    }

    [Fact]
    public async Task NotDue_WithinThirtyDays_UnlessTheMembersChanged()
    {
        var hulk = AddSeries("Hulk", "92", "93");
        int id = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk.Take(1));
        var metron = new FakeSource(ComicProvider.Metron);
        var now = DateTime.UtcNow;

        await Complete(id, Sources(metron), now);
        Assert.Equal(IdCompletionStatus.NotDue, (await Complete(id, Sources(metron), now.AddDays(10))).Status);

        using (var context = NewContext())
        {
            context.EventMemberships.Add(new EventMembership { StoryEventId = id, IssueId = hulk[1].Id, Position = 2 });
            context.SaveChanges();
        }

        Assert.Equal(IdCompletionStatus.Checked, (await Complete(id, Sources(metron), now.AddDays(10))).Status);
        Assert.Equal(IdCompletionStatus.Checked, (await Complete(id, Sources(metron), now.AddDays(45))).Status);
    }

    [Fact]
    public async Task NetworkError_LeavesTheEventUnstamped()
    {
        var hulk = AddSeries("Hulk", "92");
        AttachIssueId(hulk[0], ComicProvider.ComicVine, "1000");
        int id = AddEvent("Planet Hulk", StoryEventOrigin.Provider, hulk);
        var cv = new FakeSource(ComicProvider.ComicVine) { Fail = true };

        var outcome = await Complete(id, Sources(cv));

        Assert.Equal(IdCompletionStatus.Failed, outcome.Status);
        Assert.Null(Load(id).IdentityCheckedAt);
    }

    [Fact]
    public async Task NoConfiguredProviders_StampsWithoutCalling()
    {
        int id = AddEvent("Planet Hulk", StoryEventOrigin.Provider, AddSeries("Hulk", "92"));

        var outcome = await Complete(id, new Dictionary<ComicProvider, IArcIdentitySource>());

        Assert.Equal(IdCompletionStatus.Checked, outcome.Status);
        Assert.Equal(0, outcome.IdsFilled);
    }
}
