using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.SmartLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the Needs Review "Missing Files" query shape (docs/superpowers/specs/2026-08-06-
/// migration-ux-polish-design.md §2): the same "Missing Files" system smart list query
/// (<see cref="SmartListField.IsMissing"/>) plus an extra <c>MissingAcknowledged</c> filter,
/// mirroring <c>NeedsReviewViewModel.RefreshMissingFileItems</c> without needing the App-layer
/// ViewModel.
/// </summary>
public class MissingAcknowledgedTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public MissingAcknowledgedTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_missingack_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();

        var series = new Series { Name = "Alpha" };
        _context.Series.Add(series);
        _context.SaveChanges();

        _context.Issues.AddRange(
            new Issue { SeriesId = series.Id, Number = "1", FileIsMissing = true, MissingAcknowledged = false },
            new Issue { SeriesId = series.Id, Number = "2", FileIsMissing = true, MissingAcknowledged = true },
            new Issue { SeriesId = series.Id, Number = "3", FileIsMissing = false, MissingAcknowledged = false });
        _context.SaveChanges();
    }

    private static SmartList MissingFilesList() => new()
    {
        RootGroup = new SmartListConditionGroup
        {
            Conditions =
            {
                new() { Field = SmartListField.IsMissing, Operator = SmartListOperator.Is, Value = "true" },
            },
        },
    };

    [Fact]
    public void MissingFilesQuery_WithoutAcknowledgedFilter_ReturnsEveryMissingIssue()
    {
        var results = SmartListQueryBuilder.Build(_context, MissingFilesList()).ToList();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, i => i.Number == "1");
        Assert.Contains(results, i => i.Number == "2");
    }

    [Fact]
    public void MissingFilesQuery_WithAcknowledgedFilter_ExcludesAcknowledgedIssues()
    {
        var results = SmartListQueryBuilder.Build(_context, MissingFilesList())
            .Where(i => !i.MissingAcknowledged)
            .ToList();

        var issue = Assert.Single(results);
        Assert.Equal("1", issue.Number);
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }
}
