using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="LibraryPathRepairService"/> - startup self-heal for comics whose <c>FilePath</c> got
/// repointed at a <c>~RF*.TMP</c> ReplaceFile backup by the pre-fix write-back / folder-watch bug.
/// </summary>
public class LibraryPathRepairServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _opts;
    private readonly string _root;

    public LibraryPathRepairServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_pathrepair_{Guid.NewGuid():N}.db");
        _opts = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var ctx = new PaperbunkrDbContext(_opts);
        ctx.Database.EnsureCreated();

        _root = Path.Combine(Path.GetTempPath(), $"paperbunkr_pathrepair_root_{Guid.NewGuid():N}");
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
        catch (IOException) { }
    }

    private int Seed(string filePath, bool missing = false)
    {
        using var ctx = new PaperbunkrDbContext(_opts);
        var series = ctx.Series.FirstOrDefault() ?? ctx.Series.Add(new Series { Name = "S" }).Entity;
        ctx.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", FilePath = filePath, FileIsMissing = missing };
        ctx.Issues.Add(issue);
        ctx.SaveChanges();
        return issue.Id;
    }

    private LibraryPathRepairService.RepairResult Run()
    {
        using var ctx = new PaperbunkrDbContext(_opts);
        return LibraryPathRepairService.RunOnce(ctx);
    }

    private Issue Get(int id)
    {
        using var ctx = new PaperbunkrDbContext(_opts);
        return ctx.Issues.Single(i => i.Id == id);
    }

    [Fact]
    public void Reconnects_ABackupPath_ToTheRealFileThatStillExists()
    {
        string real = Path.Combine(_root, "Kilo Station 012 (2021).cbz");
        File.WriteAllText(real, "comic");
        int id = Seed(real + "~RF7a3b1c9.TMP", missing: true);

        var result = Run();

        Assert.Equal(1, result.Reconnected);
        Assert.Equal(0, result.NeedsManualReview);
        var issue = Get(id);
        Assert.Equal(real, issue.FilePath);
        Assert.False(issue.FileIsMissing);
    }

    [Fact]
    public void FlagsMissing_WhenTheRealFileIsGone()
    {
        string real = Path.Combine(_root, "Gone 003.cbz"); // never created
        int id = Seed(real + "~RFdeadbeef.TMP");

        var result = Run();

        Assert.Equal(0, result.Reconnected);
        Assert.Equal(1, result.NeedsManualReview);
        Assert.True(Get(id).FileIsMissing);
    }

    [Fact]
    public void LeavesBothRows_WhenARescanAlreadyReimportedTheRealFile()
    {
        string real = Path.Combine(_root, "Dup 001.cbz");
        File.WriteAllText(real, "comic");
        int broken = Seed(real + "~RF11223344.TMP");
        int reimported = Seed(real);

        var result = Run();

        Assert.Equal(0, result.Reconnected);
        Assert.Equal(1, result.NeedsManualReview);
        Assert.EndsWith("~RF11223344.TMP", Get(broken).FilePath);   // untouched - not auto-merged
        Assert.Equal(real, Get(reimported).FilePath);
    }

    [Fact]
    public void IsIdempotent_ANoOpOnACleanLibrary()
    {
        string real = Path.Combine(_root, "Fine 001.cbz");
        File.WriteAllText(real, "comic");
        Seed(real);

        Assert.False(Run().DidSomething);
        Assert.False(Run().DidSomething);
    }
}
