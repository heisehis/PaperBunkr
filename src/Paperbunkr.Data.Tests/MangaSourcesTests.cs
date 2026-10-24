using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.Data.Tests;

/// <summary>Provider links, locks and candidates for a manga series (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §11, acceptance E).</summary>
public class MangaSourcesTests : IDisposable
{
    private readonly MangaTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private sealed class FakeProvider : IMetadataProvider
    {
        public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.MangaBaka;

        public ExternalMediaMetadata? Result { get; set; }

        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>(Array.Empty<MetadataSearchResult>());

        public Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken) => Task.FromResult(Result);
    }

    private static ExternalMediaMetadata Metadata(string? description = "Provider summary", string? creator = "Provider Author, Provider Artist", string? genre = "Drama", string? status = "Completed",
        IReadOnlyList<string>? genreTags = null, IReadOnlyList<(string, string)>? otherTags = null) =>
        new("42", "Test Manga", "https://example.test/42", description, status, 10, 2, Genre: genre, Creator: creator, GenreTags: genreTags, OtherTags: otherTags);

    private async Task Link(int seriesId, FakeProvider provider)
    {
        using var context = _db.CreateContext();
        Assert.True(await MetadataLinkResolver.LinkAsync(provider, context, seriesId, "42", CancellationToken.None));
    }

    private MangaSeriesMetadataService Service() => new(_db.Factory);

    private int Confirmed(int chapters = 4, Action<MangaSeriesEdit>? change = null)
    {
        int id = _db.SeedSeries(chapters);
        var edit = Service().Load(id)!;
        Service().Save(edit);
        if (change is not null)
        {
            edit = Service().Load(id)!;
            change(edit);
            Service().Save(edit);
        }

        return id;
    }

    [Fact]
    public async Task ALockedSummary_BecomesACandidate_AndTheSeriesKeepsItsOwn()
    {
        int id = Confirmed(change: e => e.Summary = "My own summary");
        await Link(id, new FakeProvider { Result = Metadata() });

        using var context = _db.CreateContext();
        Assert.Equal("My own summary", context.Series.Single().Summary);
        var pending = Assert.Single(context.MetadataProposals.Where(p => p.Field == MetadataProposalField.Summary && p.Status == MetadataProposalStatus.Pending));
        Assert.Equal("Provider summary", pending.ProposedValue);
        Assert.Equal(ExternalMetadataProvider.MangaBaka, pending.ProviderKey);
    }

    [Fact]
    public async Task AnUnlockedField_IsStillAppliedAutomatically_AsBefore()
    {
        int id = Confirmed();
        await Link(id, new FakeProvider { Result = Metadata() });

        using var context = _db.CreateContext();
        Assert.Equal("Provider summary", context.Series.Single().Summary);
        Assert.Equal(SeriesStatus.Completed, context.Series.Single().Status);
    }

    [Fact]
    public async Task ARefreshDoesNotStoreTheSameCandidateTwice()
    {
        int id = Confirmed(change: e => e.Summary = "My own summary");
        var provider = new FakeProvider { Result = Metadata() };

        await Link(id, provider);
        await Link(id, provider);

        using var context = _db.CreateContext();
        Assert.Equal(1, context.MetadataProposals.Count(p => p.Field == MetadataProposalField.Summary && p.Status == MetadataProposalStatus.Pending));
    }

    [Fact]
    public async Task ProviderCredits_AreNeverAppliedOnTheirOwn_ForAConfirmedMangaSeries()
    {
        int id = Confirmed();
        await Link(id, new FakeProvider { Result = Metadata() });

        using var context = _db.CreateContext();
        Assert.Equal(new[] { "A" }, context.SeriesCredits.Where(c => c.Role == "Writer").Select(c => c.Name).ToArray());
        Assert.Single(context.MetadataProposals.Where(p => p.Field == MetadataProposalField.Creator && p.Status == MetadataProposalStatus.Pending));
    }

    [Fact]
    public async Task ProviderTags_GoOntoTheSeries_AndDownToChaptersWithoutAnOverride()
    {
        int id = Confirmed(5);
        using (var context = _db.CreateContext())
        {
            var chapter = context.Issues.Include(i => i.Tags).Include(i => i.FieldOverrides).OrderBy(i => i.Id).First();
            chapter.Tags.Clear();
            chapter.Tags.Add(new IssueTag { Field = IssueTagField.Genre, Value = "Mine", Category = "Genre" });
            chapter.FieldOverrides.Add(new IssueFieldOverride { FieldKey = "Genre" });
            context.SaveChanges();
        }

        await Link(id, new FakeProvider { Result = Metadata(genreTags: new[] { "Fantasy", "Adventure" }, otherTags: new[] { ("Isekai", "Theme") }) });

        using var verify = _db.CreateContext();
        var series = verify.Series.Include(s => s.SeriesTags).Single();
        // Additive: the series keeps the "Action" it already had and gains the provider's genres.
        Assert.Equal(new[] { "Action", "Adventure", "Fantasy" }, series.SeriesTags.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value).OrderBy(v => v).ToArray());
        var chapters = verify.Issues.Include(i => i.Tags).OrderBy(i => i.Id).ToList();
        Assert.Equal(new[] { "Mine" }, chapters[0].Tags.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value).ToArray());
        Assert.All(chapters.Skip(1), c => Assert.Equal(new[] { "Action", "Adventure", "Fantasy" }, c.Tags.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value).OrderBy(v => v).ToArray()));
        Assert.All(chapters.Skip(1), c => Assert.Contains(c.Tags, t => t.Field == IssueTagField.Tags && t.Value == "Isekai"));
        Assert.All(chapters.Skip(1), c => Assert.True(c.EmbeddedDirty));
    }

    [Fact]
    public async Task LockedGenres_AreNotTouchedByProviderTags()
    {
        int id = Confirmed(3, e =>
        {
            e.Values.Genres.Clear();
            e.Values.Genres.Add(new MangaTagValue("Sports", "Genre", IssueTagWeight.Unset));
        });

        await Link(id, new FakeProvider { Result = Metadata(genreTags: new[] { "Fantasy" }) });

        using var context = _db.CreateContext();
        Assert.Equal(new[] { "Sports" }, context.SeriesTagRows.Where(t => t.Field == IssueTagField.Genre).Select(t => t.Value).ToArray());
        Assert.All(context.IssueTags.Where(t => t.Field == IssueTagField.Genre), t => Assert.Equal("Sports", t.Value));
    }

    [Fact]
    public async Task AComicSeries_IsLinkedExactlyAsBefore()
    {
        int id = _db.SeedSeries(3, type: ContentType.Comic);
        await Link(id, new FakeProvider { Result = Metadata(genreTags: new[] { "Fantasy" }) });

        using var context = _db.CreateContext();
        Assert.Equal("Provider summary", context.Series.Single().Summary);
        Assert.Equal("Provider Author, Provider Artist", context.Series.Single().Creator);
        Assert.Empty(context.MetadataProposals.Where(p => p.Status == MetadataProposalStatus.Pending));
        Assert.Empty(context.SeriesTagRows);
        Assert.All(context.Issues.Include(i => i.Tags), i => Assert.Contains(i.Tags, t => t.Value == "Fantasy"));
    }

    // ---------- candidates ----------

    private MangaSourceCandidates Candidates() => new(_db.Factory, Service());

    [Fact]
    public async Task TheCandidateList_ShowsWhatTheProviderOffered_NextToWhatTheSeriesHolds()
    {
        int id = Confirmed(change: e => e.Summary = "My own summary");
        await Link(id, new FakeProvider { Result = Metadata() });

        var list = Candidates().List(id);

        var summary = Assert.Single(list, c => c.Field == MetadataProposalField.Summary);
        Assert.Equal("My own summary", summary.Current);
        Assert.Equal("Provider summary", summary.Proposed);
        Assert.True(summary.IsLocked);
        var creator = Assert.Single(list, c => c.Field == MetadataProposalField.Creator);
        Assert.True(creator.NeedsRole);
        Assert.Equal(ExternalMetadataProvider.MangaBaka, summary.Provider);
        Assert.Single(Candidates().Links(id));
    }

    [Fact]
    public async Task Accepting_AppliesThroughTheNormalPath_AndDoesNotLockTheField()
    {
        int id = Confirmed(change: e => e.Summary = "My own summary");
        await Link(id, new FakeProvider { Result = Metadata() });
        var candidate = Candidates().List(id).Single(c => c.Field == MetadataProposalField.Summary);

        var result = Candidates().Accept(candidate.ProposalId);

        using var context = _db.CreateContext();
        Assert.Equal("Provider summary", context.Series.Single().Summary);
        Assert.Equal(MetadataProposalStatus.Accepted, context.MetadataProposals.Single(p => p.Id == candidate.ProposalId).Status);
        Assert.Empty(Candidates().List(id).Where(c => c.Field == MetadataProposalField.Summary));
        // The accepted value came from the provider, so it stays updatable by a later refresh, unlike one the user typed.
        Assert.DoesNotContain("Summary", MangaFieldLocks.Parse(context.Series.Single().LockedFields).Where(k => k == "Summary").Skip(1));
        Assert.True(result.Saved);
    }

    [Fact]
    public async Task AcceptingACredit_PutsTheNamesUnderTheChosenRole_AndOnToTheChapters()
    {
        int id = Confirmed(4);
        await Link(id, new FakeProvider { Result = Metadata() });
        var candidate = Candidates().List(id).Single(c => c.Field == MetadataProposalField.Creator);

        var result = Candidates().Accept(candidate.ProposalId, creditRole: "Penciller");

        using var context = _db.CreateContext();
        Assert.Equal(new[] { "Provider Author", "Provider Artist" }, context.SeriesCredits.Where(c => c.Role == "Penciller").OrderBy(c => c.SortOrder).Select(c => c.Name).ToArray());
        Assert.All(context.Issues, i => Assert.Equal("Provider Author, Provider Artist", i.Penciller));
        Assert.All(context.Issues, i => Assert.Equal("A", i.Writer));
        Assert.Equal(4, result.ChangedIssueIds.Count);
    }

    [Fact]
    public async Task Rejecting_RemovesTheCandidate_AndChangesNothing()
    {
        int id = Confirmed(change: e => e.Summary = "My own summary");
        await Link(id, new FakeProvider { Result = Metadata() });
        var candidate = Candidates().List(id).Single(c => c.Field == MetadataProposalField.Summary);

        Candidates().Reject(candidate.ProposalId);

        using var context = _db.CreateContext();
        Assert.Equal("My own summary", context.Series.Single().Summary);
        Assert.Equal(MetadataProposalStatus.Rejected, context.MetadataProposals.Single(p => p.Id == candidate.ProposalId).Status);
        Assert.Empty(Candidates().List(id).Where(c => c.Field == MetadataProposalField.Summary));
    }

    [Fact]
    public async Task ACandidateThatAlreadyMatches_IsNotOffered()
    {
        int id = Confirmed(change: e => e.Summary = "Provider summary");
        await Link(id, new FakeProvider { Result = Metadata() });

        Assert.Empty(Candidates().List(id).Where(c => c.Field == MetadataProposalField.Summary));
    }
}
