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
}
