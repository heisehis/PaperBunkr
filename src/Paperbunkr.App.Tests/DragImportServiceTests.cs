using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="DragImportService"/> (docs/superpowers/specs/
/// 2026-08-31-drag-and-drop-import-design.md) against real synthetic .cbz fixtures
/// (<see cref="CbzFixture"/>) and a real temp SQLite database - same "generate via the real code
/// path" precedent as <see cref="LibraryFolderScannerTests"/>. Covers the new glue: folder
/// expansion + <see cref="WatchedFolder"/> registration, extension bucketing, issue-id resolution
/// for already-in-library files, and one bad reading-list file not aborting a mixed batch. Real
/// on-screen drag-and-drop wiring is not automatable here (FlaUI can't simulate an OS file drag) -
/// flagged for a manual check, per the spec's Testing section.
/// </summary>
public class DragImportServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly string _root;

    public DragImportServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_dragimport_db_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_dragimport_root_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private DragImportService CreateService() =>
        new(() => new PaperbunkrDbContext(_dbOptions), new LibraryFolderScanner(() => new PaperbunkrDbContext(_dbOptions)));

    private PaperbunkrDbContext Db() => new(_dbOptions);

    [Fact]
    public async Task ImportAsync_LooseComicFiles_ImportsAndResolvesIssueIds()
    {
        string a = CbzFixture.Create(Path.Combine(_root, "Kilo Station 001 (2020).cbz"), pageCount: 1);
        string b = CbzFixture.Create(Path.Combine(_root, "Kilo Station 002 (2020).cbz"), pageCount: 1);

        var result = await CreateService().ImportAsync(new[] { a, b });

        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.AlreadyInLibrary);
        Assert.Equal(0, result.SkippedUnsupported);
        Assert.Equal(0, result.ReadingListsImported);
        Assert.Equal(2, result.IssueIds.Count);

        using var db = Db();
        Assert.Equal(2, db.Issues.Count());
    }

    [Fact]
    public async Task ImportAsync_DroppedFolder_ImportsContentsAndRegistersWatchedFolder()
    {
        CbzFixture.Create(Path.Combine(_root, "Kilo Station 001 (2020).cbz"), pageCount: 1);
        var nested = Directory.CreateDirectory(Path.Combine(_root, "more"));
        CbzFixture.Create(Path.Combine(nested.FullName, "Kilo Station 002 (2020).cbz"), pageCount: 1);

        var result = await CreateService().ImportAsync(new[] { _root });

        Assert.Equal(2, result.Imported);
        using var db = Db();
        var watched = Assert.Single(db.WatchedFolders);
        Assert.Equal(_root, watched.Path);
        Assert.False(watched.Watch);
    }

    [Fact]
    public async Task ImportAsync_DroppedFolderAlreadyRegistered_NoDuplicateWatchedFolder()
    {
        using (var seed = Db())
        {
            seed.WatchedFolders.Add(new WatchedFolder { Path = _root });
            seed.SaveChanges();
        }

        CbzFixture.Create(Path.Combine(_root, "Kilo Station 001 (2020).cbz"), pageCount: 1);

        await CreateService().ImportAsync(new[] { _root });

        using var db = Db();
        Assert.Single(db.WatchedFolders);
    }

    [Fact]
    public async Task ImportAsync_FileAlreadyInLibrary_ResolvesIssueIdWithoutReimporting()
    {
        string file = CbzFixture.Create(Path.Combine(_root, "Kilo Station 001 (2020).cbz"), pageCount: 1);
        var first = await CreateService().ImportAsync(new[] { file });
        int originalId = Assert.Single(first.IssueIds);

        var second = await CreateService().ImportAsync(new[] { file });

        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.AlreadyInLibrary);
        Assert.Equal(originalId, Assert.Single(second.IssueIds));
        using var db = Db();
        Assert.Single(db.Issues);
    }

    [Fact]
    public async Task ImportAsync_UnsupportedLooseFile_CountedAsSkipped()
    {
        string stray = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(stray, "not a comic");

        var result = await CreateService().ImportAsync(new[] { stray });

        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.SkippedUnsupported);
        Assert.Empty(result.IssueIds);
    }

    [Fact]
    public async Task ImportAsync_CsvFile_ImportsAsNewReadingList()
    {
        string csv = Path.Combine(_root, "arc.csv");
        await File.WriteAllLinesAsync(csv, new[] { "Series,Number", "Kilo Station,1", "Kilo Station,2" });

        var result = await CreateService().ImportAsync(new[] { csv });

        Assert.Equal(1, result.ReadingListsImported);
        using var db = Db();
        var list = Assert.Single(db.ReadingLists.Include(r => r.Items));
        Assert.Equal("arc", list.Name);
        Assert.Equal(2, list.Items.Count);
    }

    [Fact]
    public async Task ImportAsync_MalformedCbl_SkippedWithoutAbortingRestOfBatch()
    {
        string badCbl = Path.Combine(_root, "broken.cbl");
        await File.WriteAllTextAsync(badCbl, "<not-a-valid-cbl/>");
        string comic = CbzFixture.Create(Path.Combine(_root, "Kilo Station 001 (2020).cbz"), pageCount: 1);

        var result = await CreateService().ImportAsync(new[] { badCbl, comic });

        Assert.Equal(0, result.ReadingListsImported);
        Assert.Equal(1, result.Imported);
        Assert.Single(result.IssueIds);
    }

    [Fact]
    public async Task ImportAsync_MixedBatch_FolderPlusLooseFilePlusCsv_AllHandledInOneCall()
    {
        var comicsFolder = Directory.CreateDirectory(Path.Combine(_root, "library"));
        CbzFixture.Create(Path.Combine(comicsFolder.FullName, "Kilo Station 001 (2020).cbz"), pageCount: 1);
        string loose = CbzFixture.Create(Path.Combine(_root, "Kilo Station 002 (2020).cbz"), pageCount: 1);
        string csv = Path.Combine(_root, "arc.csv");
        await File.WriteAllLinesAsync(csv, new[] { "Series,Number", "Kilo Station,3" });

        var result = await CreateService().ImportAsync(new[] { comicsFolder.FullName, loose, csv });

        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.ReadingListsImported);
        Assert.Equal(2, result.IssueIds.Count);
        using var db = Db();
        Assert.Single(db.WatchedFolders);
        Assert.Equal(comicsFolder.FullName, db.WatchedFolders.Single().Path);
    }
}
