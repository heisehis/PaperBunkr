using System.Threading;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.Covers;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SdColor = System.Drawing.Color;
using SdBitmap = System.Drawing.Bitmap;
using SdGraphics = System.Drawing.Graphics;
using SdImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="CoverThumbnailService"/> after the 2026-09-06 cover-durability root fix
/// (docs/superpowers/specs/2026-09-06-scheduled-tasks-and-cover-durability-design.md, Part 2):
/// covers are keyed by the bare id (<c>{id}.jpg</c>), a routine path change never touches them,
/// orphan cleanup <b>attics</b> rather than deletes, and user-picked covers live in their own
/// directory. Runs under <see cref="AvaloniaTestCollection"/> because <see cref="Bitmap"/> needs a
/// registered IPlatformRenderInterface. All caches + the state sidecar are redirected to temp
/// folders.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CoverThumbnailServiceTests : IDisposable
{
    private readonly string _originalThumbnailDirectory;
    private readonly string _originalCustomDirectory;
    private readonly string _originalStateFile;
    private readonly string _thumbnailDirectory;
    private readonly string _customDirectory;
    private readonly string _cbzPath;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public CoverThumbnailServiceTests()
    {
        _originalThumbnailDirectory = CoverThumbnailPaths.ThumbnailDirectory;
        _originalCustomDirectory = CustomCoverPaths.Directory;
        _originalStateFile = CoverCacheState.FilePath;

        string root = Path.Combine(Path.GetTempPath(), $"paperbunkr_thumbs_test_{Guid.NewGuid():N}");
        _thumbnailDirectory = Path.Combine(root, "thumbnails");
        _customDirectory = Path.Combine(root, "custom-covers");
        CoverThumbnailPaths.ThumbnailDirectory = _thumbnailDirectory;
        CustomCoverPaths.Directory = _customDirectory;
        CoverCacheState.FilePath = Path.Combine(root, "cover-cache-state.json");

        _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cover_test_{Guid.NewGuid():N}.cbz");

        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cover_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var seed = new PaperbunkrDbContext(_dbOptions);
        seed.Database.EnsureCreated();
    }

    public void Dispose()
    {
        string root = Path.GetDirectoryName(_thumbnailDirectory)!;
        CoverThumbnailPaths.ThumbnailDirectory = _originalThumbnailDirectory;
        CustomCoverPaths.Directory = _originalCustomDirectory;
        CoverCacheState.FilePath = _originalStateFile;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_cbzPath)) File.Delete(_cbzPath);
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private CoverThumbnailService Service() => new(() => new PaperbunkrDbContext(_dbOptions));

    private int AddIssue(string? filePath, long? fileSize = null)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = context.Series.FirstOrDefault() ?? context.Series.Add(new Series { Name = "Test Series" }).Entity;
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = filePath, FileSize = fileSize };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static string CachePath(int issueId) => CoverThumbnailPaths.GetCachePath(issueId);

    [Fact]
    public void TryGenerateThumbnail_ProducesDecodableJpeg_AtBareIdPath()
    {
        CbzFixture.Create(_cbzPath, pageCount: 3);

        bool result = Service().TryGenerateThumbnail(issueId: 1, _cbzPath, fileSize: 123);

        Assert.True(result);
        string path = CachePath(1);
        Assert.Equal("1.jpg", Path.GetFileName(path));
        Assert.True(File.Exists(path));
        using var bitmap = new Bitmap(path);
        Assert.True(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0);
    }

    [Fact]
    public void TryGenerateThumbnail_SkipsExisting_OnSecondCall()
    {
        CbzFixture.Create(_cbzPath, pageCount: 2);
        var service = Service();

        Assert.True(service.TryGenerateThumbnail(issueId: 2, _cbzPath));
        string path = CachePath(2);
        var firstWrite = File.GetLastWriteTimeUtc(path);

        Assert.True(service.TryGenerateThumbnail(issueId: 2, _cbzPath));
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void TryGenerateThumbnail_KeepsTheSameCover_WhenTheIssuesPathOrSizeChanges()
    {
        // The durability point: a metadata write-back / move changes FilePath + FileSize, but the
        // cover art is identical and the cache file must not be disturbed or regenerated.
        CbzFixture.Create(_cbzPath, pageCount: 1);
        var service = Service();

        service.TryGenerateThumbnail(issueId: 5, _cbzPath, fileSize: 100);
        string path = CachePath(5);
        Assert.True(File.Exists(path));
        var firstWrite = File.GetLastWriteTimeUtc(path);

        Thread.Sleep(10);
        service.TryGenerateThumbnail(issueId: 5, @"D:\moved\elsewhere.cbz", fileSize: 200);

        Assert.True(File.Exists(path));
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(path)); // untouched
        Assert.Single(CoverThumbnailPaths.EnumerateForIssue(5));  // exactly one file, still {id}.jpg
    }

    [Fact]
    public void TryGenerateThumbnail_ReturnsFalse_ForMissingFile()
    {
        bool result = Service().TryGenerateThumbnail(issueId: 3, Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.cbz"));

        Assert.False(result);
        Assert.Empty(CoverThumbnailPaths.EnumerateForIssue(3));
    }

    [Fact]
    public void TryGenerateThumbnail_ReturnsFalse_ForCorruptArchive()
    {
        File.WriteAllBytes(_cbzPath, new byte[] { 1, 2, 3, 4 });

        bool result = Service().TryGenerateThumbnail(issueId: 4, _cbzPath);

        Assert.False(result);
        Assert.Empty(CoverThumbnailPaths.EnumerateForIssue(4));
    }

    // --- Custom covers now live in their own directory, never swept ---

    private static string CreateTestImage(int width = 32, int height = 48)
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_custom_cover_test_{Guid.NewGuid():N}.png");
        using var bitmap = new SdBitmap(width, height);
        using (var g = SdGraphics.FromImage(bitmap))
        {
            g.Clear(SdColor.CornflowerBlue);
        }

        bitmap.Save(path, SdImageFormat.Png);
        return path;
    }

    [Fact]
    public void TrySetCustomCover_WritesToTheCustomDirectory_EvenWithoutAnyLinkedFile()
    {
        int issueId = AddIssue(filePath: null);
        string imagePath = CreateTestImage();
        try
        {
            bool result = Service().TrySetCustomCover(issueId, imagePath);

            Assert.True(result);
            Assert.True(CustomCoverPaths.Exists(issueId));
            Assert.False(File.Exists(CachePath(issueId))); // not in the generated dir
            using var bitmap = new Bitmap(CustomCoverPaths.GetCachePath(issueId));
            Assert.True(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0);
        }
        finally
        {
            try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch (IOException) { }
        }
    }

    [Fact]
    public void TrySetCustomCover_RemovesTheGeneratedCover_SoTheCustomArtWins()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        int issueId = AddIssue(_cbzPath, fileSize: 4242);
        var service = Service();
        service.TryGenerateThumbnail(issueId, _cbzPath, fileSize: 4242);
        Assert.True(File.Exists(CachePath(issueId)));

        string imagePath = CreateTestImage();
        try
        {
            Assert.True(service.TrySetCustomCover(issueId, imagePath));

            Assert.True(CustomCoverPaths.Exists(issueId));
            Assert.False(File.Exists(CachePath(issueId)));
        }
        finally
        {
            try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch (IOException) { }
        }
    }

    [Fact]
    public void CollectOrphans_NeverAtticsACustomCover()
    {
        // A custom cover for an id that isn't even in the DB must survive an orphan sweep.
        string imagePath = CreateTestImage();
        try
        {
            Assert.True(Service().TrySetCustomCover(777, imagePath));
            Service().GenerateAllAsync(new Progress<(int, int)>()).GetAwaiter().GetResult();
            Assert.True(CustomCoverPaths.Exists(777));
        }
        finally
        {
            try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch (IOException) { }
        }
    }

    [Fact]
    public void TrySetCustomCover_ReturnsFalse_ForUnreadableImagePath()
    {
        int issueId = AddIssue(filePath: null);

        bool result = Service().TrySetCustomCover(issueId, Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.png"));

        Assert.False(result);
    }

    [Fact]
    public void ResetCover_WithLinkedFile_RegeneratesFromTheRealPage()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        int issueId = AddIssue(_cbzPath, fileSize: 777);
        var service = Service();
        string imagePath = CreateTestImage();
        try
        {
            service.TrySetCustomCover(issueId, imagePath);
            Assert.True(CustomCoverPaths.Exists(issueId));

            service.ResetCover(issueId, _cbzPath);

            Assert.False(CustomCoverPaths.Exists(issueId));   // custom art gone
            Assert.True(File.Exists(CachePath(issueId)));     // regenerated from the linked file
        }
        finally
        {
            try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch (IOException) { }
        }
    }

    [Fact]
    public void ResetCover_WithNoLinkedFile_LeavesCoverBlank()
    {
        int issueId = AddIssue(filePath: null);
        var service = Service();
        string imagePath = CreateTestImage();
        try
        {
            service.TrySetCustomCover(issueId, imagePath);
            Assert.True(CustomCoverPaths.Exists(issueId));

            service.ResetCover(issueId, filePath: null);

            Assert.False(CustomCoverPaths.Exists(issueId));
            Assert.Empty(CoverThumbnailPaths.EnumerateForIssue(issueId));
        }
        finally
        {
            try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task GenerateAllAsync_SkipsCachedIssues_AndSkipsCorruptOnesWithoutStoppingBatch()
    {
        var corruptPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cover_corrupt_{Guid.NewGuid():N}.cbz");
        File.WriteAllBytes(corruptPath, new byte[] { 9, 9, 9 });
        CbzFixture.Create(_cbzPath, pageCount: 2);

        try
        {
            int goodId;
            int corruptId;
            using (var context = new PaperbunkrDbContext(_dbOptions))
            {
                var series = context.Series.Add(new Series { Name = "Batch Series" }).Entity;
                context.SaveChanges();
                var good = new Issue { SeriesId = series.Id, Number = "1", FilePath = _cbzPath, FileSize = 10 };
                var corrupt = new Issue { SeriesId = series.Id, Number = "2", FilePath = corruptPath, FileSize = 3 };
                context.Issues.AddRange(good, corrupt);
                context.SaveChanges();
                goodId = good.Id;
                corruptId = corrupt.Id;
            }

            var service = Service();
            var reports = new List<(int Done, int Total)>();
            await service.GenerateAllAsync(new Progress<(int Done, int Total)>(reports.Add));

            Assert.True(File.Exists(CachePath(goodId)));
            Assert.False(File.Exists(CachePath(corruptId)));
            Assert.Equal((0, 2), reports.First());
            Assert.Equal((2, 2), reports.Last());

            var lastWrite = File.GetLastWriteTimeUtc(CachePath(goodId));
            await service.GenerateAllAsync(new Progress<(int Done, int Total)>());
            Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(CachePath(goodId)));
        }
        finally
        {
            try { if (File.Exists(corruptPath)) File.Delete(corruptPath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task GenerateAllAsync_MovesAnIdLessCoverToTheAttic_AndKeepsALiveOne()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        int liveId = AddIssue(_cbzPath, fileSize: 55);

        Directory.CreateDirectory(_thumbnailDirectory);
        File.WriteAllBytes(CachePath(liveId), new byte[] { 1, 2, 3 });          // a live issue's cover
        string ghost = Path.Combine(_thumbnailDirectory, "99999.jpg");          // no such issue row
        File.WriteAllBytes(ghost, new byte[] { 4, 5, 6 });

        await Service().GenerateAllAsync(new Progress<(int Done, int Total)>());

        Assert.True(File.Exists(CachePath(liveId)));                            // kept
        Assert.False(File.Exists(ghost));                                      // swept...
        Assert.Contains(CoverThumbnailPaths.EnumerateAttic(), p => Path.GetFileName(p).StartsWith("99999.", StringComparison.Ordinal)); // ...to the attic, not deleted
    }

    [Fact]
    public async Task GenerateAllAsync_RecordsTheIssueCount_ForTheRebuildHeuristic()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        AddIssue(_cbzPath, fileSize: 1);
        AddIssue(_cbzPath, fileSize: 2);

        await Service().GenerateAllAsync(new Progress<(int Done, int Total)>());

        Assert.Equal(2, CoverCacheState.Read().IssueCount);
    }

    [Fact]
    public void TryGenerateThumbnail_Force_OverwritesEvenWhenFileAlreadyExists()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        var service = Service();
        Assert.True(service.TryGenerateThumbnail(issueId: 21, _cbzPath, fileSize: 30));
        string path = CachePath(21);
        var firstWrite = File.GetLastWriteTimeUtc(path);

        Thread.Sleep(10);
        Assert.True(service.TryGenerateThumbnail(issueId: 21, _cbzPath, fileSize: 30, force: true));

        Assert.True(File.GetLastWriteTimeUtc(path) > firstWrite);
    }

    [Fact]
    public async Task VerifyAllAsync_ReDecodesOnly_WhenTheSourceIsNewerThanTheCover()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        int unchangedId = AddIssue(_cbzPath, fileSize: 40);
        Service().TryGenerateThumbnail(unchangedId, _cbzPath, fileSize: 40);
        string unchangedPath = CachePath(unchangedId);

        // Make the cover clearly newer than the source so it is NOT re-decoded.
        File.SetLastWriteTimeUtc(unchangedPath, DateTime.UtcNow.AddHours(1));
        var unchangedWrite = File.GetLastWriteTimeUtc(unchangedPath);

        // A second issue whose source is newer than its (older) cached cover.
        int changedId = AddIssue(_cbzPath, fileSize: 40);
        Service().TryGenerateThumbnail(changedId, _cbzPath, fileSize: 40);
        string changedPath = CachePath(changedId);
        File.SetLastWriteTimeUtc(changedPath, DateTime.UtcNow.AddHours(-2));
        var changedWriteBefore = File.GetLastWriteTimeUtc(changedPath);

        await Service().VerifyAllAsync(new Progress<(int Done, int Total)>());

        Assert.Equal(unchangedWrite, File.GetLastWriteTimeUtc(unchangedPath)); // untouched
        Assert.True(File.GetLastWriteTimeUtc(changedPath) > changedWriteBefore); // re-decoded
    }

    [Fact]
    public async Task VerifyAllAsync_KeepsACoverWhoseSourceIsCurrentlyUnreadable()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        int id = AddIssue(_cbzPath, fileSize: 15);
        Service().TryGenerateThumbnail(id, _cbzPath, fileSize: 15);
        string coverPath = CachePath(id);
        File.SetLastWriteTimeUtc(coverPath, DateTime.UtcNow.AddHours(-5));

        File.Delete(_cbzPath); // source goes away

        await Service().VerifyAllAsync(new Progress<(int Done, int Total)>());

        Assert.True(File.Exists(coverPath)); // not lost just because the drive is offline
    }

    [Fact]
    public async Task RepairMissingAsync_RegeneratesOnlyBlankCoversWithAReadableSource()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        int blankWithSource = AddIssue(_cbzPath, fileSize: 1);
        int alreadyHasCover = AddIssue(_cbzPath, fileSize: 2);
        Service().TryGenerateThumbnail(alreadyHasCover, _cbzPath, fileSize: 2);
        var existingWrite = File.GetLastWriteTimeUtc(CachePath(alreadyHasCover));

        string goneSource = Path.Combine(Path.GetTempPath(), $"gone_{Guid.NewGuid():N}.cbz");
        int blankNoSource = AddIssue(goneSource, fileSize: 3);

        int custom = AddIssue(_cbzPath, fileSize: 4);
        string img = CreateTestImage();
        try
        {
            Service().TrySetCustomCover(custom, img);

            Thread.Sleep(10);
            await Service().RepairMissingAsync(new Progress<(int Done, int Total)>());

            Assert.True(File.Exists(CachePath(blankWithSource)));                       // fixed
            Assert.Equal(existingWrite, File.GetLastWriteTimeUtc(CachePath(alreadyHasCover))); // untouched
            Assert.False(File.Exists(CachePath(blankNoSource)));                        // source unavailable -> skipped
            Assert.False(File.Exists(CachePath(custom)));                               // has a custom cover -> skipped
        }
        finally
        {
            try { if (File.Exists(img)) File.Delete(img); } catch (IOException) { }
        }
    }

    // --- Cover aspect ratio (docs/superpowers/specs/2026-09-03-panorama-variable-width-design.md) ---

    [Fact]
    public async Task GenerateAllAsync_PersistsCoverAspectRatio_FromTheSourcePage()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_aspect_db_{Guid.NewGuid():N}.db");
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new System.Drawing.Size(400, 600));

        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={dbPath}").Options;
            int issueId;
            using (var context = new PaperbunkrDbContext(options))
            {
                context.Database.EnsureCreated();
                var series = new Series { Name = "S" };
                context.Series.Add(series);
                context.SaveChanges();
                var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = _cbzPath };
                context.Issues.Add(issue);
                context.SaveChanges();
                issueId = issue.Id;
            }

            await new CoverThumbnailService(() => new PaperbunkrDbContext(options))
                .GenerateAllAsync(new Progress<(int Done, int Total)>());

            using (var context = new PaperbunkrDbContext(options))
            {
                double? ratio = context.Issues.Find(issueId)!.CoverAspectRatio;
                Assert.NotNull(ratio);
                Assert.Equal(400.0 / 600.0, ratio!.Value, precision: 2);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task BackfillAspectRatios_FillsRowsWithACachedCoverButNoStoredRatio()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_aspect_backfill_{Guid.NewGuid():N}.db");
        CbzFixture.Create(_cbzPath, pageCount: 1, pageSize: _ => new System.Drawing.Size(500, 300));

        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={dbPath}").Options;
            int withCover;
            int withoutCover;
            using (var context = new PaperbunkrDbContext(options))
            {
                context.Database.EnsureCreated();
                var series = new Series { Name = "S" };
                context.Series.Add(series);
                context.SaveChanges();
                var a = new Issue { SeriesId = series.Id, Number = "1", FilePath = _cbzPath };
                var b = new Issue { SeriesId = series.Id, Number = "2" };
                context.Issues.AddRange(a, b);
                context.SaveChanges();
                withCover = a.Id;
                withoutCover = b.Id;
            }

            var service = new CoverThumbnailService(() => new PaperbunkrDbContext(options));
            service.TryGenerateThumbnail(withCover, _cbzPath);
            using (var context = new PaperbunkrDbContext(options))
            {
                context.Issues.Find(withCover)!.CoverAspectRatio = null;
                context.SaveChanges();
            }

            await service.BackfillAspectRatios();

            using (var context = new PaperbunkrDbContext(options))
            {
                Assert.NotNull(context.Issues.Find(withCover)!.CoverAspectRatio);
                Assert.Equal(500.0 / 300.0, context.Issues.Find(withCover)!.CoverAspectRatio!.Value, precision: 1);
                Assert.Null(context.Issues.Find(withoutCover)!.CoverAspectRatio);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
