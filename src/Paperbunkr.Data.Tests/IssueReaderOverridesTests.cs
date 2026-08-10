using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises the new nullable <see cref="Issue.PageFitModeOverride"/>/<see cref="Issue.AutoRotateOverride"/>
/// columns (docs/superpowers/specs/2026-08-10-reader-polish-core-viewing-controls-design.md §3,
/// migration <c>AddReaderFitModeAndAutoRotate</c>) round-trip correctly through EF - in particular
/// that <see cref="ImageFitMode"/>'s <c>HasConversion&lt;string&gt;()</c> mapping (same pattern as
/// <see cref="Issue.ReadingModeOverride"/>) handles a null value correctly, not just a set one.
/// </summary>
public class IssueReaderOverridesTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public IssueReaderOverridesTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_overrides_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();
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

    [Fact]
    public void Issue_WithNoOverridesSet_ReadsBackBothColumnsAsNull()
    {
        var series = new Series { Name = "Test Series" };
        _context.Series.Add(series);
        _context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        _context.Issues.Add(issue);
        _context.SaveChanges();

        using var reload = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var reloaded = reload.Issues.Single(i => i.Id == issue.Id);

        Assert.Null(reloaded.PageFitModeOverride);
        Assert.Null(reloaded.AutoRotateOverride);
    }

    [Fact]
    public void Issue_WithOverridesSet_RoundTripsThroughEnumStringConversion()
    {
        var series = new Series { Name = "Test Series" };
        _context.Series.Add(series);
        _context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = "1", PageFitModeOverride = ImageFitMode.BestFit, AutoRotateOverride = true };
        _context.Issues.Add(issue);
        _context.SaveChanges();

        using var reload = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var reloaded = reload.Issues.Single(i => i.Id == issue.Id);

        Assert.Equal(ImageFitMode.BestFit, reloaded.PageFitModeOverride);
        Assert.True(reloaded.AutoRotateOverride);
    }
}
