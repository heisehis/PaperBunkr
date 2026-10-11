using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>The tracker-driven content-type classifier (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md) - mapping, decision rule, editor and service.</summary>
public class ContentTypeClassificationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public ContentTypeClassificationTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite(_connection).Options);

    // ----- ProviderContentTypeMapper -----

    [Theory]
    [InlineData("manga", ContentType.Manga, false)]
    [InlineData("Manhwa", ContentType.Manhwa, false)]
    [InlineData("MANHUA", ContentType.Manhua, false)]
    [InlineData("oel", ContentType.Comic, true)]
    public void MangaBaka_MapsItsOwnTypeField(string type, ContentType expected, bool queueOnly)
    {
        var mapped = ProviderContentTypeMapper.MapMangaBaka(type);
        Assert.Equal(expected, mapped.Type);
        Assert.Equal(queueOnly, mapped.QueueOnly);
    }

    [Fact]
    public void MangaBaka_NovelIsIgnored_AndUnknownStringsMapToNothing()
    {
        Assert.True(ProviderContentTypeMapper.MapMangaBaka("novel").Ignored);
        Assert.Null(ProviderContentTypeMapper.MapMangaBaka("novel").Type);
        Assert.Null(ProviderContentTypeMapper.MapMangaBaka("other").Type);
        Assert.Null(ProviderContentTypeMapper.MapMangaBaka(null).Type);
    }

    [Theory]
    [InlineData("MANGA", "JP", ContentType.Manga)]
    [InlineData("MANGA", "KR", ContentType.Manhwa)]
    [InlineData("MANGA", "CN", ContentType.Manhua)]
    [InlineData("MANGA", "TW", ContentType.Manhua)]
    [InlineData("ONE_SHOT", null, ContentType.Manga)]
    [InlineData("ONE_SHOT", "KR", ContentType.Manhwa)]
    public void AniList_TypeComesFromCountryOfOrigin(string format, string? country, ContentType expected)
    {
        var mapped = ProviderContentTypeMapper.MapAniList(format, country);
        Assert.Equal(expected, mapped.Type);
        Assert.False(mapped.QueueOnly);
    }

    [Fact]
    public void AniList_PlainMangaWithNoCountryOrANovelSaysNothing()
    {
        Assert.Null(ProviderContentTypeMapper.MapAniList("MANGA", null).Type);
        var novel = ProviderContentTypeMapper.MapAniList("NOVEL", "JP");
        Assert.True(novel.Ignored);
        Assert.Null(novel.Type);
    }

    [Theory]
    [InlineData("ja", ContentType.Manga, false)]
    [InlineData("ko", ContentType.Manhwa, false)]
    [InlineData("zh", ContentType.Manhua, false)]
    [InlineData("zh-hk", ContentType.Manhua, false)]
    [InlineData("en", ContentType.Comic, true)]
    public void MangaDex_TypeComesFromOriginalLanguage(string language, ContentType expected, bool queueOnly)
    {
        var mapped = ProviderContentTypeMapper.MapMangaDex(language);
        Assert.Equal(expected, mapped.Type);
        Assert.Equal(queueOnly, mapped.QueueOnly);
    }

    [Theory]
    [InlineData(ContentType.Manga, ReadingMode.RightToLeft)]
    [InlineData(ContentType.Manhwa, ReadingMode.Webtoon)]
    [InlineData(ContentType.Manhua, ReadingMode.Webtoon)]
    [InlineData(ContentType.Comic, ReadingMode.LeftToRight)]
    public void ReadingModeFollowsType(ContentType type, ReadingMode expected) =>
        Assert.Equal(expected, ProviderContentTypeMapper.ReadingModeFor(type));

    // ----- ContentTypeDecision -----

    private static ContentTypeEvidenceItem Item(ExternalMetadataProvider provider, ContentType? type, double score = 1.0, bool queueOnly = false) =>
        new(provider, "1", "Title", score, "raw", type, queueOnly);

    [Fact]
    public void TwoProvidersAgreeing_AutoApply()
    {
        var outcome = ContentTypeDecision.Decide(
            new[] { Item(ExternalMetadataProvider.MangaBaka, ContentType.Manhwa, 0.97), Item(ExternalMetadataProvider.AniList, ContentType.Manhwa, 1.0) },
            Array.Empty<ContentType>(), askBefore: false);

        Assert.Equal(ContentTypeOutcomeKind.AutoApply, outcome.Kind);
        Assert.Equal(ContentType.Manhwa, outcome.Type);
        Assert.Equal(0.97, outcome.Confidence);
        Assert.False(outcome.Conflict);
    }

    [Fact]
    public void OneProviderPlusAgreeingLocalGuess_AutoApplies_ButAloneIsQueued()
    {
        var one = new[] { Item(ExternalMetadataProvider.MangaBaka, ContentType.Manga) };

        Assert.Equal(ContentTypeOutcomeKind.AutoApply, ContentTypeDecision.Decide(one, new[] { ContentType.Manga }, false).Kind);
        Assert.Equal(ContentTypeOutcomeKind.Queue, ContentTypeDecision.Decide(one, Array.Empty<ContentType>(), false).Kind);
        Assert.Equal(ContentTypeOutcomeKind.Queue, ContentTypeDecision.Decide(one, new[] { ContentType.Manhwa }, false).Kind);
    }

    [Fact]
    public void AskBefore_NeverAutoApplies()
    {
        var outcome = ContentTypeDecision.Decide(
            new[] { Item(ExternalMetadataProvider.MangaBaka, ContentType.Manga), Item(ExternalMetadataProvider.AniList, ContentType.Manga) },
            Array.Empty<ContentType>(), askBefore: true);

        Assert.Equal(ContentTypeOutcomeKind.Queue, outcome.Kind);
        Assert.Equal(ContentType.Manga, outcome.Type);
    }

    [Fact]
    public void DisagreeingProviders_QueueAsAConflict()
    {
        var outcome = ContentTypeDecision.Decide(
            new[] { Item(ExternalMetadataProvider.MangaBaka, ContentType.Manhwa), Item(ExternalMetadataProvider.AniList, ContentType.Manga) },
            new[] { ContentType.Manga }, askBefore: false);

        Assert.Equal(ContentTypeOutcomeKind.Queue, outcome.Kind);
        Assert.True(outcome.Conflict);
    }

    [Fact]
    public void ReviewTierMatch_IsQueuedNotApplied_AndBelowReviewIsNoMatch()
    {
        var review = ContentTypeDecision.Decide(
            new[] { Item(ExternalMetadataProvider.MangaBaka, ContentType.Manga, 0.80), Item(ExternalMetadataProvider.AniList, ContentType.Manga, 0.80) },
            new[] { ContentType.Manga }, false);
        Assert.Equal(ContentTypeOutcomeKind.Queue, review.Kind);

        var low = ContentTypeDecision.Decide(new[] { Item(ExternalMetadataProvider.MangaBaka, ContentType.Manga, 0.60) }, new[] { ContentType.Manga }, false);
        Assert.Equal(ContentTypeOutcomeKind.NoMatch, low.Kind);
    }

    [Fact]
    public void QueueOnlyType_NeverVotesForAnAutoApply()
    {
        var outcome = ContentTypeDecision.Decide(
            new[] { Item(ExternalMetadataProvider.MangaBaka, ContentType.Comic, 1.0, queueOnly: true) },
            new[] { ContentType.Comic }, false);

        Assert.Equal(ContentTypeOutcomeKind.Queue, outcome.Kind);
        Assert.Equal(ContentType.Comic, outcome.Type);
    }

    [Fact]
    public void UntypedEvidence_IsNoMatch() =>
        Assert.Equal(ContentTypeOutcomeKind.NoMatch,
            ContentTypeDecision.Decide(new[] { Item(ExternalMetadataProvider.AniList, null) }, Array.Empty<ContentType>(), false).Kind);

    [Fact]
    public void EvidenceRoundTripsThroughJson()
    {
        var items = new[] { Item(ExternalMetadataProvider.MangaBaka, ContentType.Manhwa, 0.97), Item(ExternalMetadataProvider.AniList, ContentType.Manhwa) };
        var back = ContentTypeEvidence.Deserialize(ContentTypeEvidence.Serialize(items));

        Assert.Equal(items, back);
        Assert.Empty(ContentTypeEvidence.Deserialize(null));
        Assert.Empty(ContentTypeEvidence.Deserialize("not json"));
    }

    // ----- SeriesContentTypeEditor -----

    [Fact]
    public void SetManual_Locks_AndClearsPipelineState()
    {
        var series = new Series { Name = "A", ContentTypeSuggestion = ContentType.Manga, PreviousContentType = ContentType.Unknown };
        SeriesContentTypeEditor.SetManual(series, ContentType.Manhua);

        Assert.Equal(ContentType.Manhua, series.ContentType);
        Assert.True(series.ContentTypeLocked);
        Assert.Equal(ContentTypeSource.Manual, series.ContentTypeSource);
        Assert.Null(series.ContentTypeSuggestion);
        Assert.Null(series.PreviousContentType);
    }

    [Fact]
    public void SetGuess_RefusesALockedSeries()
    {
        var series = new Series { Name = "A", ContentTypeLocked = true };

        Assert.False(SeriesContentTypeEditor.SetGuess(series, ContentType.Manga, ReadingMode.RightToLeft, ContentTypeSource.Publisher));
        Assert.Equal(ContentType.Unknown, series.ContentType);
    }

    [Fact]
    public void ApplyAuto_ChangesReadingModeOnlyWhileDefault_AndUndoRestoresBoth()
    {
        var fresh = new Series { Name = "A" };
        SeriesContentTypeEditor.ApplyAuto(fresh, ContentType.Manhwa, 0.97, null, DateTime.UtcNow);
        Assert.Equal(ReadingMode.Webtoon, fresh.ReadingMode);
        Assert.Equal(ContentTypeSource.Provider, fresh.ContentTypeSource);

        Assert.True(SeriesContentTypeEditor.Undo(fresh));
        Assert.Equal(ContentType.Unknown, fresh.ContentType);
        Assert.Equal(ReadingMode.LeftToRight, fresh.ReadingMode);
        Assert.True(fresh.ContentTypeLocked);

        var customised = new Series { Name = "B", ReadingMode = ReadingMode.VerticalContinuous };
        SeriesContentTypeEditor.ApplyAuto(customised, ContentType.Manga, 0.97, null, DateTime.UtcNow);
        Assert.Equal(ReadingMode.VerticalContinuous, customised.ReadingMode);
    }

    [Fact]
    public void QueueActions_AcceptKeepSkip_AllLock()
    {
        var accept = new Series { Name = "A" };
        SeriesContentTypeEditor.Queue(accept, ContentType.Manga, 0.9, null, DateTime.UtcNow);
        Assert.True(SeriesContentTypeEditor.AcceptSuggestion(accept));
        Assert.Equal(ContentType.Manga, accept.ContentType);
        Assert.True(accept.ContentTypeLocked);
        Assert.Null(accept.ContentTypeSuggestion);

        var keep = new Series { Name = "B", ContentType = ContentType.Comic };
        SeriesContentTypeEditor.Queue(keep, ContentType.Manga, 0.9, null, DateTime.UtcNow);
        SeriesContentTypeEditor.Keep(keep);
        Assert.Equal(ContentType.Comic, keep.ContentType);
        Assert.True(keep.ContentTypeLocked);
        Assert.Null(keep.ContentTypeSuggestion);

        var skip = new Series { Name = "C" };
        SeriesContentTypeEditor.Skip(skip, DateTime.UtcNow);
        Assert.True(skip.ContentTypeLocked);
        Assert.Equal(ContentTypeCheck.Skipped, skip.ContentTypeCheck);
    }

    [Fact]
    public void RecheckPublisherClassified_UnlocksOnlyRowsThePublisherStillExplains()
    {
        using (var context = CreateContext())
        {
            context.Series.Add(Locked("Viz Manga", ContentType.Manga, "VIZ Media", ContentTypeSource.Existing));
            context.Series.Add(Locked("Hand Set", ContentType.Manga, "VIZ Media", ContentTypeSource.Manual));
            context.Series.Add(Locked("Wrong Publisher", ContentType.Manhwa, "VIZ Media", ContentTypeSource.Existing));
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal(1, SeriesContentTypeEditor.RecheckPublisherClassified(context));
        }

        using (var context = CreateContext())
        {
            Assert.False(context.Series.Single(s => s.Name == "Viz Manga").ContentTypeLocked);
            Assert.True(context.Series.Single(s => s.Name == "Hand Set").ContentTypeLocked);
            Assert.True(context.Series.Single(s => s.Name == "Wrong Publisher").ContentTypeLocked);
        }
    }

    private static Series Locked(string name, ContentType type, string publisher, ContentTypeSource source)
    {
        var series = new Series { Name = name, ContentType = type, ContentTypeSource = source, ContentTypeLocked = true };
        series.Issues.Add(new Issue { FilePath = $"/x/{name}.cbz", Publisher = publisher });
        return series;
    }

    // ----- ContentTypeClassificationService -----

    private sealed class FakeSource : IContentTypeEvidenceSource
    {
        private readonly Func<IReadOnlyList<string>, ContentTypeEvidenceItem?> _answer;
        public int Calls { get; private set; }

        public FakeSource(ExternalMetadataProvider provider, Func<IReadOnlyList<string>, ContentTypeEvidenceItem?> answer)
        {
            Provider = provider;
            _answer = answer;
        }

        public ExternalMetadataProvider Provider { get; }

        public Task<ContentTypeEvidenceItem?> GetEvidenceAsync(IReadOnlyList<string> knownTitles, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_answer(knownTitles));
        }
    }

    private sealed class DownSource : IContentTypeEvidenceSource
    {
        public ExternalMetadataProvider Provider => ExternalMetadataProvider.AniList;

        public Task<ContentTypeEvidenceItem?> GetEvidenceAsync(IReadOnlyList<string> knownTitles, CancellationToken cancellationToken) =>
            throw new MetadataProviderUnavailableException();
    }

    private static Series Plain(string name, string? publisher = null)
    {
        var series = new Series { Name = name };
        series.Issues.Add(new Issue { FilePath = $"/x/{name}.cbz", Publisher = publisher });
        return series;
    }

    [Fact]
    public async Task Service_StopsQueryingOnceTheAnswerIsCertain()
    {
        var baka = new FakeSource(ExternalMetadataProvider.MangaBaka, _ => Item(ExternalMetadataProvider.MangaBaka, ContentType.Manga));
        var anilist = new FakeSource(ExternalMetadataProvider.AniList, _ => Item(ExternalMetadataProvider.AniList, ContentType.Manga));
        var service = new ContentTypeClassificationService(new IContentTypeEvidenceSource[] { baka, anilist });

        // The series' own type is a Publisher guess of Manga, which corroborates MangaBaka alone.
        var series = Plain("Berserk", "Dark Horse");
        series.ContentType = ContentType.Manga;
        series.ContentTypeSource = ContentTypeSource.Publisher;

        var result = await service.ClassifyAsync(series, askBefore: false, CancellationToken.None);

        Assert.Equal(SeriesClassifyKind.Applied, result.Kind);
        Assert.Equal(1, baka.Calls);
        Assert.Equal(0, anilist.Calls);
        Assert.Equal(ContentTypeSource.Provider, series.ContentTypeSource);
    }

    [Fact]
    public async Task Service_QueuesAUniqueUncorroboratedMatch_AndRecordsNoMatch()
    {
        var queued = Plain("Solo Leveling");
        var nothing = Plain("Obscure Thing");
        var source = new FakeSource(ExternalMetadataProvider.MangaBaka,
            titles => titles[0] == "Solo Leveling" ? Item(ExternalMetadataProvider.MangaBaka, ContentType.Manhwa, 0.97) : null);
        var service = new ContentTypeClassificationService(new IContentTypeEvidenceSource[] { source });

        Assert.Equal(SeriesClassifyKind.Queued, (await service.ClassifyAsync(queued, false, CancellationToken.None)).Kind);
        Assert.Equal(ContentType.Manhwa, queued.ContentTypeSuggestion);
        Assert.Equal(ContentType.Unknown, queued.ContentType);
        Assert.NotNull(queued.ContentTypeEvidence);

        Assert.Equal(SeriesClassifyKind.NoMatch, (await service.ClassifyAsync(nothing, false, CancellationToken.None)).Kind);
        Assert.Equal(ContentTypeCheck.NoMatch, nothing.ContentTypeCheck);
    }

    [Fact]
    public async Task Service_SkipsWesternSeries_WithoutSearching()
    {
        var source = new FakeSource(ExternalMetadataProvider.MangaBaka, _ => Item(ExternalMetadataProvider.MangaBaka, ContentType.Manga));
        var service = new ContentTypeClassificationService(new IContentTypeEvidenceSource[] { source });

        var result = await service.ClassifyAsync(Plain("Batman", "DC Comics"), false, CancellationToken.None);

        Assert.Equal(SeriesClassifyKind.Skipped, result.Kind);
        Assert.Equal("Western publisher", result.SkipReason);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Service_AnOutage_WritesNothing()
    {
        var series = Plain("Berserk");
        var service = new ContentTypeClassificationService(new IContentTypeEvidenceSource[] { new DownSource() });

        Assert.Equal(SeriesClassifyKind.Unavailable, (await service.ClassifyAsync(series, false, CancellationToken.None)).Kind);
        Assert.Equal(ContentTypeCheck.None, series.ContentTypeCheck);
    }

    [Fact]
    public async Task Batch_HonoursTheBudget_NeverTouchesLockedRows_AndIsResumable()
    {
        using (var context = CreateContext())
        {
            for (int i = 0; i < 5; i++)
            {
                context.Series.Add(Plain($"Series {i}"));
            }

            var locked = Plain("Locked One");
            locked.ContentTypeLocked = true;
            context.Series.Add(locked);
            context.SaveChanges();
        }

        var source = new FakeSource(ExternalMetadataProvider.MangaBaka, _ => Item(ExternalMetadataProvider.MangaBaka, ContentType.Manga));
        var service = new ContentTypeClassificationService(new IContentTypeEvidenceSource[] { source });

        var first = await service.ClassifyBatchAsync(CreateContext, askBefore: false, searchBudget: 3, null, CancellationToken.None);
        Assert.Equal(3, first.Queued);
        Assert.True(first.BudgetReached);

        var second = await service.ClassifyBatchAsync(CreateContext, askBefore: false, searchBudget: 3, null, CancellationToken.None);
        Assert.Equal(2, second.Queued);   // resumes with the two never-checked ones; nothing is checked twice
        Assert.Equal(5, source.Calls);

        using var verify = CreateContext();
        Assert.Equal(ContentTypeCheck.None, verify.Series.Single(s => s.Name == "Locked One").ContentTypeCheck);
    }

    [Fact]
    public async Task Batch_ForcedSelection_ReRunsASeriesTheAutomaticPassWouldSkip_ButNotALockedOne()
    {
        int id, lockedId;
        using (var context = CreateContext())
        {
            var checkedRecently = Plain("Checked");
            checkedRecently.ContentTypeCheck = ContentTypeCheck.NoMatch;
            checkedRecently.ContentTypeCheckedUtc = DateTime.UtcNow;
            var locked = Plain("Locked");
            locked.ContentTypeLocked = true;
            context.Series.AddRange(checkedRecently, locked);
            context.SaveChanges();
            id = checkedRecently.Id;
            lockedId = locked.Id;
        }

        var source = new FakeSource(ExternalMetadataProvider.MangaBaka, _ => Item(ExternalMetadataProvider.MangaBaka, ContentType.Manga));
        var service = new ContentTypeClassificationService(new IContentTypeEvidenceSource[] { source });

        var automatic = await service.ClassifyBatchAsync(CreateContext, false, 10, null, CancellationToken.None);
        Assert.Equal(0, automatic.Examined);

        var forced = await service.ClassifyBatchAsync(CreateContext, false, 10, null, CancellationToken.None, new[] { id, lockedId });
        Assert.Equal(1, forced.Examined);
        Assert.Equal(1, forced.Queued);
    }

    [Fact]
    public void IsDue_RetriesNoMatchOnlyAfterNinetyDays()
    {
        var now = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
        var series = new Series { Name = "A", ContentTypeCheck = ContentTypeCheck.NoMatch, ContentTypeCheckedUtc = now.AddDays(-89) };
        Assert.False(ContentTypeClassificationService.IsDue(series, now));

        series.ContentTypeCheckedUtc = now.AddDays(-91);
        Assert.True(ContentTypeClassificationService.IsDue(series, now));

        series.ContentTypeLocked = true;
        Assert.False(ContentTypeClassificationService.IsDue(series, now));
    }
}
