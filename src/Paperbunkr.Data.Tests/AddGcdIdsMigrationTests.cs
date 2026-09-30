using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Gcd;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddGcdIds</c> migration (docs/superpowers/specs/2026-09-27-gcd-data-design.md §3): the Grand Comics Database
/// ids on <c>Series</c>/<c>Issue</c> start unset, round-trip, and the match source is stored by name. Forward-only.
/// </summary>
public class AddGcdIdsMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_gcd_ids_migration_test_{Guid.NewGuid():N}.db");

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

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    [Fact]
    public void GcdIds_StartUnset_AndRoundTrip()
    {
        int plainId, matchedId;
        using (var context = CreateContext())
        {
            context.Database.Migrate();
            var plain = new Series { Name = "Planet Hulk" };
            plain.Issues.Add(new Issue { Number = "1" });
            var matched = new Series { Name = "World War Hulk", GcdSeriesId = 30123, GcdMatchSource = GcdMatchSourceKind.Metron };
            matched.Issues.Add(new Issue { Number = "1", GcdIssueId = 900001 });
            context.Series.AddRange(plain, matched);
            context.SaveChanges();
            plainId = plain.Id;
            matchedId = matched.Id;
        }

        using var check = CreateContext();
        var reloadedPlain = check.Series.Include(s => s.Issues).Single(s => s.Id == plainId);
        Assert.Null(reloadedPlain.GcdSeriesId);
        Assert.Null(reloadedPlain.GcdMatchSource);
        Assert.Null(reloadedPlain.Issues.Single().GcdIssueId);

        var reloadedMatched = check.Series.Include(s => s.Issues).Single(s => s.Id == matchedId);
        Assert.Equal(30123, reloadedMatched.GcdSeriesId);
        Assert.Equal(GcdMatchSourceKind.Metron, reloadedMatched.GcdMatchSource);
        Assert.Equal(900001, reloadedMatched.Issues.Single().GcdIssueId);

        var stored = check.Database.SqlQueryRaw<string>($"SELECT GcdMatchSource AS Value FROM Series WHERE Id = {matchedId}").Single();
        Assert.Equal("Metron", stored);
    }

    /// <summary>The <c>AddSeriesRelationDismissals</c> migration that follows it: pairs are unique and go with their series.</summary>
    [Fact]
    public void SeriesRelationDismissals_AreUnique_AndCascade()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        var a = new Series { Name = "Hulk (1968)" };
        var b = new Series { Name = "Hulk (2008)" };
        context.Series.AddRange(a, b);
        context.SaveChanges();
        context.SeriesRelationDismissals.Add(new SeriesRelationDismissal { LowerSeriesId = a.Id, HigherSeriesId = b.Id });
        context.SaveChanges();

        using (var duplicate = CreateContext())
        {
            duplicate.SeriesRelationDismissals.Add(new SeriesRelationDismissal { LowerSeriesId = a.Id, HigherSeriesId = b.Id });
            Assert.Throws<DbUpdateException>(() => duplicate.SaveChanges());
        }

        context.Series.Remove(a);
        context.SaveChanges();
        Assert.Empty(context.SeriesRelationDismissals);
    }
}
