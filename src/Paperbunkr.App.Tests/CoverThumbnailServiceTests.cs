using Avalonia.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

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
}
