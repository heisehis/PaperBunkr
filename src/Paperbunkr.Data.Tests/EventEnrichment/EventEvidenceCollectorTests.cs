using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata.EventEnrichment;
using Paperbunkr.Data.ReadingLists.Sources;
using Paperbunkr.Data.Tests.StoryEventIdentity;

namespace Paperbunkr.Data.Tests.EventEnrichment;

public sealed class EventEvidenceCollectorTests : IdentityTestDb
{
    [Fact]
    public async Task ComicVineDeckBeatsEditionOnlyDescriptionWithoutWritingAnything()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, [], comicVineId: "1");
        var fake = new FakeSource(new("Collected Editions\nTimestorm 2009/2099", null, Deck: "Heroes of the present encounter their future counterparts."));
        using var db = NewContext();
        var proposal = await new EventEvidenceCollector(_ => fake).CollectAsync(db, id, CancellationToken.None);
        Assert.Equal("Heroes of the present encounter their future counterparts.", Assert.Single(proposal.Automatic.Where(e => e.Field == EventMetadataField.Description)).Value);
        Assert.Contains(proposal.Evidence, e => e.Field == EventMetadataField.Description && !e.Automatic);
        Assert.DoesNotContain(proposal.Evidence, e => e.Field == EventMetadataField.Tags);
        Assert.Null(db.StoryEvents.Single(e => e.Id == id).Description); Assert.Empty(db.EventMetadataChanges);
    }

    [Fact]
    public async Task ConflictingComicVineBridgeIsNotFetched()
    {
        int id = AddEvent("Event", StoryEventOrigin.User, [], comicVineId: "2", metronId: "1");
        var fake = new FakeSource(new("A valid overview of this event.", null, ComicVineId: "3"));
        int comicVineReads = 0;
        using var db = NewContext();
        var proposal = await new EventEvidenceCollector(provider => { if (provider == "ComicVine") comicVineReads++; return fake; }).CollectAsync(db, id, CancellationToken.None);
        Assert.Equal(0, comicVineReads); Assert.Contains(proposal.Sources, s => s.Status == "Needs comparison");
        Assert.DoesNotContain(proposal.Evidence, e => e.Provider == "ComicVine");
    }

    [Fact]
    public async Task AnOperationReusesSourceReadsAcrossEvents()
    {
        int first = AddEvent("One", StoryEventOrigin.User, [], metronId: "1"), second = AddEvent("Two", StoryEventOrigin.User, [], metronId: "1");
        var fake = new FakeSource(new("A valid overview of this event.", null)); var cache = new EventEvidenceCache();
        using (var db = NewContext()) await new EventEvidenceCollector(_ => fake, cache: cache).CollectAsync(db, first, CancellationToken.None);
        using (var db = NewContext()) await new EventEvidenceCollector(_ => fake, cache: cache).CollectAsync(db, second, CancellationToken.None);
        Assert.Equal(1, fake.OverviewReads); Assert.Equal(1, fake.IssueReads);
    }

    private sealed class FakeSource(ArcOverviewInfo info) : IReadingListSource
    {
        public int OverviewReads, IssueReads;
        public string SourceKey => "Fake"; public string DisplayName => SourceKey;
        public bool RequiresCredentials => false; public bool HasBrowsableCatalog => false;
        public Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken ct) => Task.FromResult<IReadOnlyList<ArcSearchResult>>([]);
        public Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string id, CancellationToken ct)
        { IssueReads++; return Task.FromResult<IReadOnlyList<ArcIssue>>([]); }
        public Task<ArcOverviewInfo?> GetArcOverviewAsync(string id, CancellationToken ct)
        { OverviewReads++; return Task.FromResult<ArcOverviewInfo?>(info); }
    }
}
