using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>Metadata Model Phase 7: chapter ranges a collected-edition file holds, and how alignment uses them (docs/superpowers/specs/2026-10-26-you-vs-community-and-chapter-alignment-design.md Part 3).</summary>
public class CollectionContentTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    // Volume 1 = chapters 1-3, volume 2 = chapters 4-6, volume 3 = chapter 7 only.
    private const string Aggregate = """
        {"result":"ok","volumes":{
          "1":{"volume":"1","chapters":{"1":{"chapter":"1"},"2":{"chapter":"2"},"3":{"chapter":"3"}}},
          "2":{"volume":"2","chapters":{"4":{"chapter":"4"},"5":{"chapter":"5"},"6":{"chapter":"6"}}},
          "3":{"volume":"3","chapters":{"7":{"chapter":"7"}}}}}
        """;

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_content_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public CollectionContentTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_options);
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

    private PaperbunkrDbContext Open() => new(_options);

    private int AddSeries(params string[] numbers)
    {
        using var context = Open();
        var series = new Series { Name = "Series" };
        foreach (string number in numbers)
        {
            series.Issues.Add(new Issue { Number = number, FilePath = $"C:\\x\\{Guid.NewGuid():N}.cbz" });
        }

        context.Series.Add(series);
        context.SaveChanges();
        return series.Id;
    }

    private static MangaDexAggregate Remote() => MangaDexAggregateClient.Parse(Aggregate)!;

    [Fact]
    public void MapVolumes_GivesEachVolumeFileItsChapterRange()
    {
        int seriesId = AddSeries("1", "2");
        using var context = Open();

        var result = CollectionContentService.MapVolumes(context, seriesId, Remote(), Now);
        context.SaveChanges();

        Assert.Equal(new VolumeMappingResult(2, 0), result);
        var rows = context.CollectionContents.Include(c => c.Issue).OrderBy(c => c.FromKey).ToList();
        Assert.Equal(new[] { ("1", "3"), ("4", "6") }, rows.Select(r => (r.FromNumber, r.ToNumber)));
        Assert.All(rows, r => Assert.Equal(CollectionContentSource.MangaDexVolume, r.Source));
        Assert.Equal(new[] { "1", "2" }, rows.Select(r => r.Issue!.Number));
    }

    [Fact]
    public void MapVolumes_CountsFilesMangaDexHasNoVolumeFor_AndRunsAgainWithoutDuplicates()
    {
        int seriesId = AddSeries("1", "9");
        using (var context = Open())
        {
            Assert.Equal(new VolumeMappingResult(1, 1), CollectionContentService.MapVolumes(context, seriesId, Remote(), Now));
            context.SaveChanges();
        }

        using var again = Open();
        CollectionContentService.MapVolumes(again, seriesId, Remote(), Now);
        again.SaveChanges();

        Assert.Single(again.CollectionContents);
    }

    [Fact]
    public void MapVolumes_NeverReplacesARangeTheUserEntered()
    {
        int seriesId = AddSeries("1", "2");
        using (var context = Open())
        {
            int first = context.Issues.Single(i => i.SeriesId == seriesId && i.Number == "1").Id;
            context.CollectionContents.Add(new CollectionContent { IssueId = first, FromNumber = "1", ToNumber = "2", FromKey = 1, ToKey = 2, Source = CollectionContentSource.Manual, CreatedAt = Now });
            context.SaveChanges();
        }

        using var run = Open();
        var result = CollectionContentService.MapVolumes(run, seriesId, Remote(), Now);
        run.SaveChanges();

        Assert.Equal(1, result.Mapped);
        Assert.Equal(1, run.CollectionContents.Count(c => c.Source == CollectionContentSource.Manual && c.ToNumber == "2"));
        Assert.Equal(2, run.CollectionContents.Count());
    }

    [Fact]
    public void ClearMapped_RemovesOnlyTheAutomaticRanges()
    {
        int seriesId = AddSeries("1", "2");
        using (var context = Open())
        {
            CollectionContentService.MapVolumes(context, seriesId, Remote(), Now);
            int manualIssue = context.Issues.Single(i => i.SeriesId == seriesId && i.Number == "1").Id;
            context.CollectionContents.Add(new CollectionContent { IssueId = manualIssue, FromNumber = "1", ToNumber = "1", FromKey = 1, ToKey = 1, Source = CollectionContentSource.Manual, CreatedAt = Now });
            context.SaveChanges();
        }

        using var clear = Open();
        int removed = CollectionContentService.ClearMapped(clear, seriesId);
        clear.SaveChanges();

        Assert.Equal(2, removed);
        Assert.Equal(CollectionContentSource.Manual, Assert.Single(clear.CollectionContents).Source);
        Assert.True(CollectionContentService.HasRanges(clear, seriesId));
    }

    [Fact]
    public void DeletingAnIssue_DeletesItsRanges()
    {
        int seriesId = AddSeries("1");
        using (var context = Open())
        {
            CollectionContentService.MapVolumes(context, seriesId, Remote(), Now);
            context.SaveChanges();
            context.Issues.RemoveRange(context.Issues.Where(i => i.SeriesId == seriesId));
            context.SaveChanges();
        }

        using var read = Open();
        Assert.Empty(read.CollectionContents);
        Assert.False(CollectionContentService.HasRanges(read, seriesId));
    }

    [Fact]
    public void Compare_AMappedVolumeFile_OwnsItsChapters_AndIsNeverAnExtraNumber()
    {
        int seriesId = AddSeries("1", "2");
        using var context = Open();
        CollectionContentService.MapVolumes(context, seriesId, Remote(), Now);
        context.SaveChanges();
        var issues = context.Issues.Where(i => i.SeriesId == seriesId).ToList();

        var report = ChapterAlignmentResolver.Compare(issues, Remote(), ChapterAlignmentResolver.LoadContents(context, issues.Select(i => i.Id)));

        Assert.Equal(AlignmentUnit.Chapters, report.Unit);
        Assert.Equal(new[] { "7" }, report.Missing.Select(m => m.Number)); // volume 3 isn't owned
        Assert.Empty(report.ExtraLocally);
        Assert.Equal(2, report.LocalCount);
        Assert.Equal(6, report.MatchedCount);
    }

    [Fact]
    public void Compare_WithoutRanges_AVolumeFileStillReadsAsAVolume()
    {
        int seriesId = AddSeries("1", "2");
        using var context = Open();
        var issues = context.Issues.Where(i => i.SeriesId == seriesId).ToList();
        // Chapters 1 and 2 exist on MangaDex, so numbers 1 and 2 match chapters: the heuristic only switches to volumes when nothing matches a chapter.
        var report = ChapterAlignmentResolver.Compare(issues, Remote(), ChapterAlignmentResolver.LoadContents(context, issues.Select(i => i.Id)));

        Assert.Equal(AlignmentUnit.Chapters, report.Unit);
        Assert.Contains(report.Missing, m => m.Number == "4");
    }

    [Fact]
    public void ARangeBeyondWhatMangaDexLists_StillCountsItsEndpointsAsOwned()
    {
        int seriesId = AddSeries("5");
        using var context = Open();
        var issue = context.Issues.Single(i => i.SeriesId == seriesId);
        context.CollectionContents.Add(new CollectionContent { IssueId = issue.Id, FromNumber = "6.5", ToNumber = "6.5", FromKey = 6.5f, ToKey = 6.5f, Source = CollectionContentSource.Manual, CreatedAt = Now });
        context.SaveChanges();

        var report = ChapterAlignmentResolver.Compare(new[] { issue }, Remote(), ChapterAlignmentResolver.LoadContents(context, new[] { issue.Id }));

        Assert.Empty(report.ExtraLocally);
        Assert.Equal(7, report.Missing.Count);
    }
}
