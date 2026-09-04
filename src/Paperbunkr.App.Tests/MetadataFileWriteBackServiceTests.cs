using System;
using System.IO;
using System.IO.Compression;
using cYo.Projects.ComicRack.Engine;
using cYo.Projects.ComicRack.Engine.IO.Provider;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.CeMigration;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="MetadataFileWriteBackService"/> against real synthetic archives
/// (<see cref="CbzFixture"/>) - the whole point is a real in-place archive rewrite, so a real
/// round-trip is the only meaningful test. Replaces the old <c>ComicInfoWriteBackServiceTests</c>.
/// Real on-screen drag-a-file end-to-end is a manual check (docs/superpowers/specs/2026-09-03-file-
/// metadata-write-back-design.md).
/// </summary>
public class MetadataFileWriteBackServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _dir;

    public MetadataFileWriteBackServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_mfwb_db_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_mfwb_{Guid.NewGuid():N}");
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

    private MetadataFileWriteBackService Service() => new(() => new PaperbunkrDbContext(_dbOptions));

    private int SeedIssue(string filePath, Action<Issue>? configure = null)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Kilo Station" };
        context.Series.Add(series);
        context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = filePath };
        configure?.Invoke(issue);
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static ComicInfo ReadBack(string path)
    {
        using var provider = Providers.Readers.CreateSourceProvider(path);
        provider.Open(async: false);
        return ((IInfoStorage)provider).LoadInfo(InfoLoadingMethod.Complete);
    }

    [Fact]
    public async Task WriteAsync_RoundTripsFields_AndPreservesUnmodeledElements()
    {
        string cbz = Path.Combine(_dir, "book.cbz");
        CbzFixture.Create(cbz, pageCount: 2, new ComicInfo { Summary = "old", AlternateCount = 7 });
        int id = SeedIssue(cbz, i =>
        {
            i.Summary = "A new summary.";
            i.Writer = "Jane Writer";
            i.PageCount = 2;
        });

        var outcome = await Service().WriteAsync(id, includeSidecar: false);

        Assert.Equal(MetadataWriteBackResult.Success, outcome.Result);
        var info = ReadBack(cbz);
        Assert.Equal("A new summary.", info.Summary);
        Assert.Equal("Jane Writer", info.Writer);
        Assert.Equal("Kilo Station", info.Series);
        Assert.Equal(7, info.AlternateCount); // unmodeled - survived
        Assert.Equal(2, info.PageCount);
    }

    [Fact]
    public async Task WriteAsync_Sidecar_WrittenAndParseable()
    {
        string cbz = Path.Combine(_dir, "sidecar.cbz");
        CbzFixture.Create(cbz, pageCount: 1);
        int id = SeedIssue(cbz, i => { i.Rating = 4f; i.IsFinalIssue = true; });

        await Service().WriteAsync(id, includeSidecar: true);

        using var zip = ZipFile.OpenRead(cbz);
        var entry = zip.GetEntry("paperbunkr.json");
        Assert.NotNull(entry);
        using var stream = entry!.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var parsed = PaperbunkrSidecar.TryParse(ms.ToArray());
        Assert.NotNull(parsed);
        Assert.Equal(4f, parsed!.Rating);
        Assert.True(parsed.IsFinalIssue);
    }

    [Fact]
    public async Task WriteAsync_NoSidecar_NoJsonEntry()
    {
        string cbz = Path.Combine(_dir, "nosidecar.cbz");
        CbzFixture.Create(cbz, pageCount: 1);
        int id = SeedIssue(cbz);

        await Service().WriteAsync(id, includeSidecar: false);

        using var zip = ZipFile.OpenRead(cbz);
        Assert.Null(zip.GetEntry("paperbunkr.json"));
    }

    [Fact]
    public async Task WriteAsync_Cbr_SkippedUnsupportedFormat()
    {
        string cbr = Path.Combine(_dir, "book.cbr");
        File.WriteAllText(cbr, "not a real rar");
        int id = SeedIssue(cbr);

        var outcome = await Service().WriteAsync(id, includeSidecar: false);

        Assert.Equal(MetadataWriteBackResult.SkippedUnsupportedFormat, outcome.Result);
        Assert.Equal("not a real rar", File.ReadAllText(cbr));
    }

    [Fact]
    public async Task WriteAsync_MissingFile_SkippedMissingFile()
    {
        int id = SeedIssue(Path.Combine(_dir, "gone.cbz"));
        var outcome = await Service().WriteAsync(id, includeSidecar: false);
        Assert.Equal(MetadataWriteBackResult.SkippedMissingFile, outcome.Result);
    }

    [Fact]
    public async Task WriteAsync_Placeholder_SkippedMissingFile()
    {
        int id = SeedIssue(Path.Combine(_dir, "x.cbz"), i => { i.IsPlaceholder = true; i.FilePath = null; });
        var outcome = await Service().WriteAsync(id, includeSidecar: false);
        Assert.Equal(MetadataWriteBackResult.SkippedMissingFile, outcome.Result);
    }

    [Fact]
    public async Task WriteAsync_ReadOnlyFile_SkippedReadOnly()
    {
        string cbz = Path.Combine(_dir, "ro.cbz");
        CbzFixture.Create(cbz, pageCount: 1);
        int id = SeedIssue(cbz);
        var fi = new FileInfo(cbz) { IsReadOnly = true };
        try
        {
            var outcome = await Service().WriteAsync(id, includeSidecar: false);
            Assert.Equal(MetadataWriteBackResult.SkippedReadOnly, outcome.Result);
        }
        finally
        {
            fi.IsReadOnly = false;
        }
    }

    [Fact]
    public async Task WriteAsync_FolderOfImages_WritesFilesIntoFolder()
    {
        string folder = Path.Combine(_dir, "folder-comic");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "001.jpg"), new byte[] { 1, 2, 3 });
        int id = SeedIssue(folder, i => i.Summary = "Folder summary.");

        var outcome = await Service().WriteAsync(id, includeSidecar: true);

        Assert.Equal(MetadataWriteBackResult.Success, outcome.Result);
        Assert.True(File.Exists(Path.Combine(folder, "ComicInfo.xml")));
        Assert.True(File.Exists(Path.Combine(folder, "paperbunkr.json")));
    }

    [Fact]
    public async Task WriteAsync_CorruptArchive_Failed_DoesNotThrow()
    {
        string cbz = Path.Combine(_dir, "corrupt.cbz");
        File.WriteAllBytes(cbz, new byte[] { 0x50, 0x4B, 0x00, 0x00, 0xFF, 0xFF }); // PK signature, junk body
        int id = SeedIssue(cbz);

        var outcome = await Service().WriteAsync(id, includeSidecar: false);

        Assert.Contains(outcome.Result, new[] { MetadataWriteBackResult.Failed, MetadataWriteBackResult.Success });
        // The point is no throw - if 7-Zip repairs/rewrites it that's Success, if it rejects it that's Failed.
    }
}
