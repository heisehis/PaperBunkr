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
/// <see cref="CbzFixture"/>, the same "generate via the real code path" precedent used by
/// <see cref="PageImageDecoderTests"/>). Runs under <see cref="AvaloniaTestCollection"/> for the
/// same reason - <see cref="Bitmap"/> construction/scaling needs a registered
/// IPlatformRenderInterface. Redirects <see cref="CoverThumbnailPaths.ThumbnailDirectory"/> to a
/// temp folder so tests never touch the real per-user thumbnail cache on this machine.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class CoverThumbnailServiceTests : IDisposable
{
    private readonly string _originalThumbnailDirectory;
    private readonly string _thumbnailDirectory;
    private readonly string _cbzPath;

    public CoverThumbnailServiceTests()
    {
        _originalThumbnailDirectory = CoverThumbnailPaths.ThumbnailDirectory;
        _thumbnailDirectory = Path.Combine(Path.GetTempPath(), $"paperbunkr_thumbs_test_{Guid.NewGuid():N}");
        CoverThumbnailPaths.ThumbnailDirectory = _thumbnailDirectory;

        _cbzPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cover_test_{Guid.NewGuid():N}.cbz");
    }

    public void Dispose()
    {
        CoverThumbnailPaths.ThumbnailDirectory = _originalThumbnailDirectory;

        try
        {
            if (File.Exists(_cbzPath)) File.Delete(_cbzPath);
            if (Directory.Exists(_thumbnailDirectory)) Directory.Delete(_thumbnailDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TryGenerateThumbnail_ProducesDecodableJpeg_FromRealCbz()
    {
        CbzFixture.Create(_cbzPath, pageCount: 3);
        var service = new CoverThumbnailService();

        bool result = service.TryGenerateThumbnail(issueId: 1, _cbzPath);

        Assert.True(result);
        string path = CoverThumbnailPaths.GetCachePath(1);
        Assert.True(File.Exists(path));
        using var bitmap = new Bitmap(path);
        Assert.True(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0);
    }

    [Fact]
    public void TryGenerateThumbnail_SkipsExisting_OnSecondCall()
    {
        CbzFixture.Create(_cbzPath, pageCount: 2);
        var service = new CoverThumbnailService();

        Assert.True(service.TryGenerateThumbnail(issueId: 2, _cbzPath));
        string path = CoverThumbnailPaths.GetCachePath(2);
        var firstWrite = File.GetLastWriteTimeUtc(path);

        Assert.True(service.TryGenerateThumbnail(issueId: 2, _cbzPath));
        var secondWrite = File.GetLastWriteTimeUtc(path);

        Assert.Equal(firstWrite, secondWrite);
    }

    [Fact]
    public void TryGenerateThumbnail_ReturnsFalse_ForMissingFile()
    {
        var service = new CoverThumbnailService();

        bool result = service.TryGenerateThumbnail(issueId: 3, Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.cbz"));

        Assert.False(result);
        Assert.False(File.Exists(CoverThumbnailPaths.GetCachePath(3)));
    }

    [Fact]
    public void TryGenerateThumbnail_ReturnsFalse_ForCorruptArchive()
    {
        File.WriteAllBytes(_cbzPath, new byte[] { 1, 2, 3, 4 });
        var service = new CoverThumbnailService();

        bool result = service.TryGenerateThumbnail(issueId: 4, _cbzPath);

        Assert.False(result);
        Assert.False(File.Exists(CoverThumbnailPaths.GetCachePath(4)));
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
        string imagePath = CreateTestImage();
        try
        {
            var service = new CoverThumbnailService();

            bool result = service.TrySetCustomCover(issueId: 10, imagePath);

            Assert.True(result);
            string path = CoverThumbnailPaths.GetCachePath(10);
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
        var service = new CoverThumbnailService();
        service.TryGenerateThumbnail(issueId: 11, _cbzPath);
        string path = CoverThumbnailPaths.GetCachePath(11);
        var originalWrite = File.GetLastWriteTimeUtc(path);

        string imagePath = CreateTestImage();
        try
        {
            Thread.Sleep(10); // ensure a distinguishable write timestamp on fast filesystems
            bool result = service.TrySetCustomCover(11, imagePath);

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
        var service = new CoverThumbnailService();

        bool result = service.TrySetCustomCover(issueId: 12, Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}.png"));

        Assert.False(result);
    }

    [Fact]
    public void ResetCover_WithLinkedFile_RegeneratesFromTheRealPage()
    {
        CbzFixture.Create(_cbzPath, pageCount: 1);
        var service = new CoverThumbnailService();
        string imagePath = CreateTestImage();
        try
        {
            service.TrySetCustomCover(13, imagePath);
            string path = CoverThumbnailPaths.GetCachePath(13);
            Assert.True(File.Exists(path));

            service.ResetCover(13, _cbzPath);

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
        var service = new CoverThumbnailService();
        string imagePath = CreateTestImage();
        try
        {
            service.TrySetCustomCover(14, imagePath);
            string path = CoverThumbnailPaths.GetCachePath(14);
            Assert.True(File.Exists(path));

            service.ResetCover(14, filePath: null);

            Assert.False(File.Exists(path));
        }
        finally
        {
            try { if (File.Exists(imagePath)) File.Delete(imagePath); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task GenerateAllAsync_SkipsCachedIssues_AndSkipsCorruptOnesWithoutStoppingBatch()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cover_db_test_{Guid.NewGuid():N}.db");
        var corruptPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_cover_corrupt_{Guid.NewGuid():N}.cbz");
        File.WriteAllBytes(corruptPath, new byte[] { 9, 9, 9 });
        CbzFixture.Create(_cbzPath, pageCount: 2);

        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={dbPath}").Options;
            int goodIssueId;
            int corruptIssueId;
            using (var context = new PaperbunkrDbContext(options))
            {
                context.Database.EnsureCreated();
                var series = new Series { Name = "Test Series" };
                context.Series.Add(series);
                context.SaveChanges();

                var good = new Issue { SeriesId = series.Id, Number = "1", FilePath = _cbzPath };
                var corrupt = new Issue { SeriesId = series.Id, Number = "2", FilePath = corruptPath };
                context.Issues.AddRange(good, corrupt);
                context.SaveChanges();
                goodIssueId = good.Id;
                corruptIssueId = corrupt.Id;
            }

            var service = new CoverThumbnailService(() => new PaperbunkrDbContext(options));
            var reports = new List<(int Done, int Total)>();
            var progress = new Progress<(int Done, int Total)>(reports.Add);

            await service.GenerateAllAsync(progress);

            Assert.True(File.Exists(CoverThumbnailPaths.GetCachePath(goodIssueId)));
            Assert.False(File.Exists(CoverThumbnailPaths.GetCachePath(corruptIssueId)));
            Assert.Equal((0, 2), reports.First());
            Assert.Equal((2, 2), reports.Last());

            var lastWrite = File.GetLastWriteTimeUtc(CoverThumbnailPaths.GetCachePath(goodIssueId));
            await service.GenerateAllAsync(new Progress<(int Done, int Total)>());
            Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(CoverThumbnailPaths.GetCachePath(goodIssueId)));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(dbPath)) File.Delete(dbPath);
                if (File.Exists(corruptPath)) File.Delete(corruptPath);
            }
            catch (IOException)
            {
            }
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

            // The upgrade case: a cached cover file exists but nothing recorded its ratio. Generate
            // the JPEG, then clear the ratio the generation path just persisted.
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
                Assert.Null(context.Issues.Find(withoutCover)!.CoverAspectRatio); // no cover file -> left alone
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch (IOException) { }
        }
    }
}
