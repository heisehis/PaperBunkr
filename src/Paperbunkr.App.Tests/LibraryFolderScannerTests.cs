using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="LibraryFolderScanner"/> (docs/superpowers/specs/
/// 2026-08-07-preferences-libraries-tab-design.md §2) against real synthetic .cbz fixtures
/// (<see cref="CbzFixture"/>, same "generate via the real code path" precedent used throughout
/// this codebase). Filename-parsing correctness itself is <c>ComicNameInfo</c>'s own already-ported
/// CE concern - these tests focus on the new glue: extension filtering, idempotent re-scan, and
/// series find-or-create.
/// </summary>
public class LibraryFolderScannerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _scanRoot;

    public LibraryFolderScannerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_scanner_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _scanRoot = Path.Combine(Path.GetTempPath(), $"paperbunkr_scanner_root_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scanRoot);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_scanRoot)) Directory.Delete(_scanRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private LibraryFolderScanner CreateScanner() => new(() => new PaperbunkrDbContext(_dbOptions));

    private void AddWatchedFolder(string path)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.WatchedFolders.Add(new WatchedFolder { Path = path });
        context.SaveChanges();
    }

    [Fact]
    public async Task ScanAllAsync_CreatesSeriesAndIssue_FromParsedFilename()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.IssuesAdded);
        Assert.Equal(1, result.SeriesTouched);

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal("Kilo Station", series.Name);
        var issue = Assert.Single(context.Issues);
        Assert.Equal("12", issue.Number);
        Assert.Equal(2021, issue.Year);
        Assert.Equal(series.Id, issue.SeriesId);
    }

    [Fact]
    public async Task ScanAllAsync_ReScanning_IsIdempotent_NoDuplicates()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 012 (2021).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);
        var scanner = CreateScanner();
        await scanner.ScanAllAsync(new Progress<(int, int)>());

        var second = await scanner.ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(0, second.IssuesAdded);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.Issues);
    }

    [Fact]
    public async Task ScanAllAsync_MultipleNewIssuesSameSeries_ShareOneSeriesRow()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 001 (2020).cbz"), pageCount: 1);
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 002 (2020).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(2, result.IssuesAdded);
        Assert.Equal(1, result.SeriesTouched);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(context.Series);
        Assert.Equal(2, context.Issues.Count());
    }

    [Fact]
    public async Task ScanAllAsync_ExistingSeries_AddsIssueToIt_NotADuplicateSeries()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Series.Add(new Series { Name = "Kilo Station" });
            context.SaveChanges();
        }

        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 003 (2022).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var verify = new PaperbunkrDbContext(_dbOptions);
        Assert.Single(verify.Series);
        Assert.Single(verify.Issues);
    }

    [Fact]
    public async Task ScanAllAsync_IgnoresUnsupportedExtensions()
    {
        File.WriteAllText(Path.Combine(_scanRoot, "notes.txt"), "hello");
        AddWatchedFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(0, result.IssuesAdded);
    }

    [Fact]
    public async Task ScanAllAsync_ReportsProgress()
    {
        CbzFixture.Create(Path.Combine(_scanRoot, "Kilo Station 001 (2020).cbz"), pageCount: 1);
        AddWatchedFolder(_scanRoot);
        var reports = new List<(int Done, int Total)>();

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>(reports.Add));

        Assert.Equal((0, 1), reports.First());
        Assert.Equal((1, 1), reports.Last());
    }

    [Fact]
    public async Task ScanAllAsync_EmbeddedComicInfo_WinsOverMisleadingFilename()
    {
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Series = "Real Series",
            Number = "3",
            Volume = 2,
            Year = 2023,
            Writer = "Real Writer",
            Publisher = "Real Publisher",
        };
        CbzFixture.Create(Path.Combine(_scanRoot, "Wrong Series 099 (1999).cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.IssuesAdded);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal("Real Series", series.Name);
        var issue = Assert.Single(context.Issues);
        Assert.Equal("3", issue.Number);
        Assert.Equal(2, issue.Volume);
        Assert.Equal(2023, issue.Year);
        Assert.Equal("Real Writer", issue.Writer);
        Assert.Equal("Real Publisher", issue.Publisher);
    }

    [Fact]
    public async Task ScanAllAsync_EmbeddedComicInfoMissingAField_FallsBackToFilenameForThatFieldOnly()
    {
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Series = "Real Series",
            // Number/Volume/Year deliberately left unset - filename parsing should fill these in.
        };
        CbzFixture.Create(Path.Combine(_scanRoot, "Ignored Name 007 (2018).cbz"), pageCount: 1, embedded);
        AddWatchedFolder(_scanRoot);

        await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal("Real Series", series.Name); // embedded won
        var issue = Assert.Single(context.Issues);
        Assert.Equal("7", issue.Number); // filename fallback
        Assert.Equal(2018, issue.Year); // filename fallback
    }

    [Fact]
    public async Task ScanAllAsync_MalformedComicInfoXml_FallsBackToFilenameParsing()
    {
        string path = Path.Combine(_scanRoot, "Kilo Station 005 (2019).cbz");
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            var page = zip.CreateEntry("page_000.png");
            using (var pageStream = page.Open())
            {
                using var bitmap = new System.Drawing.Bitmap(16, 16);
                bitmap.Save(pageStream, System.Drawing.Imaging.ImageFormat.Png);
            }

            var infoEntry = zip.CreateEntry("ComicInfo.xml");
            using var infoStream = infoEntry.Open();
            byte[] garbage = "<not-valid-xml this is broken"u8.ToArray();
            infoStream.Write(garbage, 0, garbage.Length);
        }
        AddWatchedFolder(_scanRoot);

        var result = await CreateScanner().ScanAllAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.IssuesAdded);
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = Assert.Single(context.Series);
        Assert.Equal("Kilo Station", series.Name);
        var issue = Assert.Single(context.Issues);
        Assert.Equal("5", issue.Number);
        Assert.Equal(2019, issue.Year);
    }

    [Fact]
    public async Task SyncMetadataAsync_FillsBlankFields_PreservesExistingOnes()
    {
        string cbzPath = Path.Combine(_scanRoot, "already-linked.cbz");
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo
        {
            Writer = "Embedded Writer",
            Publisher = "Embedded Publisher",
        };
        CbzFixture.Create(cbzPath, pageCount: 1, embedded);

        int issueId;
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Existing Series" };
            context.Series.Add(series);
            var issue = new Issue { Series = series, FilePath = cbzPath, Writer = "Existing Writer" };
            context.Issues.Add(issue);
            context.SaveChanges();
            issueId = issue.Id;
        }

        var result = await CreateScanner().SyncMetadataAsync(new Progress<(int, int)>());

        Assert.Equal(1, result.IssuesUpdated);
        using var verify = new PaperbunkrDbContext(_dbOptions);
        var updated = verify.Issues.Single(i => i.Id == issueId);
        Assert.Equal("Existing Writer", updated.Writer); // preserved
        Assert.Equal("Embedded Publisher", updated.Publisher); // filled in
    }

    [Fact]
    public async Task SyncMetadataAsync_MissingFile_SkippedGracefully()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Existing Series" };
            context.Series.Add(series);
            context.Issues.Add(new Issue { Series = series, FilePath = Path.Combine(_scanRoot, "does-not-exist.cbz") });
            context.SaveChanges();
        }

        var result = await CreateScanner().SyncMetadataAsync(new Progress<(int, int)>());

        Assert.Equal(0, result.IssuesUpdated);
    }

    [Fact]
    public async Task SyncMetadataAsync_ReRunAfterFullSync_ReportsNoFurtherUpdates()
    {
        string cbzPath = Path.Combine(_scanRoot, "already-linked.cbz");
        var embedded = new cYo.Projects.ComicRack.Engine.ComicInfo { Writer = "Embedded Writer" };
        CbzFixture.Create(cbzPath, pageCount: 1, embedded);

        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            var series = new Series { Name = "Existing Series" };
            context.Series.Add(series);
            context.Issues.Add(new Issue { Series = series, FilePath = cbzPath });
            context.SaveChanges();
        }

        var scanner = CreateScanner();
        var first = await scanner.SyncMetadataAsync(new Progress<(int, int)>());
        var second = await scanner.SyncMetadataAsync(new Progress<(int, int)>());

        Assert.Equal(1, first.IssuesUpdated);
        Assert.Equal(0, second.IssuesUpdated);
    }
}
