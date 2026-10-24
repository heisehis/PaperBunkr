using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Durable embedded sync for manga chapters (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §10, acceptance G, H, I, J), on real synthetic
/// archives: fingerprints, the external-modification guard, retry, the startup catch-up, and that a Comic is written exactly as before.
/// </summary>
public class EmbeddedSyncTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_embsync_db_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;
    private readonly string _dir;

    public EmbeddedSyncTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = Context();
        context.Database.EnsureCreated();
        _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_embsync_{Guid.NewGuid():N}");
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

    private MetadataFileWriteBackService Service() => new(Context);

    private void SetSettings(bool master = true, bool automatic = true)
    {
        using var context = Context();
        var settings = context.GetOrCreateAppSettings();
        settings.WriteMetadataToFiles = master;
        settings.WriteMetadataAutomatically = automatic;
        context.SaveChanges();
    }

    private (int SeriesId, int IssueId, string Path) Seed(string name = "ch1.cbz", ContentType type = ContentType.Manga, Action<Series>? configureSeries = null, Action<Issue>? configureIssue = null, bool createFile = true)
    {
        string path = Path.Combine(_dir, name);
        if (createFile)
        {
            CbzFixture.Create(path, pageCount: 1);
        }

        using var context = Context();
        var series = new Series { Name = "Kilo Station", ContentType = type };
        configureSeries?.Invoke(series);
        var issue = new Issue { Number = "1", FilePath = path, PageCount = 1, Publisher = "X", Writer = "A" };
        configureIssue?.Invoke(issue);
        series.Issues.Add(issue);
        context.Series.Add(series);
        context.SaveChanges();
        return (series.Id, issue.Id, path);
    }

    private static ComicInfo ReadBack(string path)
    {
        using var provider = Providers.Readers.CreateSourceProvider(path);
        provider.Open(async: false);
        return ((IInfoStorage)provider).LoadInfo(InfoLoadingMethod.Complete);
    }

    /// <summary>The publisher a file holds, or null when it has no ComicInfo at all.</summary>
    private static string? PublisherOf(string path)
    {
        using var provider = Providers.Readers.CreateSourceProvider(path);
        provider.Open(async: false);
        return ((IInfoStorage)provider).LoadInfo(InfoLoadingMethod.Complete)?.Publisher;
    }

    /// <summary>Someone else's rewrite of the file: replaces it with a new archive carrying the given ComicInfo.</summary>
    private static void Recreate(string path, ComicInfo info)
    {
        File.Delete(path);
        CbzFixture.Create(path, pageCount: 1, info);
    }

    private Issue Reload(int id)
    {
        using var context = Context();
        return context.Issues.AsNoTracking().Single(i => i.Id == id);
    }

    [Fact]
    public async Task AWrite_RecordsTheFingerprintStampAndSyncedState_AndClearsDirty()
    {
        var (_, id, path) = Seed(configureIssue: i => i.EmbeddedDirty = true);

        var outcome = await Service().WriteAsync(id, includeSidecar: false);

        Assert.Equal(MetadataWriteBackResult.Success, outcome.Result);
        var issue = Reload(id);
        Assert.False(issue.EmbeddedDirty);
        Assert.Equal(EmbeddedSyncState.Synced, issue.EmbeddedSyncState);
        Assert.NotNull(issue.EmbeddedFingerprint);
        Assert.Equal(MetadataFileWriteBackService.FileStamp(path), issue.EmbeddedFileStamp);
        Assert.Equal("X", ReadBack(path).Publisher);
    }

    [Fact]
    public async Task AnUnchangedFile_IsNotRewritten()
    {
        var (_, id, path) = Seed();
        await Service().WriteAsync(id, includeSidecar: false);
        string stampAfterFirst = MetadataFileWriteBackService.FileStamp(path)!;

        var second = await Service().WriteAsync(id, includeSidecar: false);

        Assert.Equal(MetadataWriteBackResult.SkippedAlreadyCurrent, second.Result);
        Assert.Equal(stampAfterFirst, MetadataFileWriteBackService.FileStamp(path));
    }

    [Fact]
    public async Task AForcedWrite_RewritesAnUnchangedFile()
    {
        var (_, id, _) = Seed();
        await Service().WriteAsync(id, includeSidecar: false);

        var forced = await Service().WriteAsync(id, includeSidecar: false, includeMetronInfo: false, force: true);

        Assert.Equal(MetadataWriteBackResult.Success, forced.Result);
    }

    [Fact]
    public async Task SeveralEditsBeforeTheWrite_ExportOnlyTheLatestValue()
    {
        var (_, id, path) = Seed();
        foreach (string publisher in new[] { "One", "Two", "Three" })
        {
            using var context = Context();
            context.Issues.Single(i => i.Id == id).Publisher = publisher;
            context.SaveChanges();
        }

        await Service().WriteAsync(id, includeSidecar: false);

        Assert.Equal("Three", ReadBack(path).Publisher);
    }

    [Fact]
    public async Task AFileChangedOutsidePaperbunkr_IsLeftAlone_UntilWriteAnyway()
    {
        var (_, id, path) = Seed();
        await Service().WriteAsync(id, includeSidecar: false);

        // Someone else rewrites the file, and the library value changes too.
        Recreate(path, new ComicInfo { Publisher = "Edited elsewhere" });
        using (var context = Context())
        {
            context.Issues.Single(i => i.Id == id).Publisher = "Y";
            context.SaveChanges();
        }

        var skipped = await Service().WriteAsync(id, includeSidecar: false);

        Assert.Equal(MetadataWriteBackResult.SkippedExternalModification, skipped.Result);
        Assert.Equal("Edited elsewhere", ReadBack(path).Publisher);
        Assert.Equal(EmbeddedSyncState.ConflictExternalModification, Reload(id).EmbeddedSyncState);

        var forced = await Service().WriteAsync(id, includeSidecar: false, includeMetronInfo: false, force: true);

        Assert.Equal(MetadataWriteBackResult.Success, forced.Result);
        Assert.Equal("Y", ReadBack(path).Publisher);
        Assert.Equal(EmbeddedSyncState.Synced, Reload(id).EmbeddedSyncState);
    }

    [Fact]
    public async Task ASeriesWithSyncOff_IsNotWritten_ButAForcedWriteStillWorks()
    {
        var (_, id, path) = Seed(configureSeries: s => s.EmbeddedSyncEnabled = false);

        var outcome = await Service().WriteAsync(id, includeSidecar: false);
        Assert.Equal(MetadataWriteBackResult.SkippedSyncDisabled, outcome.Result);
        Assert.True(string.IsNullOrEmpty(PublisherOf(path)));

        var forced = await Service().WriteAsync(id, includeSidecar: false, includeMetronInfo: false, force: true);
        Assert.Equal(MetadataWriteBackResult.Success, forced.Result);
    }

    [Fact]
    public async Task AChapterWithoutASynopsis_ExportsTheSeriesSummary_AndTheFingerprintFollowsIt()
    {
        var (seriesId, id, path) = Seed(configureSeries: s => s.Summary = "Series summary");
        await Service().WriteAsync(id, includeSidecar: false);
        Assert.Equal("Series summary", ReadBack(path).Summary);

        using (var context = Context())
        {
            context.Series.Single(s => s.Id == seriesId).Summary = "A changed series summary";
            context.SaveChanges();
        }

        var again = await Service().WriteAsync(id, includeSidecar: false);

        Assert.Equal(MetadataWriteBackResult.Success, again.Result);
        Assert.Equal("A changed series summary", ReadBack(path).Summary);
    }

    [Fact]
    public async Task AChapterWithItsOwnSynopsis_KeepsIt()
    {
        var (_, id, path) = Seed(configureSeries: s => s.Summary = "Series summary", configureIssue: i => i.Summary = "Own synopsis");

        await Service().WriteAsync(id, includeSidecar: false);

        Assert.Equal("Own synopsis", ReadBack(path).Summary);
    }

    [Fact]
    public async Task UnsupportedMissingAndReadOnlyFiles_AreReportedHonestly_NeverAsSynced()
    {
        var (_, cb7, _) = Seed("a.cb7", configureIssue: i => i.EmbeddedDirty = true);
        File.WriteAllBytes(Path.Combine(_dir, "a.cb7"), new byte[] { 1, 2, 3 });
        var (_, missing, _) = Seed("gone.cbz", createFile: false, configureIssue: i => i.EmbeddedDirty = true);
        var (_, readOnly, readOnlyPath) = Seed("ro.cbz", configureIssue: i => i.EmbeddedDirty = true);
        File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal(MetadataWriteBackResult.SkippedUnsupportedFormat, (await Service().WriteAsync(cb7, false)).Result);
            Assert.Equal(MetadataWriteBackResult.SkippedMissingFile, (await Service().WriteAsync(missing, false)).Result);
            Assert.Equal(MetadataWriteBackResult.SkippedReadOnly, (await Service().WriteAsync(readOnly, false)).Result);
        }
        finally
        {
            File.SetAttributes(readOnlyPath, FileAttributes.Normal);
        }

        var a = Reload(cb7);
        Assert.Equal(EmbeddedSyncState.UnsupportedFormat, a.EmbeddedSyncState);
        Assert.False(a.EmbeddedDirty);
        var b = Reload(missing);
        Assert.Equal(EmbeddedSyncState.SkippedMissingFile, b.EmbeddedSyncState);
        Assert.False(b.EmbeddedDirty);
        var c = Reload(readOnly);
        Assert.Equal(EmbeddedSyncState.SkippedReadOnly, c.EmbeddedSyncState);
        Assert.True(c.EmbeddedDirty);
    }

    [Fact]
    public async Task AComic_IsWrittenExactlyAsBefore_EveryTime_WithNoSyncStateRecorded()
    {
        var (_, id, _) = Seed(type: ContentType.Comic);

        var first = await Service().WriteAsync(id, includeSidecar: false);
        var second = await Service().WriteAsync(id, includeSidecar: false);

        Assert.Equal(MetadataWriteBackResult.Success, first.Result);
        Assert.Equal(MetadataWriteBackResult.Success, second.Result);
        var issue = Reload(id);
        Assert.Equal(EmbeddedSyncState.None, issue.EmbeddedSyncState);
        Assert.Null(issue.EmbeddedFingerprint);
    }

    // ---------- the queue ----------

    private MetadataWriteBackQueue Queue(IReadOnlyList<TimeSpan>? retryDelays = null) =>
        new(Context, Service(), (_, _) => { }, TimeSpan.FromMilliseconds(20), retryDelays: retryDelays ?? new[] { TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30) });

    private async Task WaitFor(Func<bool> condition, int timeoutMs = 25000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the queue.");
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task TheStartupCatchUp_WritesOnlyDirtyChapters_AndOnlyInSeriesWithSyncOn()
    {
        SetSettings();
        var (_, dirty, dirtyPath) = Seed("dirty.cbz", configureIssue: i => i.EmbeddedDirty = true);
        var (_, clean, cleanPath) = Seed("clean.cbz");
        var (_, off, offPath) = Seed("off.cbz", configureSeries: s => s.EmbeddedSyncEnabled = false, configureIssue: i => i.EmbeddedDirty = true);
        using var queue = Queue();

        int queued = queue.EnqueueDirty();
        await WaitFor(() => !Reload(dirty).EmbeddedDirty);

        Assert.Equal(1, queued);
        Assert.Equal("X", ReadBack(dirtyPath).Publisher);
        Assert.True(string.IsNullOrEmpty(PublisherOf(cleanPath)));
        Assert.True(string.IsNullOrEmpty(PublisherOf(offPath)));
        Assert.True(Reload(off).EmbeddedDirty);
    }

    [Fact]
    public async Task WorkThatIsStillOwed_SurvivesARestart()
    {
        SetSettings();
        var (_, id, path) = Seed(configureIssue: i => i.EmbeddedDirty = true);
        // "The app closed before the write": no queue ever ran. A new queue after the restart finds it from the database alone.
        using var afterRestart = Queue();

        afterRestart.EnqueueDirty();
        await WaitFor(() => Reload(id).EmbeddedSyncState == EmbeddedSyncState.Synced);

        Assert.Equal("X", ReadBack(path).Publisher);
    }

    [Fact]
    public async Task WithWriteBackOff_NothingIsWritten_AndTheChapterStaysOwed()
    {
        SetSettings(master: false, automatic: false);
        var (_, id, path) = Seed(configureIssue: i => i.EmbeddedDirty = true);
        using var queue = Queue();

        queue.EnqueueDirty();
        await Task.Delay(300);

        Assert.True(Reload(id).EmbeddedDirty);
        Assert.True(string.IsNullOrEmpty(PublisherOf(path)));
    }

    [Fact]
    public async Task AFailingWrite_IsRetriedThenMarkedNeedsAttention_AndStaysOwed()
    {
        SetSettings();
        var (_, id, path) = Seed(configureIssue: i => i.EmbeddedDirty = true);
        File.WriteAllBytes(path, new byte[] { 0, 1, 2, 3, 4, 5 });   // a .cbz that is not a zip: opening it fails
        using var queue = Queue();

        queue.Enqueue(id);
        await WaitFor(() => Reload(id).EmbeddedSyncState == EmbeddedSyncState.NeedsAttention);

        var issue = Reload(id);
        Assert.True(issue.EmbeddedDirty);
        Assert.False(string.IsNullOrEmpty(issue.EmbeddedSyncMessage));
    }

    [Fact]
    public async Task ALargeBatch_IsWrittenInFull_WithEachChapterRecordedSynced()
    {
        SetSettings();
        var ids = new List<int>();
        for (int n = 0; n < MetadataWriteBackQueue.ProgressJobThreshold + 5; n++)
        {
            ids.Add(Seed($"batch{n}.cbz", configureIssue: i => i.EmbeddedDirty = true).IssueId);
        }

        using var queue = Queue();
        queue.EnqueueDirty();
        await WaitFor(() => ids.All(id => Reload(id).EmbeddedSyncState == EmbeddedSyncState.Synced), timeoutMs: 30000);

        Assert.All(ids, id => Assert.False(Reload(id).EmbeddedDirty));
    }

    [Fact]
    public async Task AManualEnqueue_ForcesTheWrite_OfAChangedOutsideFile()
    {
        SetSettings();
        var (_, id, path) = Seed();
        await Service().WriteAsync(id, includeSidecar: false);
        Recreate(path, new ComicInfo { Publisher = "Edited elsewhere" });
        using var queue = Queue();

        queue.Enqueue(id, manual: true);
        await WaitFor(() => ReadBack(path).Publisher == "X");

        Assert.Equal(EmbeddedSyncState.Synced, Reload(id).EmbeddedSyncState);
    }
}
