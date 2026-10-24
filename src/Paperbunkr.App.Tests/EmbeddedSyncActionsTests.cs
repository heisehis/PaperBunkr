using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Summary, Retry, Write anyway, Verify and the startup clean-up (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §10).</summary>
public class EmbeddedSyncActionsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_embact_db_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly string _dir;

    public EmbeddedSyncActionsTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = Context();
        context.Database.EnsureCreated();
        var settings = context.GetOrCreateAppSettings();
        settings.WriteMetadataToFiles = true;
        settings.WriteMetadataAutomatically = true;
        context.SaveChanges();
        _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_embact_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext Context() => new(_options);

    private (int SeriesId, int[] IssueIds) SeedSeries(params (EmbeddedSyncState State, bool Dirty)[] chapters)
    {
        using var context = Context();
        var series = new Series { Name = "Kilo Station", ContentType = ContentType.Manga };
        int n = 0;
        foreach (var (state, dirty) in chapters)
        {
            string path = Path.Combine(_dir, $"ch{++n}.cbz");
            CbzFixture.Create(path, pageCount: 1);
            series.Issues.Add(new Issue { Number = n.ToString(), FilePath = path, PageCount = 1, Publisher = "X", EmbeddedSyncState = state, EmbeddedDirty = dirty });
        }

        context.Series.Add(series);
        context.SaveChanges();
        return (series.Id, series.Issues.Select(i => i.Id).ToArray());
    }

    private EmbeddedSyncActions Actions() => new(Context, new MetadataWriteBackQueue(Context, new MetadataFileWriteBackService(Context), (_, _) => { }, TimeSpan.FromMilliseconds(20)));

    [Fact]
    public void TheSummary_CountsChaptersByWhatIsHappeningToTheirFiles()
    {
        var (seriesId, _) = SeedSeries(
            (EmbeddedSyncState.None, true),
            (EmbeddedSyncState.Synced, false),
            (EmbeddedSyncState.NeedsAttention, true),
            (EmbeddedSyncState.ConflictExternalModification, true),
            (EmbeddedSyncState.UnsupportedFormat, false),
            (EmbeddedSyncState.SkippedReadOnly, true));

        var summary = Actions().GetSummary(seriesId);

        Assert.Equal(6, summary.Total);
        Assert.Equal(1, summary.Owed);
        Assert.Equal(1, summary.Synced);
        Assert.Equal(1, summary.NeedAttention);
        Assert.Equal(1, summary.Conflicts);
        Assert.Equal(1, summary.Unsupported);
        Assert.Equal(1, summary.ReadOnlyOrMissing);
        Assert.True(summary.HasProblems);
        Assert.Contains("changed outside Paperbunkr", summary.Describe());
    }

    [Fact]
    public void RetryFailed_QueuesTheStuckChapters_ButNotTheConflicts()
    {
        var (seriesId, ids) = SeedSeries(
            (EmbeddedSyncState.NeedsAttention, true),
            (EmbeddedSyncState.ConflictExternalModification, true),
            (EmbeddedSyncState.Synced, false));

        Assert.Equal(1, Actions().RetryFailed(seriesId));
    }

    [Fact]
    public async Task WriteAnyway_ReplacesAFileThatWasChangedOutside()
    {
        var (seriesId, ids) = SeedSeries((EmbeddedSyncState.None, true));
        var service = new MetadataFileWriteBackService(Context);
        await service.WriteAsync(ids[0], includeSidecar: false);
        string path = Path.Combine(_dir, "ch1.cbz");
        File.Delete(path);
        CbzFixture.Create(path, pageCount: 1, new ComicInfo { Publisher = "Edited elsewhere" });
        await service.WriteAsync(ids[0], includeSidecar: false);   // notices the outside change
        Assert.Equal(EmbeddedSyncState.ConflictExternalModification, Context().Issues.Single(i => i.Id == ids[0]).EmbeddedSyncState);

        Assert.Equal(1, Actions().WriteAnyway(seriesId));
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (Context().Issues.Single(i => i.Id == ids[0]).EmbeddedSyncState != EmbeddedSyncState.Synced && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        Assert.Equal(EmbeddedSyncState.Synced, Context().Issues.Single(i => i.Id == ids[0]).EmbeddedSyncState);
    }

    [Fact]
    public async Task Verify_ReportsAFileThatDiffers_AndWritesNothing()
    {
        var (seriesId, ids) = SeedSeries((EmbeddedSyncState.None, true), (EmbeddedSyncState.None, true));
        var service = new MetadataFileWriteBackService(Context);
        await service.WriteAsync(ids[0], includeSidecar: false);
        await service.WriteAsync(ids[1], includeSidecar: false);
        using (var context = Context())
        {
            context.Issues.Single(i => i.Id == ids[1]).Publisher = "Changed in the library";
            context.SaveChanges();
        }

        string untouched = Path.Combine(_dir, "ch2.cbz");
        string stampBefore = MetadataFileWriteBackService.FileStamp(untouched)!;

        var result = await Actions().VerifyAsync(seriesId);

        Assert.Equal(2, result.Checked);
        Assert.Equal(1, result.Matching);
        Assert.Contains("Publisher", Assert.Single(result.Mismatches));
        Assert.Equal(stampBefore, MetadataFileWriteBackService.FileStamp(untouched));
    }

    [Fact]
    public void TheStartupCleanUp_RemovesLeftoversOfAnInterruptedWrite_AndNothingElse()
    {
        string archive = Path.Combine(_dir, "book.cbz");
        File.WriteAllBytes(archive, new byte[] { 1 });
        string leftover = archive + ".pbwrite-0123456789abcdef.tmp";
        File.WriteAllBytes(leftover, new byte[] { 2 });
        File.SetLastWriteTimeUtc(leftover, DateTime.UtcNow.AddMinutes(-10)); // fresher ones may be a write still in progress
        string other = Path.Combine(_dir, "other.txt");
        File.WriteAllBytes(other, new byte[] { 3 });

        int removed = EmbeddedSyncActions.CleanLeftoverTempFiles(new[] { archive });

        Assert.Equal(1, removed);
        Assert.False(File.Exists(leftover));
        Assert.True(File.Exists(archive));
        Assert.True(File.Exists(other));
    }
}
