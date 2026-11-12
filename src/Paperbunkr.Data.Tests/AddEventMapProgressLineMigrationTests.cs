using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <c>AddEventMapProgressLine</c> (docs/superpowers/specs/2026-11-11-continuity-map-polish-design.md §1): forward-only on a synthetic
/// database (no up-down-up, see the CharacterCount antipattern). An existing settings row reads the progress line as on.
/// </summary>
public sealed class AddEventMapProgressLineMigrationTests
{
    [Fact]
    public void ExistingSettingsShowTheProgressLine()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_map_progress_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options;
            using var context = new PaperbunkrDbContext(options);
            context.GetService<IMigrator>().Migrate("20261111203943_AddEventMapRelayPacking");
            var connection = context.Database.GetDbConnection();
            connection.Open();
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA table_info('AppSettings')";
                using var rows = pragma.ExecuteReader();
                var names = new List<string>();
                var values = new List<string>();
                while (rows.Read())
                {
                    string name = rows.GetString(1);
                    if (name != "Id" && rows.GetInt32(3) == 0) continue;
                    names.Add("\"" + name.Replace("\"", "\"\"") + "\"");
                    values.Add(name == "Id" ? "1" : rows.GetString(2).Contains("TEXT", StringComparison.OrdinalIgnoreCase) ? "''" : "0");
                }

                rows.Close();
                using var insert = connection.CreateCommand();
                insert.CommandText = $"INSERT INTO AppSettings ({string.Join(",", names)}) VALUES ({string.Join(",", values)})";
                insert.ExecuteNonQuery();
            }

            context.GetService<IMigrator>().Migrate();
            Assert.True(context.AppSettings.AsNoTracking().Select(s => s.EventMapProgressLine).Single());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DownDropsOnlyItsOwnColumn()
    {
        var migration = new Migrations.AddEventMapProgressLine();
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");
        typeof(Migration).GetMethod("Down", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(migration, new object[] { builder });
        var drop = Assert.Single(builder.Operations);
        var column = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropColumnOperation>(drop);
        Assert.Equal("EventMapProgressLine", column.Name);
        Assert.Equal("AppSettings", column.Table);
    }

    [Fact]
    public void NewSettingsDefaultToTheProgressLineOn() => Assert.True(new Entities.AppSettings().EventMapProgressLine);
}
