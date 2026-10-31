using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The full MangaBaka scrape (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §14): the provider reads every useful field including tag weights, and a refresh of a confirmed manga
/// series follows the three tiers (always applied / blank-only else candidate / suggested only).
/// </summary>
public class MangaFullScrapeTests : IDisposable
{
    private readonly MangaTestDb _db = new();

    public void Dispose() => _db.Dispose();

    // ---------- the provider reads the fields ----------

    private const string RealisticResponse = """
        {
          "status": 200,
          "data": {
            "id": 3321, "state": "active", "merged_with": null,
            "title": "Gokurakugai", "native_title": "極楽街", "romanized_title": "Gokurakugai",
            "canonical_url": "https://mangabaka.org/manga/3321/Gokurakugai",
            "description": "A bright district with a dark side.",
            "authors": ["Yuto Sano"], "artists": ["Yuto Sano", "Second Artist"],
            "year": 2022, "status": "releasing", "content_rating": "suggestive", "type": "manga", "original_language": "ja",
            "rating": 76.24, "final_volume": "6", "total_chapters": "38",
            "genres": ["action", "fantasy", "shounen"],
            "publishers": [
              { "name": "Shueisha", "language": "ja", "type": "Original" },
              { "name": "VIZ Media", "language": "en", "type": "English" },
              { "name": "Pika Édition", "language": "fr", "type": "Other" }
            ],
            "titles": [
              { "language": "en", "title": "Gokurakugai", "traits": ["official"], "is_primary": true },
              { "language": "en", "title": "The Paradise District", "traits": [], "is_primary": false },
              { "language": "ja", "title": "極楽街", "traits": ["native"], "is_primary": true }
            ],
            "tags_v2": [
              { "name": "Action", "name_path": "Themes > Action", "is_genre": true, "is_spoiler": false, "weight": "core" },
              { "name": "Gangs", "name_path": "Activities > Crimes > Organized Crime > Gangs", "is_genre": false, "is_spoiler": false, "weight": "defining" },
              { "name": "Slavery", "name_path": "Activities > Crimes > Slavery", "is_genre": false, "is_spoiler": false, "weight": "incidental" },
              { "name": "Yandere", "name_path": "Character Archetype > Dere Types > Yandere", "is_genre": false, "is_spoiler": true, "weight": "recurrent" },
              { "name": "Smoking", "name_path": "Activities > Daily Life > Smoking", "is_genre": false, "is_spoiler": false, "weight": "unweighted" }
            ]
          }
        }
        """;

