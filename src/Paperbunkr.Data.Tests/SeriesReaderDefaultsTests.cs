using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The new nullable <c>Series.PageFitModeOverride</c>/<c>AutoRotateOverride</c> columns (migration
/// <c>AddSeriesReaderDefaults</c>, docs/superpowers/specs/2026-09-21-comic-reader-flow-and-defaults-design.md §2)
/// and the <c>issue ?? series ?? global</c> chain in <see cref="ReaderDefaultsResolver"/>.
/// </summary>
public class SeriesReaderDefaultsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_series_reader_defaults_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;

    public SeriesReaderDefaultsTests()
    {
        _context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
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
    public void NewSeries_ReadsBackBothColumnsAsNull()
    {
        var series = new Series { Name = "S" };
        _context.Series.Add(series);
        _context.SaveChanges();

        using var reload = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var reloaded = reload.Series.Single(s => s.Id == series.Id);

        Assert.Null(reloaded.PageFitModeOverride);
        Assert.Null(reloaded.AutoRotateOverride);
    }

    [Fact]
    public void SetValues_RoundTripThroughEnumStringConversion()
    {
        var series = new Series { Name = "S", PageFitModeOverride = ImageFitMode.BestFit, AutoRotateOverride = true };
        _context.Series.Add(series);
        _context.SaveChanges();

        using var reload = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var reloaded = reload.Series.Single(s => s.Id == series.Id);

        Assert.Equal(ImageFitMode.BestFit, reloaded.PageFitModeOverride);
        Assert.True(reloaded.AutoRotateOverride);
    }

    [Fact]
    public void Resolver_FallsThrough_IssueThenSeriesThenGlobal()
    {
        var settings = new AppSettings { DefaultPageFitMode = ImageFitMode.FitWidth, DefaultAutoRotate = false };
        var series = new Series { PageFitModeOverride = ImageFitMode.BestFit, AutoRotateOverride = true };
        var bareSeries = new Series();

        Assert.Equal(ImageFitMode.Original, ReaderDefaultsResolver.EffectiveFitMode(new Issue { PageFitModeOverride = ImageFitMode.Original }, series, settings));
        Assert.Equal(ImageFitMode.BestFit, ReaderDefaultsResolver.EffectiveFitMode(new Issue(), series, settings));
        Assert.Equal(ImageFitMode.FitWidth, ReaderDefaultsResolver.EffectiveFitMode(new Issue(), bareSeries, settings));

        Assert.False(ReaderDefaultsResolver.EffectiveAutoRotate(new Issue { AutoRotateOverride = false }, series, settings));
        Assert.True(ReaderDefaultsResolver.EffectiveAutoRotate(new Issue(), series, settings));
        Assert.False(ReaderDefaultsResolver.EffectiveAutoRotate(new Issue(), bareSeries, settings));
    }
}
