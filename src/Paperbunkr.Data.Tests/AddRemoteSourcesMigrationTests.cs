using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// The <c>AddRemoteSources</c> migration (docs/superpowers/specs/2026-09-19-remote-library-sharing-
/// design.md §7.1). Adding foreign keys to <c>Issues</c>/<c>Series</c> makes EF's SQLite provider
/// rebuild both tables, so the important claim is that existing rows survive that rebuild untouched -
/// verified by seeding at the previous schema with raw SQL (never through the current EF model, which
/// already has the new columns) and migrating forward. <c>Down()</c> is a deliberate no-op per the
/// project's orphan-column convention, so nothing here rolls back.
/// </summary>
public class AddRemoteSourcesMigrationTests : IDisposable
{
    private const string PriorMigration = "20260920155556_AddPullListReleaseHidden";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_remotesrc_migration_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    /// <summary>Inserts one row into <paramref name="table"/> at the *current physical schema*, giving every NOT NULL column that has no default a neutral value - so the seed keeps working as unrelated columns are added.</summary>
    private static long InsertRaw(SqliteConnection connection, string table, IDictionary<string, object> overrides)
    {
        var values = new Dictionary<string, object>(overrides);
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                string name = reader.GetString(1);
                string type = reader.GetString(2).ToUpperInvariant();
                bool notNull = reader.GetInt32(3) == 1;
                bool hasDefault = !reader.IsDBNull(4);
                bool isPk = reader.GetInt32(5) > 0;
                if (notNull && !hasDefault && !isPk && !values.ContainsKey(name))
                {
                    values[name] = type.Contains("INT") || type.Contains("REAL") ? 0 : "";
                }
            }
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO \"{table}\" ({string.Join(",", values.Keys.Select(k => $"\"{k}\""))}) " +
                             $"VALUES ({string.Join(",", values.Keys.Select((_, i) => $"$p{i}"))}); SELECT last_insert_rowid();";
        int n = 0;
        foreach (object v in values.Values)
        {
            insert.Parameters.AddWithValue($"$p{n++}", v);
        }

        return (long)insert.ExecuteScalar()!;
    }

    [Fact]
    public void ExistingRows_SurviveTheTableRebuild_WithNullRemoteColumns()
    {
        long seriesId, issueId;
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            connection.Open();
            seriesId = InsertRaw(connection, "Series", new Dictionary<string, object> { ["Name"] = "Legacy Series" });
            issueId = InsertRaw(connection, "Issues", new Dictionary<string, object>
            {
                ["SeriesId"] = seriesId, ["Number"] = "7", ["Title"] = "Legacy Issue", ["FilePath"] = @"C:\comics\legacy.cbz",
            });
        }
        SqliteConnection.ClearAllPools();

        using (var context = CreateContext())
        {
            context.Database.Migrate();

            var series = context.Series.Single(s => s.Id == seriesId);
            var issue = context.Issues.Single(i => i.Id == issueId);

            Assert.Equal("Legacy Series", series.Name);
            Assert.Null(series.RemoteSourceId);
            Assert.Null(series.RemoteSeriesId);
            Assert.Equal("Legacy Issue", issue.Title);
            Assert.Equal(@"C:\comics\legacy.cbz", issue.FilePath);
            Assert.Null(issue.RemoteSourceId);
            Assert.Null(issue.RemoteIssueId);
        }
    }

    [Fact]
    public void RemoteSource_RoundTrips_AndInstanceIdIsUnique()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        context.RemoteSources.Add(new RemoteSource
        {
            InstanceId = "aaaa", DisplayName = "Den PC", Host = "192.168.1.5", Port = 7614,
            CertFingerprint = "ABCD", ProtectedPassword = "opaque", LastCatalogEtag = "\"v1\"",
        });
        context.SaveChanges();

        var back = context.RemoteSources.Single();
        Assert.Equal("Den PC", back.DisplayName);
        Assert.Equal("opaque", back.ProtectedPassword);
        Assert.False(back.IsOffline);
        Assert.False(back.HostChanged);

        context.RemoteSources.Add(new RemoteSource { InstanceId = "aaaa", DisplayName = "dup", Host = "h", CertFingerprint = "x" });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void MirrorKey_IsUniquePerSource_ButManyLocalRowsCoexist()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var source = new RemoteSource { InstanceId = "s1", DisplayName = "A", Host = "h", CertFingerprint = "x" };
        var other = new RemoteSource { InstanceId = "s2", DisplayName = "B", Host = "h", CertFingerprint = "x" };
        context.RemoteSources.AddRange(source, other);
        context.SaveChanges();

        // Many local rows (both remote columns null) never collide with each other.
        var local = new Series { Name = "Local" };
        var a = new Series { Name = "Mirror A", RemoteSourceId = source.Id, RemoteSeriesId = 1 };
        var sameIdOtherSource = new Series { Name = "Mirror B", RemoteSourceId = other.Id, RemoteSeriesId = 1 };
        context.Series.AddRange(local, new Series { Name = "Local 2" }, a, sameIdOtherSource);
        context.SaveChanges();

        context.Series.Add(new Series { Name = "Duplicate", RemoteSourceId = source.Id, RemoteSeriesId = 1 });
        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void DeletingARemoteSource_CascadesToItsMirrorOnly()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        var source = new RemoteSource { InstanceId = "s1", DisplayName = "A", Host = "h", CertFingerprint = "x" };
        context.RemoteSources.Add(source);
        context.SaveChanges();

        var localSeries = new Series { Name = "Local" };
        var mirrorSeries = new Series { Name = "Mirror", RemoteSourceId = source.Id, RemoteSeriesId = 1 };
        context.Series.AddRange(localSeries, mirrorSeries);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = localSeries.Id, Number = "1", FilePath = @"C:\a.cbz" });
        context.Issues.Add(new Issue { SeriesId = mirrorSeries.Id, Number = "1", RemoteSourceId = source.Id, RemoteIssueId = 10 });
        context.SaveChanges();

        context.RemoteSources.Remove(source);
        context.SaveChanges();

        Assert.Equal(new[] { "Local" }, context.Series.Select(s => s.Name).ToList());
        Assert.Equal(1, context.Issues.Count());
        Assert.Equal(@"C:\a.cbz", context.Issues.Single().FilePath);
    }
}
