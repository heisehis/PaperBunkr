using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Paperbunkr.Data.Tests;

public sealed class AddEventMapViewSettingsMigrationTests
{
    [Fact]
    public void ExistingSettingsReceiveFalseForBothNewColumns()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_map_settings_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options;
            using var context = new PaperbunkrDbContext(options);
            context.GetService<IMigrator>().Migrate("20261111115950_AddLocationProfiles");
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
            var settings = context.AppSettings.AsNoTracking()
                .Select(s => new { s.EventMapLaneRailWide, s.EventMapInspectorDocked }).Single();
            Assert.False(settings.EventMapLaneRailWide);
            Assert.False(settings.EventMapInspectorDocked);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
