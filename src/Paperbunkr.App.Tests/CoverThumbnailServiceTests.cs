using System.Threading;
using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using SdColor = System.Drawing.Color;
using SdBitmap = System.Drawing.Bitmap;
using SdGraphics = System.Drawing.Graphics;
using SdImageFormat = System.Drawing.Imaging.ImageFormat;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="CoverThumbnailService"/> against a real synthetic .cbz (via
/// <see cref="CbzFixture"/>). Runs under <see cref="AvaloniaTestCollection"/> because
/// <see cref="Bitmap"/> construction/scaling needs a registered IPlatformRenderInterface.
/// Redirects <see cref="CoverThumbnailPaths.ThumbnailDirectory"/> to a temp folder so tests never
/// touch the real per-user thumbnail cache, and injects a throwaway SQLite context so the
/// custom-cover paths (which resolve an issue's fingerprint from the database) never hit the real
/// per-user database either.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CoverThumbnailServiceTests : IDisposable
{
    private readonly string _originalThumbnailDirectory;
    private readonly string _thumbnailDirectory;
    private readonly string _cbzPath;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public CoverThumbnailServiceTests()
    {
        _originalThumbnailDirectory = CoverThumbnailPaths.ThumbnailDirectory;
        _thumbnailDirectory = Path.Combine(Path.GetTempPath(), $"paperbunkr_thumbs_test_{Guid.NewGuid():N}");
        CoverThumbnailPaths.ThumbnailDirectory = _thumbnailDirectory;

        _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cover_test_{Guid.NewGuid():N}.cbz");

        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cover_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var seed = new PaperbunkrDbContext(_dbOptions);
        seed.Database.EnsureCreated();
    }

    public void Dispose()
    {
        CoverThumbnailPaths.ThumbnailDirectory = _originalThumbnailDirectory;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (File.Exists(_cbzPath)) File.Delete(_cbzPath);
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_thumbnailDirectory)) Directory.Delete(_thumbnailDirectory, recursive: true);
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

    private string CachePath(int issueId, string? filePath, long? fileSize = null) =>
        CoverThumbnailPaths.GetCachePath(CoverFingerprint.Stem(issueId, filePath, fileSize));

    [Fact]
    public void TryGenerateThumbnail_ProducesDecodableJpeg_AtFingerprintedPath()
    {
        CbzFixture.Create(_cbzPath, pageCount: 3);

        bool result = Service().TryGenerateThumbnail(issueId: 1, _cbzPath, fileSize: 123);

        Assert.True(result);
        string path = CachePath(1, _cbzPath, 123);
        string fileName = Path.GetFileName(path);
        Assert.Matches(@"^1-[0-9a-f]{8}\.jpg$", fileName); // {id}-{fingerprint}.jpg, not the bare {id}.jpg
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
        string path = CachePath(2, _cbzPath);
        var firstWrite = File.GetLastWriteTimeUtc(path);

        Assert.True(service.TryGenerateThumbnail(issueId: 2, _cbzPath));
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void TryGenerateThumbnail_SweepsStaleSibling_WhenTheIssuesFileChanged()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        var service = Service();

        // First generation: issue 5 backed by a file of size 100.
        service.TryGenerateThumbnail(issueId: 5, _cbzPath, fileSize: 100);
        string stalePath = CachePath(5, _cbzPath, 100);
        Assert.True(File.Exists(stalePath));

        // The issue's file identity changes (re-scan picked up a new size).
        service.TryGenerateThumbnail(issueId: 5, _cbzPath, fileSize: 200);
        string freshPath = CachePath(5, _cbzPath, 200);

        Assert.True(File.Exists(freshPath));
        Assert.False(File.Exists(stalePath)); // the old sibling was swept
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

    // --- Cover art override (docs/superpowers/specs/2026-08-23-cover-art-override-design.md) ---

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
    public void TrySetCustomCover_WritesDecodableJpeg_EvenWithoutAnyLinkedFile()
    {
        int issueId = AddIssue(filePath: null);
        string imagePath = CreateTestImage();
        try
        {
            bool result = Service().TrySetCustomCover(issueId, imagePath);

            Assert.True(result);
            string path = CachePath(issueId, null); // "{id}-nofile.jpg"
            Assert.True(File.Exists(path));
            using var bitmap = new Bitmap(path);
            Assert.True(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0);
        }
        finally
        {
            try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch (IOException) { }
        }
    }

    [Fact]
    public void TrySetCustomCover_OverwritesAnExistingCachedThumbnail()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        int issueId = AddIssue(_cbzPath, fileSize: 4242);
        var service = Service();
        service.TryGenerateThumbnail(issueId, _cbzPath, fileSize: 4242);
        string path = CachePath(issueId, _cbzPath, 4242);
        var originalWrite = File.GetLastWriteTimeUtc(path);

        string imagePath = CreateTestImage();
        try
        {
            Thread.Sleep(10);
            bool result = service.TrySetCustomCover(issueId, imagePath);

            Assert.True(result);
            Assert.True(File.GetLastWriteTimeUtc(path) > originalWrite);
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
            string path = CachePath(issueId, _cbzPath, 777);
            Assert.True(File.Exists(path));

            service.ResetCover(issueId, _cbzPath);

            Assert.True(File.Exists(path)); // regenerated from the linked file, not left blank
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
            Assert.NotEmpty(CoverThumbnailPaths.EnumerateForIssue(issueId));

            service.ResetCover(issueId, filePath: null);

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

            Assert.True(File.Exists(CachePath(goodId, _cbzPath, 10)));
            Assert.False(File.Exists(CachePath(corruptId, corruptPath, 3)));
            Assert.Equal((0, 2), reports.First());
            Assert.Equal((2, 2), reports.Last());

            var lastWrite = File.GetLastWriteTimeUtc(CachePath(goodId, _cbzPath, 10));
            await service.GenerateAllAsync(new Progress<(int Done, int Total)>());
            Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(CachePath(goodId, _cbzPath, 10)));
        }
        finally
        {
            try { if (File.Exists(corruptPath)) File.Delete(corruptPath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task GenerateAllAsync_AfterIdReuse_RegeneratesTheRightCover_AndGarbageCollectsTheOrphan()
    {
        // Simulate a library rebuild: the old issue that held a numeric id is gone, and a *different*
        // comic now holds that same id. Without the fingerprint the stale cover would be served forever.
        CbzFixture.Create(_cbzPath, pageCount: 1);
        string otherCbz = Path.Combine(Path.GetTempPath(), $"paperbunkr_cover_other_{Guid.NewGuid():N}.cbz");
        CbzFixture.Create(otherCbz, pageCount: 1);

        try
        {
            // A leftover cache file from "the previous library": id 411, a since-deleted file.
            string orphanStem = CoverFingerprint.Stem(411, "C:/old/deleted-comic.cbz", 999);
            Directory.CreateDirectory(_thumbnailDirectory);
            File.WriteAllBytes(CoverThumbnailPaths.GetCachePath(orphanStem), new byte[] { 1, 2, 3 });

            using (var context = new PaperbunkrDbContext(_dbOptions))
            {
                var series = context.Series.Add(new Series { Name = "Rebuilt Series" }).Entity;
                context.SaveChanges();
                // Force this issue onto id 411.
                var issue = new Issue { Id = 411, SeriesId = series.Id, Number = "1", FilePath = otherCbz, FileSize = 55 };
                context.Issues.Add(issue);
                context.SaveChanges();
            }

            await Service().GenerateAllAsync(new Progress<(int Done, int Total)>());

            Assert.True(File.Exists(CachePath(411, otherCbz, 55)));           // the real, current cover
            Assert.False(File.Exists(CoverThumbnailPaths.GetCachePath(orphanStem))); // orphan swept
        }
        finally
        {
            try { if (File.Exists(otherCbz)) File.Delete(otherCbz); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task GenerateAllAsync_GarbageCollectsAPreRework_BareIdFile()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        int issueId = AddIssue(_cbzPath, fileSize: 12);

        Directory.CreateDirectory(_thumbnailDirectory);
        string legacyPath = Path.Combine(_thumbnailDirectory, $"{issueId}.jpg"); // old naming
        File.WriteAllBytes(legacyPath, new byte[] { 4, 5, 6 });

        await Service().GenerateAllAsync(new Progress<(int Done, int Total)>());

        Assert.False(File.Exists(legacyPath));
        Assert.True(File.Exists(CachePath(issueId, _cbzPath, 12)));
    }
}
