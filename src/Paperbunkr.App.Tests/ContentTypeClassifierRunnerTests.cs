using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>The runner that the scheduled task, "Classify Library" and "Classify selected" share (docs/superpowers/specs/2026-10-06-content-type-auto-classify-design.md).</summary>
public class ContentTypeClassifierRunnerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public ContentTypeClassifierRunnerTests()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite(_connection).Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private sealed class TwoAgreeingSources : IContentTypeEvidenceSource
    {
        public TwoAgreeingSources(ExternalMetadataProvider provider) => Provider = provider;

        public ExternalMetadataProvider Provider { get; }

        public Task<ContentTypeEvidenceItem?> GetEvidenceAsync(IReadOnlyList<string> knownTitles, CancellationToken cancellationToken) =>
            Task.FromResult<ContentTypeEvidenceItem?>(new(Provider, "1", knownTitles[0], 1.0, "KR", ContentType.Manhwa, false));
    }

    private static ContentTypeClassificationService TwoProviders() => new(new IContentTypeEvidenceSource[]
    {
        new TwoAgreeingSources(ExternalMetadataProvider.MangaBaka),
        new TwoAgreeingSources(ExternalMetadataProvider.AniList),
    });

    private void AddSeries()
    {
        using var context = new PaperbunkrDbContext(_options);
        var series = new Series { Name = "Solo Leveling" };
        series.Issues.Add(new Issue { FilePath = "/x/sl.cbz" });
        context.Series.Add(series);
        context.SaveChanges();
    }

    [Fact]
    public async Task Run_AppliesAConfidentMatch_WhenAskBeforeIsOff()
    {
        AddSeries();

        var result = await ContentTypeClassifierRunner.RunAsync(10, null, CancellationToken.None, null, () => new PaperbunkrDbContext(_options), TwoProviders());

        Assert.Equal(1, result.Applied);
        using var verify = new PaperbunkrDbContext(_options);
        var series = verify.Series.Single();
        Assert.Equal(ContentType.Manhwa, series.ContentType);
        Assert.Equal(ReadingMode.Webtoon, series.ReadingMode);
        Assert.Equal(ContentTypeSource.Provider, series.ContentTypeSource);
    }

    [Fact]
    public async Task Run_OnlyQueues_WhenAskBeforeIsOn()
    {
        AddSeries();
        using (var context = new PaperbunkrDbContext(_options))
        {
            context.GetOrCreateAppSettings().AskBeforeClassifying = true;
            context.SaveChanges();
        }

        var result = await ContentTypeClassifierRunner.RunAsync(10, null, CancellationToken.None, null, () => new PaperbunkrDbContext(_options), TwoProviders());

        Assert.Equal(0, result.Applied);
        Assert.Equal(1, result.Queued);
        using var verify = new PaperbunkrDbContext(_options);
        var series = verify.Series.Single();
        Assert.Equal(ContentType.Unknown, series.ContentType);
        Assert.Equal(ContentType.Manhwa, series.ContentTypeSuggestion);
    }

    [Fact]
    public void Summarize_NamesWhatHappened()
    {
        Assert.Equal("Nothing to classify", ContentTypeClassifierRunner.Summarize(new ContentTypeBatchResult(0, 0, 0, 0, 0, 0, false)));
        Assert.Equal("3 classified, 2 waiting for your review, 4 with no match",
            ContentTypeClassifierRunner.Summarize(new ContentTypeBatchResult(9, 3, 2, 4, 0, 0, false)));
        Assert.Contains("stopped at this run's limit", ContentTypeClassifierRunner.Summarize(new ContentTypeBatchResult(9, 3, 0, 0, 0, 0, true)));
        Assert.Contains("didn't respond", ContentTypeClassifierRunner.Summarize(new ContentTypeBatchResult(5, 0, 0, 0, 0, 5, false)));
    }
}
