using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.Data.Tests.Gcd;

/// <summary>A temp library database plus a <see cref="FakeGcdDump"/>, for the GCD matcher/bond tests.</summary>
public abstract class GcdLibraryDb : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_gcd_library_test_{Guid.NewGuid():N}.db");

    protected GcdLibraryDb()
    {
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    protected FakeGcdDump Dump { get; } = new();

    public void Dispose()
    {
        Dump.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    protected PaperbunkrDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    protected Func<PaperbunkrDbContext> Factory => NewContext;

    protected Series AddSeries(string name, string? publisher, params (string Number, int? Year, int? GcdIssueId)[] issues)
    {
        using var context = NewContext();
        var series = new Series { Name = name, Publisher = publisher };
        foreach (var (number, year, gcdIssueId) in issues)
        {
            series.Issues.Add(new Issue { Number = number, Year = year, GcdIssueId = gcdIssueId });
        }

        context.Series.Add(series);
        context.SaveChanges();
        return series;
    }

    protected Series AddMatchedSeries(string name, int gcdSeriesId, GcdMatchSourceKind source = GcdMatchSourceKind.Name)
    {
        using var context = NewContext();
        var series = new Series { Name = name, GcdSeriesId = gcdSeriesId, GcdMatchSource = source };
        context.Series.Add(series);
        context.SaveChanges();
        return series;
    }

    protected void AttachMetronSeriesId(int seriesId, int metronId)
    {
        using var context = NewContext();
        context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
        {
            EntityKind = ComicMetadataEntityKind.Series, EntityId = seriesId, Provider = ComicProvider.Metron,
            ExternalId = metronId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        context.SaveChanges();
    }

    protected Series Reload(int seriesId)
    {
        using var context = NewContext();
        return context.Series.AsNoTracking().Include(s => s.Issues).Single(s => s.Id == seriesId);
    }
}