    private static MangaBakaMetadataProvider RealProvider(string json) => new(new HttpClient(new StubHandler(json)));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StubHandler(string json) => _json = json;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_json, Encoding.UTF8, "application/json") });
    }

    [Fact]
    public async Task TheProvider_ReadsWeights_Publishers_Credits_Rating_AndTheRest()
    {
        var metadata = (await RealProvider(RealisticResponse).GetAsync("3321", CancellationToken.None))!;

        Assert.Equal(new[] { "Yuto Sano" }, metadata.Authors);
        Assert.Equal(new[] { "Yuto Sano", "Second Artist" }, metadata.Artists);
        Assert.Equal("suggestive", metadata.ContentRating);
        Assert.Equal("ja", metadata.OriginalLanguage);
        Assert.Equal(76.24, metadata.Rating!.Value, 2);
        Assert.Equal(new[] { "The Paradise District" }, metadata.AlternateTitles);
        Assert.Equal("Shueisha", metadata.Publishers!.Single(p => p.Type == "Original").Name);
        Assert.Equal(3, metadata.Publishers!.Count);

        var tags = metadata.WeightedTags!;
        Assert.Equal(IssueTagWeight.Core, tags.Single(t => t.Name == "Action").Weight);
        Assert.True(tags.Single(t => t.Name == "Action").IsGenre);
        Assert.Equal(IssueTagWeight.Defining, tags.Single(t => t.Name == "Gangs").Weight);
        Assert.Equal("Activities", tags.Single(t => t.Name == "Gangs").Category);
        Assert.Equal(IssueTagWeight.Incidental, tags.Single(t => t.Name == "Slavery").Weight);
        Assert.Equal(IssueTagWeight.Unset, tags.Single(t => t.Name == "Smoking").Weight);   // "unweighted"
        Assert.True(tags.Single(t => t.Name == "Yandere").IsSpoiler);
    }

    [Fact]
    public async Task AMergedSeries_PointsAtTheOneItWasMergedInto()
    {
        string merged = RealisticResponse.Replace("\"merged_with\": null", "\"merged_with\": 999");

        var metadata = (await RealProvider(merged).GetAsync("3321", CancellationToken.None))!;

        Assert.Equal("999", metadata.MergedWithId);
    }

    // ---------- a refresh of a confirmed series ----------

    private sealed class FakeProvider : IMetadataProvider
    {
        public ExternalMetadataProvider ProviderKey => ExternalMetadataProvider.MangaBaka;

        public Dictionary<string, ExternalMediaMetadata> ById { get; } = new();

        public List<string> Asked { get; } = new();

        public Task<IReadOnlyList<MetadataSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetadataSearchResult>>(Array.Empty<MetadataSearchResult>());

        public Task<ExternalMediaMetadata?> GetAsync(string externalId, CancellationToken cancellationToken)
        {
            Asked.Add(externalId);
            return Task.FromResult(ById.GetValueOrDefault(externalId));
        }
    }

    private static ExternalMediaMetadata Rich(
        string id = "42",
        string? description = "Provider summary",
        IReadOnlyList<string>? authors = null,
        IReadOnlyList<string>? artists = null,
        IReadOnlyList<ProviderPublisher>? publishers = null,
        string? contentRating = "safe",
        IReadOnlyList<ProviderTag>? tags = null,
        string? type = "manga",
        string? language = "ja",
        double? rating = 80.5,
        IReadOnlyList<string>? alternateTitles = null,
        string? mergedWith = null,
        int? ratingVotes = null) =>
        new(id, "Test Manga", "https://example.test/" + id, description, "Completed", 10, 2,
            Authors: authors ?? new[] { "Pat Author" }, Artists: artists ?? new[] { "Sam Artist" },
            Publishers: publishers ?? new[] { new ProviderPublisher("Shueisha", "ja", "Original"), new ProviderPublisher("VIZ Media", "en", "English") },
            ContentRating: contentRating, WeightedTags: tags, PublicationFormat: type, OriginalLanguage: language, Rating: rating,
            AlternateTitles: alternateTitles, MergedWithId: mergedWith, RatingVoteCount: ratingVotes);

    private static ProviderTag Tag(string name, IssueTagWeight weight, bool genre = false, bool spoiler = false, string category = "Themes") => new(name, category, weight, genre, spoiler);

    private async Task Refresh(int seriesId, FakeProvider provider, MangaApplyOptions? options = null, string externalId = "42")
    {
        using var context = _db.CreateContext();
        Assert.True(await MetadataLinkResolver.LinkAsync(provider, context, seriesId, externalId, CancellationToken.None, options));
    }

    private int Confirmed(int chapters = 4, Action<MangaSeriesEdit>? change = null, Action<int, Issue>? perChapter = null)
    {
        int id = _db.SeedSeries(chapters, perChapter);
        var service = new MangaSeriesMetadataService(_db.Factory);
        service.Save(service.Load(id)!);
        if (change is not null)
        {
            var edit = service.Load(id)!;
            change(edit);
            service.Save(edit);
        }

        return id;
    }

    private MangaSourceCandidates Candidates() => new(_db.Factory, new MangaSeriesMetadataService(_db.Factory));

    [Fact]
    public async Task TagsArriveWithTheProvidersWeights_OnTheSeries_AndOnEveryChapter()
    {
        int id = Confirmed(4);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(tags: new[]
        {
            Tag("Action", IssueTagWeight.Core, genre: true, category: "Themes"),
            Tag("Gangs", IssueTagWeight.Defining, category: "Activities"),
            Tag("Slavery", IssueTagWeight.Incidental, category: "Activities"),
            Tag("Smoking", IssueTagWeight.Unset, category: "Activities"),
        });

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        var series = context.Series.Include(s => s.SeriesTags).Single();
        Assert.Equal(IssueTagWeight.Core, series.SeriesTags.Single(t => t.Field == IssueTagField.Genre && t.Value == "Action").Weight);
        var gangs = series.SeriesTags.Single(t => t.Value == "Gangs");
        Assert.Equal(IssueTagField.Tags, gangs.Field);
        Assert.Equal("Activities", gangs.Category);
        Assert.Equal(IssueTagWeight.Defining, gangs.Weight);
        Assert.Equal(IssueTagWeight.Unset, series.SeriesTags.Single(t => t.Value == "Smoking").Weight);

        foreach (var chapter in context.Issues.Include(i => i.Tags).ToList())
        {
            Assert.Equal(IssueTagWeight.Core, chapter.Tags.Single(t => t.Value == "Action").Weight);
            Assert.Equal(IssueTagWeight.Defining, chapter.Tags.Single(t => t.Value == "Gangs").Weight);
            Assert.Equal(IssueTagWeight.Incidental, chapter.Tags.Single(t => t.Value == "Slavery").Weight);
            Assert.True(chapter.EmbeddedDirty);
        }
    }

    [Fact]
    public async Task ARefresh_FillsAnUnsetWeight_ButNeverReplacesOneThatIsSet_AndNeverRemovesATag()
    {
        int id = Confirmed(3, e =>
        {
            e.Values.Tags.Clear();
            e.Values.Tags.Add(new MangaTagValue("Gangs", "Activities", IssueTagWeight.Incidental));   // set by the user
            e.Values.Tags.Add(new MangaTagValue("Smoking", "Activities", IssueTagWeight.Unset));
            e.Values.Tags.Add(new MangaTagValue("My Own Tag", "Uncategorized", IssueTagWeight.Core));
        });
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(tags: new[]
        {
            Tag("Gangs", IssueTagWeight.Defining, category: "Activities"),
            Tag("Smoking", IssueTagWeight.Recurrent, category: "Activities"),
        });

        await Refresh(id, provider);

        // Tags was edited by hand so it is locked: the provider leaves it entirely alone.
        using var context = _db.CreateContext();
        var series = context.Series.Include(s => s.SeriesTags).Single();
        Assert.Equal(IssueTagWeight.Incidental, series.SeriesTags.Single(t => t.Value == "Gangs").Weight);
        Assert.Equal(IssueTagWeight.Unset, series.SeriesTags.Single(t => t.Value == "Smoking").Weight);
        Assert.Contains(series.SeriesTags, t => t.Value == "My Own Tag");
    }

    [Fact]
    public async Task OnAnUnlockedList_AnUnsetWeightIsFilled_ASetWeightKept_AndNothingIsRemoved()
    {
        int id = Confirmed(3);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(tags: new[] { Tag("Gangs", IssueTagWeight.Incidental, category: "Activities"), Tag("Smoking", IssueTagWeight.Unset, category: "Activities") });
        await Refresh(id, provider);

        // Someone sets Gangs to Core on the series without it counting as a hand edit of the whole list (a provider-sourced second refresh follows).
        using (var context = _db.CreateContext())
        {
            var gangs = context.SeriesTagRows.Single(t => t.Value == "Gangs");
            gangs.Weight = IssueTagWeight.Core;
            context.SaveChanges();
        }

        provider.ById["42"] = Rich(tags: new[] { Tag("Gangs", IssueTagWeight.Defining, category: "Activities"), Tag("Smoking", IssueTagWeight.Recurrent, category: "Activities"), Tag("Brand New", IssueTagWeight.Core, category: "Themes") });
        await Refresh(id, provider);

        using var verify = _db.CreateContext();
        var tags = verify.SeriesTagRows.Where(t => t.Field == IssueTagField.Tags).ToList();
        Assert.Equal(IssueTagWeight.Core, tags.Single(t => t.Value == "Gangs").Weight);          // set: kept
        Assert.Equal(IssueTagWeight.Recurrent, tags.Single(t => t.Value == "Smoking").Weight);   // was unset: filled
        Assert.Equal(IssueTagWeight.Core, tags.Single(t => t.Value == "Brand New").Weight);       // new: added with its weight
    }

    [Fact]
    public async Task SpoilerTags_AreSkippedByDefault_AndImportedWhenAsked()
    {
        int id = Confirmed(2);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(tags: new[] { Tag("Gangs", IssueTagWeight.Defining), Tag("Yandere", IssueTagWeight.Recurrent, spoiler: true) });

        await Refresh(id, provider);
        using (var context = _db.CreateContext())
        {
            Assert.DoesNotContain(context.SeriesTagRows, t => t.Value == "Yandere");
        }

        await Refresh(id, provider, new MangaApplyOptions(IncludeSpoilerTags: true));
        using var verify = _db.CreateContext();
        Assert.Equal(IssueTagWeight.Recurrent, verify.SeriesTagRows.Single(t => t.Value == "Yandere").Weight);
    }

    [Fact]
    public async Task AChapterOwnTags_AreLeftAlone_WhenItIsAnException()
    {
        int id = Confirmed(3);
        using (var context = _db.CreateContext())
        {
            var chapter = context.Issues.Include(i => i.FieldOverrides).OrderBy(i => i.Id).First();
            chapter.FieldOverrides.Add(new IssueFieldOverride { FieldKey = "Tags" });
            context.SaveChanges();
        }

        var provider = new FakeProvider();
        provider.ById["42"] = Rich(tags: new[] { Tag("Gangs", IssueTagWeight.Defining) });
        await Refresh(id, provider);

        using var verify = _db.CreateContext();
        var chapters = verify.Issues.Include(i => i.Tags).OrderBy(i => i.Id).ToList();
        Assert.DoesNotContain(chapters[0].Tags, t => t.Value == "Gangs");
        Assert.All(chapters.Skip(1), c => Assert.Contains(c.Tags, t => t.Value == "Gangs"));
    }

    // ---------- blank is filled, otherwise a candidate ----------

    [Fact]
    public async Task AuthorsAndArtists_FillBlankWriterAndPenciller_AndPassToChapters()
    {
        int id = Confirmed(3, perChapter: (n, issue) => issue.Writer = null);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich();

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        Assert.Equal(new[] { "Pat Author" }, context.SeriesCredits.Where(c => c.Role == "Writer").Select(c => c.Name).ToArray());
        Assert.Equal(new[] { "Sam Artist" }, context.SeriesCredits.Where(c => c.Role == "Penciller").Select(c => c.Name).ToArray());
        Assert.All(context.Issues, i =>
        {
            Assert.Equal("Pat Author", i.Writer);
            Assert.Equal("Sam Artist", i.Penciller);
        });
        Assert.Contains(context.CreatorCredits.Include(c => c.Creator), c => c.Role == "Writer" && c.Creator!.Name == "Pat Author");
        Assert.DoesNotContain(context.MetadataProposals, p => p.Status == MetadataProposalStatus.Pending && p.Field is MetadataProposalField.Writer or MetadataProposalField.Penciller);
    }

    [Fact]
    public async Task ADifferentExistingWriter_IsOfferedNotReplaced()
    {
        int id = Confirmed(3);   // chapters all credit writer "A"
        var provider = new FakeProvider();
        provider.ById["42"] = Rich();

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        Assert.All(context.Issues, i => Assert.Equal("A", i.Writer));
        var offer = Assert.Single(Candidates().List(id), c => c.Field == MetadataProposalField.Writer);
        Assert.Equal("A", offer.Current);
        Assert.Equal("Pat Author", offer.Proposed);
    }

    [Fact]
    public async Task TheOriginalPublisher_FillsABlankSeries_ButNotAnEnglishOne()
    {
        int id = Confirmed(3, perChapter: (n, issue) => issue.Publisher = null);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich();

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        Assert.Equal("Shueisha", context.Series.Single().Publisher);
        Assert.All(context.Issues, i => Assert.Equal("Shueisha", i.Publisher));
    }

    [Fact]
    public async Task ADifferentExistingPublisher_IsOffered()
    {
        int id = Confirmed(3);   // chapters say publisher "X"
        var provider = new FakeProvider();
        provider.ById["42"] = Rich();

        await Refresh(id, provider);

        var offer = Assert.Single(Candidates().List(id), c => c.Field == MetadataProposalField.Publisher);
        Assert.Equal("X", offer.Current);
        Assert.Equal("Shueisha", offer.Proposed);
        using var context = _db.CreateContext();
        Assert.Equal("X", context.Series.Single().Publisher);
    }

    [Theory]
    [InlineData("safe", null)]
    [InlineData("suggestive", "Teen")]
    [InlineData("erotica", "Mature 17+")]
    [InlineData("pornographic", "X18+")]
    public void TheContentRating_BecomesAnAgeRating_ExceptSafe(string contentRating, string? expected)
    {
        Assert.Equal(expected, MangaProviderApply.AgeRatingFrom(contentRating));
    }

    [Fact]
    public async Task AgeRating_FillsWhenBlank()
    {
        int id = Confirmed(3);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(contentRating: "erotica");

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        Assert.Equal("Mature 17+", context.Series.Single().AgeRating);
        Assert.All(context.Issues, i => Assert.Equal("Mature 17+", i.AgeRating));
    }

    [Fact]
    public async Task ALockedField_IsNeverFilledAndNeverReplaced_OnlyOffered()
    {
        int id = Confirmed(3, e => e.Values.Credits["Writer"] = new List<string>());   // emptied by hand, so locked
        var provider = new FakeProvider();
        provider.ById["42"] = Rich();

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        Assert.Empty(context.SeriesCredits.Where(c => c.Role == "Writer"));
        Assert.Contains(Candidates().List(id), c => c.Field == MetadataProposalField.Writer && c.IsLocked);
    }

    [Fact]
    public async Task ADismissedCandidate_IsNotOfferedAgain()
    {
        int id = Confirmed(3);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich();
        await Refresh(id, provider);
        var offer = Candidates().List(id).Single(c => c.Field == MetadataProposalField.Publisher);

        Candidates().Reject(offer.ProposalId);
        await Refresh(id, provider);

        Assert.DoesNotContain(Candidates().List(id), c => c.Field == MetadataProposalField.Publisher);
    }

    [Fact]
    public async Task AcceptingAWriterCandidate_ReplacesTheCredit_OnSeriesAndChapters()
    {
        int id = Confirmed(3);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich();
        await Refresh(id, provider);
        var offer = Candidates().List(id).Single(c => c.Field == MetadataProposalField.Writer);

        Candidates().Accept(offer.ProposalId);

        using var context = _db.CreateContext();
        Assert.Equal(new[] { "Pat Author" }, context.SeriesCredits.Where(c => c.Role == "Writer").Select(c => c.Name).ToArray());
        Assert.All(context.Issues, i => Assert.Equal("Pat Author", i.Writer));
    }

    // ---------- always applied ----------

    [Fact]
    public async Task TheProvidersRating_AndAlternateTitles_AreStored()
    {
        int id = Confirmed(2);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(rating: 76.24, alternateTitles: new[] { "The Paradise District", "Gokuraku District" });

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        var rating = context.ExternalRatings.Single();
        Assert.Equal(ExternalMetadataProvider.MangaBaka, rating.Provider);
        Assert.Equal(76.24m, rating.Value);
        Assert.Equal(100m, rating.Scale);
        Assert.Equal(new[] { "Gokuraku District", "The Paradise District" }, context.Set<SeriesTitle>().Where(t => t.Type == SeriesTitleType.Localized).Select(t => t.Value).OrderBy(v => v).ToArray());

        await Refresh(id, provider);   // a second refresh updates, never duplicates
        using var again = _db.CreateContext();
        Assert.Single(again.ExternalRatings);
    }

    [Fact]
    public async Task TheRatingsVoteCount_IsStored_AndKeptWhenALaterRefreshHasNone()
    {
        int id = Confirmed(2);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(rating: 76.24, ratingVotes: 1200);
        await Refresh(id, provider);

        using (var context = _db.CreateContext())
        {
            Assert.Equal(1200, context.ExternalRatings.Single().VoteCount);
        }

        provider.ById["42"] = Rich(rating: 70, ratingVotes: null);
        await Refresh(id, provider);

        using var again = _db.CreateContext();
        Assert.Equal(1200, again.ExternalRatings.Single().VoteCount);
        Assert.Equal(70m, again.ExternalRatings.Single().Value);
    }

    [Fact]
    public async Task ALockedStatus_IsNotChanged()
    {
        int id = Confirmed(2, e => e.Status = SeriesStatus.Ongoing);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich();

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        Assert.Equal(SeriesStatus.Ongoing, context.Series.Single().Status);
    }

    // ---------- reading mode is only ever suggested ----------

    [Theory]
    [InlineData("manhwa", "ko", ReadingMode.Webtoon)]
    [InlineData("manga", "ko", ReadingMode.Webtoon)]
    [InlineData("manga", "ja", ReadingMode.RightToLeft)]
    public void TheSuggestedReadingMode_FollowsTypeAndLanguage(string type, string language, ReadingMode expected)
    {
        var metadata = Rich(type: type, language: language);
        Assert.Equal(expected, MangaProviderApply.SuggestedReadingMode(metadata));
    }

    [Fact]
    public void WithNothingToGoOn_NoReadingModeIsSuggested()
    {
        Assert.Null(MangaProviderApply.SuggestedReadingMode(Rich(type: "novel", language: "en")));
    }

    [Fact]
    public async Task AReadingModeSuggestion_IsACandidate_NeverApplied_AndAcceptingItChangesTheSeries()
    {
        int id = Confirmed(3);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(type: "manhwa", language: "ko");

        await Refresh(id, provider);

        using (var context = _db.CreateContext())
        {
            Assert.Equal(ReadingMode.LeftToRight, context.Series.Single().ReadingMode);
        }

        var offer = Assert.Single(Candidates().List(id), c => c.Field == MetadataProposalField.ReadingMode);
        Assert.Equal("Webtoon", offer.Proposed);

        Candidates().Accept(offer.ProposalId);

        using var verify = _db.CreateContext();
        Assert.Equal(ReadingMode.Webtoon, verify.Series.Single().ReadingMode);
    }

    [Fact]
    public async Task ASuggestionMatchingTheCurrentMode_IsNotOffered()
    {
        int id = Confirmed(3);
        using (var context = _db.CreateContext())
        {
            context.Series.Single().ReadingMode = ReadingMode.RightToLeft;
            context.SaveChanges();
        }

        var provider = new FakeProvider();
        provider.ById["42"] = Rich(type: "manga", language: "ja");
        await Refresh(id, provider);

        Assert.DoesNotContain(Candidates().List(id), c => c.Field == MetadataProposalField.ReadingMode);
    }

    // ---------- merged series ----------

    [Fact]
    public async Task AMergedSeries_IsFollowedToTheLiveEntry_AndLinkedThere()
    {
        int id = Confirmed(2);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(id: "42", mergedWith: "99", description: "stale entry");
        provider.ById["99"] = Rich(id: "99", description: "The live entry");

        await Refresh(id, provider);

        Assert.Equal(new[] { "42", "99" }, provider.Asked);
        using var context = _db.CreateContext();
        Assert.Equal("99", context.ExternalMediaIds.Single().ExternalId);
        Assert.Equal("The live entry", context.Series.Single().Summary);
    }

    // ---------- other series are untouched ----------

    [Fact]
    public async Task AComicSeries_StillGetsTheOldBehaviour_AndNoCandidates()
    {
        int id = _db.SeedSeries(2, type: ContentType.Comic);
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(tags: new[] { Tag("Gangs", IssueTagWeight.Defining) });

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        Assert.Equal("Provider summary", context.Series.Single().Summary);
        Assert.Empty(context.SeriesCredits);
        Assert.Empty(context.SeriesTagRows);
        Assert.Empty(context.ExternalRatings);
    }

    [Fact]
    public async Task AnUnconfirmedMangaSeries_IsFilledToo_NotOnlyAConfirmedOne()
    {
        // The case that mattered: most series have never been opened in Edit Series, and a refresh used to fill only the summary, genres and titles.
        int id = _db.SeedSeries(3, (n, issue) => { issue.Publisher = null; issue.Writer = null; });
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(contentRating: "suggestive", tags: new[] { Tag("Gangs", IssueTagWeight.Defining, category: "Activities") });

        MangaApplyResult? report = null;
        using (var context = _db.CreateContext())
        {
            Assert.True(await MetadataLinkResolver.LinkAsync(provider, context, id, "42", CancellationToken.None, null, r => report = r));
        }

        using var verify = _db.CreateContext();
        var series = verify.Series.Single();
        Assert.Equal("Shueisha", series.Publisher);
        Assert.Equal("Teen", series.AgeRating);
        Assert.Equal("Provider summary", series.Summary);
        Assert.Equal(new[] { "Pat Author" }, verify.SeriesCredits.Where(c => c.Role == "Writer").Select(c => c.Name).ToArray());
        Assert.Equal(new[] { "Sam Artist" }, verify.SeriesCredits.Where(c => c.Role == "Penciller").Select(c => c.Name).ToArray());
        Assert.Single(verify.ExternalRatings);
        Assert.All(verify.Issues, i =>
        {
            Assert.Equal("Shueisha", i.Publisher);
            Assert.Equal("Pat Author", i.Writer);
            Assert.Equal("Sam Artist", i.Penciller);
            Assert.Equal("Teen", i.AgeRating);
        });
        // It stays unconfirmed: the user has not looked at it yet.
        Assert.Equal(0, series.CanonicalReviewVersion);
        Assert.NotNull(report);
        Assert.Contains("publisher Shueisha", report!.Describe());
    }

    [Fact]
    public async Task AnUnconfirmedSeries_KeepsWhatItsChaptersAlreadySay_AndOffersTheProvidersInstead()
    {
        int id = _db.SeedSeries(3);   // chapters say publisher "X", writer "A"
        var provider = new FakeProvider();
        provider.ById["42"] = Rich();

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        Assert.All(context.Issues, i =>
        {
            Assert.Equal("X", i.Publisher);
            Assert.Equal("A", i.Writer);
        });
        var offers = Candidates().List(id);
        Assert.Contains(offers, c => c.Field == MetadataProposalField.Publisher && c.Current == "X" && c.Proposed == "Shueisha");
        Assert.Contains(offers, c => c.Field == MetadataProposalField.Writer && c.Proposed == "Pat Author");
    }

    [Fact]
    public async Task AnUnconfirmedSeriesWhoseChaptersHaveDifferentTags_KeepsThem_AndGainsTheProvidersWithWeights()
    {
        int id = _db.SeedSeries(3, (n, issue) =>
        {
            issue.Tags.Clear();
            issue.Tags.Add(new IssueTag { Field = IssueTagField.Tags, Value = n == 1 ? "Only In One" : "Shared", Category = "Uncategorized" });
        });
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(tags: new[] { Tag("Shared", IssueTagWeight.Core), Tag("Gangs", IssueTagWeight.Defining, category: "Activities") });

        await Refresh(id, provider);

        using var context = _db.CreateContext();
        var chapters = context.Issues.Include(i => i.Tags).OrderBy(i => i.Id).ToList();
        Assert.Contains(chapters[0].Tags, t => t.Value == "Only In One");   // its own tag survives
        Assert.DoesNotContain(chapters[1].Tags, t => t.Value == "Only In One");
        Assert.All(chapters, c => Assert.Contains(c.Tags, t => t.Value == "Gangs" && t.Weight == IssueTagWeight.Defining));
        Assert.All(chapters.Skip(1), c => Assert.Equal(IssueTagWeight.Core, c.Tags.Single(t => t.Value == "Shared").Weight));
    }

    [Fact]
    public async Task TheReport_SaysWhatWasAppliedOfferedAndKept()
    {
        int id = Confirmed(3, e => e.Summary = "My own summary");
        var provider = new FakeProvider();
        provider.ById["42"] = Rich(tags: new[] { Tag("Gangs", IssueTagWeight.Defining), Tag("Yandere", IssueTagWeight.Recurrent, spoiler: true) });

        MangaApplyResult? report = null;
        using (var context = _db.CreateContext())
        {
            await MetadataLinkResolver.LinkAsync(provider, context, id, "42", CancellationToken.None, null, r => report = r);
        }

        Assert.Contains(report!.Applied, a => a.StartsWith("status"));
        Assert.Contains(report.Applied, a => a.StartsWith("rating"));
        Assert.Contains(report.Applied, a => a.Contains("new tag"));
        Assert.Contains(report.Offered, o => o.StartsWith("summary"));
        Assert.Contains(report.Kept, k => k.Contains("spoiler"));
    }
}
