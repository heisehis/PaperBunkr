using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Library;

namespace Paperbunkr.Data.Tests;

/// <summary>2026-09-26 library audit: FileSize/FileModifiedTime/FileCreationTime were never populated (status bar read "0 MB").</summary>
public class IssueFileStatsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"paperbunkr_filestats_{Guid.NewGuid():N}");
    private readonly string _dbPath;

    public IssueFileStatsTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "test.db");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private PaperbunkrDbContext CreateContext()
    {
        var context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        context.Database.Migrate();
        return context;
    }

    private string WriteFile(string name, int bytes)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void TryApply_ReadsSizeAndUtcTimes()
    {
        string path = WriteFile("a.cbz", 1234);
        var issue = new Issue { FilePath = path };

        Assert.True(IssueFileStats.TryApply(issue));

        Assert.Equal(1234, issue.FileSize);
        Assert.Equal(File.GetLastWriteTimeUtc(path), issue.FileModifiedTime);
        Assert.Equal(File.GetCreationTimeUtc(path), issue.FileCreationTime);
    }

    [Fact]
    public void TryApply_MissingFileOrNoPath_LeavesIssueUntouched()
    {
        var missing = new Issue { FilePath = Path.Combine(_dir, "gone.cbz"), FileSize = 99 };
        var fileless = new Issue();

        Assert.False(IssueFileStats.TryApply(missing));
        Assert.False(IssueFileStats.TryApply(fileless));
        Assert.Equal(99, missing.FileSize);
        Assert.Null(fileless.FileSize);
    }

    [Fact]
    public void BackfillMissing_FillsOnlyRowsWithoutASize_AndIsIdempotent()
    {
        string path = WriteFile("b.cbz", 500);
        using (var context = CreateContext())
        {
            var series = new Series { Name = "S" };
            context.Series.Add(series);
            context.SaveChanges();
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", FilePath = path });
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = "2", FilePath = path, FileSize = 7 });
            context.Issues.Add(new Issue { SeriesId = series.Id, Number = "3" });
            context.SaveChanges();

            Assert.Equal(1, IssueFileStats.BackfillMissing(context));
            Assert.Equal(0, IssueFileStats.BackfillMissing(context));
        }

        using var verify = CreateContext();
        Assert.Equal(500, verify.Issues.Single(i => i.Number == "1").FileSize);
        Assert.Equal(7, verify.Issues.Single(i => i.Number == "2").FileSize);
        Assert.Null(verify.Issues.Single(i => i.Number == "3").FileSize);
    }
}
