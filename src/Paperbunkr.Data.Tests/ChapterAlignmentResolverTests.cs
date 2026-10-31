using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="MangaDexAggregateClient"/> and <see cref="ChapterAlignmentResolver"/> (docs/superpowers/specs/
/// 2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 1) against a fake HttpMessageHandler and real SQLite - no network.
/// </summary>
public class ChapterAlignmentResolverTests : IDisposable
{
    private const string Sample = """
        {"result":"ok","volumes":{
          "none":{"volume":"none","count":2,"chapters":{
            "12":{"chapter":"12","id":"a","isUnavailable":false,"others":[],"count":1},
            "11.5":{"chapter":"11.5","id":"b","isUnavailable":false,"others":[],"count":1},
            "none":{"chapter":"none","id":"o","isUnavailable":false,"others":[],"count":1}}},
          "1":{"volume":"1","count":3,"chapters":{
            "1":{"chapter":"1","id":"c","isUnavailable":false,"others":[],"count":1},
            "2":{"chapter":"2","id":"d","isUnavailable":false,"others":[],"count":1},
            "3":{"chapter":"3","id":"e","isUnavailable":true,"others":[],"count":1}}}}}
        """;

    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ChapterAlignmentResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_alignment_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static Issue Local(string number, string? volume = null) => new() { Id = 0, Number = number, Volume = volume };

    [Fact]
    public void Parse_ReadsChaptersSkipsUnavailableAndUnnumbered()
    {
        var aggregate = MangaDexAggregateClient.Parse(Sample)!;

        Assert.Equal(new[] { "12", "11.5", "1", "2" }, aggregate.Chapters.Select(c => c.Chapter));
        Assert.Null(aggregate.Chapters.First(c => c.Chapter == "12").Volume);
        Assert.Equal("1", aggregate.Chapters.First(c => c.Chapter == "2").Volume);
        Assert.Equal(new[] { "1" }, aggregate.Volumes);
    }

    [Fact]
    public void Parse_EmptyArrayVolumes_IsAnEmptyAggregate()
    {
        var aggregate = MangaDexAggregateClient.Parse("{\"result\":\"ok\",\"volumes\":[]}")!;

        Assert.Empty(aggregate.Chapters);
    }

    [Fact]
    public void Parse_Garbage_ReturnsNull()
    {
        Assert.Null(MangaDexAggregateClient.Parse("not json"));
        Assert.Null(MangaDexAggregateClient.Parse("{\"result\":\"error\"}"));
    }

    [Fact]
    public void Compare_ReportsMissingExtraAndMatched()
    {
        var aggregate = MangaDexAggregateClient.Parse(Sample)!;
        var issues = new[] { Local("1"), Local("2"), Local("11.5"), Local("40") };

        var report = ChapterAlignmentResolver.Compare(issues, aggregate);

        Assert.Equal(AlignmentUnit.Chapters, report.Unit);
        Assert.Equal(new[] { "12" }, report.Missing.Select(m => m.Number));
        Assert.Equal(new[] { "40" }, report.ExtraLocally);
        Assert.Equal(3, report.MatchedCount);
        Assert.True(report.HasMismatches);
    }

    [Fact]
    public void Compare_DecimalAndFormattedNumbersMatch()
    {
        var aggregate = MangaDexAggregateClient.Parse(Sample)!;

        var report = ChapterAlignmentResolver.Compare(new[] { Local("11.5"), Local("012") }, aggregate);

        Assert.DoesNotContain(report.Missing, m => m.Number is "11.5" or "12");
    }

    [Fact]
    public void Compare_VolumeDisagreement_IsAConflictNotMissing()
    {
        var aggregate = MangaDexAggregateClient.Parse(Sample)!;

        var report = ChapterAlignmentResolver.Compare(new[] { Local("2", volume: "3"), Local("1", volume: "1") }, aggregate);

        var conflict = Assert.Single(report.VolumeConflicts);
        Assert.Equal("2", conflict.Number);
        Assert.Equal("3", conflict.LocalVolume);
        Assert.Equal("1", conflict.RemoteVolume);
    }

    [Fact]
    public void Compare_NoLocalVolumeSet_IsNeverAConflict()
    {
        var aggregate = MangaDexAggregateClient.Parse(Sample)!;

        var report = ChapterAlignmentResolver.Compare(new[] { Local("1"), Local("2") }, aggregate);

        Assert.Empty(report.VolumeConflicts);
    }

    [Fact]
    public void Compare_VolumeFilesWithNoChapterOverlap_UseVolumeUnit()
    {
        var aggregate = MangaDexAggregateClient.Parse(
            """{"result":"ok","volumes":{"1":{"volume":"1","chapters":{"10":{"chapter":"10"}}},"2":{"volume":"2","chapters":{"20":{"chapter":"20"}}},"3":{"volume":"3","chapters":{"30":{"chapter":"30"}}}}}""")!;

        var report = ChapterAlignmentResolver.Compare(new[] { Local("1"), Local("2") }, aggregate);

        Assert.Equal(AlignmentUnit.Volumes, report.Unit);
        Assert.Equal(new[] { "3" }, report.Missing.Select(m => m.Number));
        Assert.Empty(report.ExtraLocally);
    }

    [Fact]
    public void Compare_EmptyLibrary_ListsEveryRemoteChapterMissing()
    {
        var report = ChapterAlignmentResolver.Compare(Array.Empty<Issue>(), MangaDexAggregateClient.Parse(Sample)!);

        Assert.Equal(4, report.Missing.Count);
        Assert.Equal(0, report.LocalCount);
    }

    [Fact]
    public void Compare_EmptyRemote_MarksEveryLocalExtra()
    {
        var report = ChapterAlignmentResolver.Compare(new[] { Local("1"), Local("2") }, new MangaDexAggregate(Array.Empty<MangaDexAggregateChapter>()));

        Assert.Empty(report.Missing);
        Assert.Equal(2, report.ExtraLocally.Count);
    }

    [Fact]
    public void Compare_IssuesWithoutNumbers_AreIgnored()
    {
        var report = ChapterAlignmentResolver.Compare(new[] { Local("1"), new Issue { Number = null } }, MangaDexAggregateClient.Parse(Sample)!);

        Assert.Equal(1, report.LocalCount);
    }

    [Fact]
    public void PreferredLanguage_IsTheMostCommonOrEnglish()
    {
        Assert.Equal("fr", ChapterAlignmentResolver.PreferredLanguage(new[]
        {
            new Issue { LanguageISO = "fr" }, new Issue { LanguageISO = "FR" }, new Issue { LanguageISO = "en" },
        }));
        Assert.Equal("en", ChapterAlignmentResolver.PreferredLanguage(new[] { new Issue() }));
    }

    [Fact]
    public async Task Client_FiltersByLanguage_AndRaisesOnFailure()
    {
        string? requested = null;
        var ok = new MangaDexAggregateClient(new HttpClient(new StubHandler(r => { requested = r.RequestUri!.ToString(); return Json(HttpStatusCode.OK, Sample); })));

        var aggregate = await ok.FetchAsync("abc", "EN", CancellationToken.None);

        Assert.Equal(4, aggregate.ChapterCount);
        Assert.Contains("/manga/abc/aggregate", requested);
        Assert.Contains("translatedLanguage%5B%5D=en", requested);

        var limited = new MangaDexAggregateClient(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.TooManyRequests, "{}"))));
        await Assert.ThrowsAsync<MetadataProviderUnavailableException>(() => limited.FetchAsync("abc", null, CancellationToken.None));

        var broken = new MangaDexAggregateClient(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "<html>"))));
        await Assert.ThrowsAsync<MetadataProviderUnavailableException>(() => broken.FetchAsync("abc", null, CancellationToken.None));
    }

    [Fact]
    public async Task BuildAsync_NotLinked_ReturnsNull()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, linked: false);

        var report = await ChapterAlignmentResolver.BuildAsync(context, seriesId, FailingClient(), DateTime.UtcNow);

        Assert.Null(report);
    }

    [Fact]
    public async Task BuildAsync_CachesTheAggregate_AndRefetchesWhenStale()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, linked: true, numbers: new[] { "1", "2" });
        int calls = 0;
        var client = new MangaDexAggregateClient(new HttpClient(new StubHandler(_ => { calls++; return Json(HttpStatusCode.OK, Sample); })));
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        var first = await ChapterAlignmentResolver.BuildAsync(context, seriesId, client, now);
        var second = await ChapterAlignmentResolver.BuildAsync(context, seriesId, client, now.AddHours(1));
        Assert.Equal(1, calls);
        Assert.Equal(first!.Missing.Select(m => m.Number), second!.Missing.Select(m => m.Number));

        await ChapterAlignmentResolver.BuildAsync(context, seriesId, client, now.AddDays(2));
        Assert.Equal(2, calls);
        Assert.Single(context.ExternalMetadataSnapshots.Where(s => s.SchemaVersion == ChapterAlignmentResolver.SnapshotSchemaVersion));
    }

    [Fact]
    public async Task BuildAsync_Offline_FallsBackToStaleCache_OrThrowsWithNone()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int seriesId = SeedSeries(context, linked: true, numbers: new[] { "1" });
        var now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<MetadataProviderUnavailableException>(() => ChapterAlignmentResolver.BuildAsync(context, seriesId, FailingClient(), now));

        var good = new MangaDexAggregateClient(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, Sample))));
        await ChapterAlignmentResolver.BuildAsync(context, seriesId, good, now);

        var stale = await ChapterAlignmentResolver.BuildAsync(context, seriesId, FailingClient(), now.AddDays(5));
        Assert.NotNull(stale);
    }

    private static int SeedSeries(PaperbunkrDbContext context, bool linked, string[]? numbers = null)
    {
        var series = new Series { Name = "Test Manga" };
        foreach (string number in numbers ?? Array.Empty<string>())
        {
            series.Issues.Add(new Issue { Number = number });
        }

        context.Series.Add(series);
        context.SaveChanges();
        if (linked)
        {
            context.ExternalMediaIds.Add(new ExternalMediaId { SeriesId = series.Id, Provider = ExternalMetadataProvider.MangaDex, ExternalId = "md-1" });
            context.SaveChanges();
        }

        return series.Id;
    }

    private static MangaDexAggregateClient FailingClient() =>
        new(new HttpClient(new StubHandler(_ => Json(HttpStatusCode.ServiceUnavailable, "{}"))));

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
