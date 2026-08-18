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

    /// <summary>
    /// New nullable columns from migration <c>AddReaderPolishSettings</c> (docs/superpowers/specs/
    /// 2026-08-10-reader-polish-continuous-scroll-chrome-overlays-design.md §9/§11) - same
    /// null-by-default/round-trips-when-set shape as <see cref="PageFitModeOverride"/> above.
    /// </summary>
    [Fact]
    public void Issue_WithNoAdjustmentOverridesSet_ReadsBackAllFourColumnsAsNull()
    {
        var series = new Series { Name = "Test Series" };
        _context.Series.Add(series);
        _context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        _context.Issues.Add(issue);
        _context.SaveChanges();

        using var reload = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var reloaded = reload.Issues.Single(i => i.Id == issue.Id);

        Assert.Null(reloaded.BrightnessOverride);
        Assert.Null(reloaded.ContrastOverride);
        Assert.Null(reloaded.SaturationOverride);
        Assert.Null(reloaded.GammaOverride);
    }

    [Fact]
    public void Issue_WithAdjustmentOverridesSet_RoundTrips()
    {
        var series = new Series { Name = "Test Series" };
        _context.Series.Add(series);
        _context.SaveChanges();

        var issue = new Issue
        {
            SeriesId = series.Id,
            Number = "1",
            BrightnessOverride = 0.1f,
            ContrastOverride = -0.2f,
            SaturationOverride = 0.3f,
            GammaOverride = -0.4f,
        };
        _context.Issues.Add(issue);
        _context.SaveChanges();

        using var reload = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var reloaded = reload.Issues.Single(i => i.Id == issue.Id);

        Assert.Equal(0.1f, reloaded.BrightnessOverride);
        Assert.Equal(-0.2f, reloaded.ContrastOverride);
        Assert.Equal(0.3f, reloaded.SaturationOverride);
        Assert.Equal(-0.4f, reloaded.GammaOverride);
    }

    /// <summary>
    /// New nullable columns from migration <c>AddPageLayoutModeSettings</c> (docs/superpowers/specs/
    /// 2026-08-15-reader-double-page-spread-design.md §2) - same null-by-default/round-trips-when-set
    /// shape as <see cref="PageFitModeOverride"/> above, for both the Series and Issue layers of the
    /// resolution chain.
    /// </summary>
    [Fact]
    public void SeriesAndIssue_WithNoPageLayoutModeSet_ReadBackAsNull()
    {
        var series = new Series { Name = "Test Series" };
        _context.Series.Add(series);
        _context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        _context.Issues.Add(issue);
        _context.SaveChanges();

        using var reload = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var reloadedSeries = reload.Series.Single(s => s.Id == series.Id);
        var reloadedIssue = reload.Issues.Single(i => i.Id == issue.Id);

        Assert.Null(reloadedSeries.PageLayoutMode);
        Assert.Null(reloadedIssue.PageLayoutModeOverride);
    }

    [Fact]
    public void SeriesAndIssue_WithPageLayoutModeSet_RoundTripThroughEnumStringConversion()
    {
        var series = new Series { Name = "Test Series", PageLayoutMode = PageLayoutMode.Double };
        _context.Series.Add(series);
        _context.SaveChanges();

        var issue = new Issue { SeriesId = series.Id, Number = "1", PageLayoutModeOverride = PageLayoutMode.Single };
        _context.Issues.Add(issue);
        _context.SaveChanges();

        using var reload = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var reloadedSeries = reload.Series.Single(s => s.Id == series.Id);
        var reloadedIssue = reload.Issues.Single(i => i.Id == issue.Id);

        Assert.Equal(PageLayoutMode.Double, reloadedSeries.PageLayoutMode);
        Assert.Equal(PageLayoutMode.Single, reloadedIssue.PageLayoutModeOverride);
    }
}
