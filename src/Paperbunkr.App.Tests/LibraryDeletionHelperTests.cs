using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="LibraryDeletionHelper"/>'s removed-files blacklist upsert
/// (docs/superpowers/specs/2026-09-06-scan-missing-file-handling-design.md) - the single choke
/// point every removal path in the app already shares, so these tests cover <c>RemoveIssue</c>/
/// <c>RemoveSeries</c> directly rather than every call site individually.
/// </summary>
public class LibraryDeletionHelperTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public LibraryDeletionHelperTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_deletion_helper_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void RemoveIssue_UpsertsRemovedFilePath()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Kilo Station" };
        var issue = new Issue { Series = series, FilePath = @"C:\comics\kilo-station-012.cbz", FileIsMissing = true };
        context.Series.Add(series);
        context.Issues.Add(issue);
        context.SaveChanges();

        LibraryDeletionHelper.RemoveIssue(context, issue);
        context.SaveChanges();

        var recorded = Assert.Single(context.RemovedFilePaths);
        Assert.Equal(@"C:\comics\kilo-station-012.cbz", recorded.FilePath);
    }

    [Fact]
    public void RemoveIssue_RemovingSamePathTwice_UpdatesTimestamp_NotDuplicateRow()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Kilo Station" };
        context.Series.Add(series);
        var first = new Issue { Series = series, FilePath = @"C:\comics\kilo-station-012.cbz", FileIsMissing = true };
        context.Issues.Add(first);
        context.SaveChanges();
        LibraryDeletionHelper.RemoveIssue(context, first);
        context.SaveChanges();
        var firstTimestamp = Assert.Single(context.RemovedFilePaths).RemovedAtUtc;

        // Same path, a different Issue row (e.g. the file was re-added by a scan and then removed again).
        var second = new Issue { Series = series, FilePath = @"C:\comics\kilo-station-012.cbz", FileIsMissing = true };
        context.Issues.Add(second);
        context.SaveChanges();
        LibraryDeletionHelper.RemoveIssue(context, second);
        context.SaveChanges();

        var recorded = Assert.Single(context.RemovedFilePaths);
        Assert.True(recorded.RemovedAtUtc >= firstTimestamp);
    }

    [Fact]
    public void RemoveIssue_NullFilePath_WritesNothingToBlacklist()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Kilo Station" };
        var issue = new Issue { Series = series, FilePath = null, IsPlaceholder = true };
        context.Series.Add(series);
        context.Issues.Add(issue);
        context.SaveChanges();

        LibraryDeletionHelper.RemoveIssue(context, issue);
        context.SaveChanges();

        Assert.Empty(context.RemovedFilePaths);
    }

    [Fact]
    public void RemoveSeries_UpsertsRemovedFilePath_ForEveryIssue()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Kilo Station" };
        series.Issues.Add(new Issue { Series = series, FilePath = @"C:\comics\kilo-station-001.cbz", FileIsMissing = true });
        series.Issues.Add(new Issue { Series = series, FilePath = @"C:\comics\kilo-station-002.cbz", FileIsMissing = true });
        context.Series.Add(series);
        context.SaveChanges();

        LibraryDeletionHelper.RemoveSeries(context, series);
        context.SaveChanges();

        Assert.Equal(2, context.RemovedFilePaths.Count());
    }
}
