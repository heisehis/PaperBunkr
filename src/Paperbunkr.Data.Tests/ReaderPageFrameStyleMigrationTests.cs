using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

public sealed class ReaderPageFrameStyleMigrationTests
{
    [Fact]
    public void LatestMigration_UsesNoFrameAsTheGlobalDefault()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_reader_frame_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
            using var context = new PaperbunkrDbContext(options);
            context.Database.Migrate();

            Assert.Equal(ReaderPageFrameStyle.None, context.GetOrCreateAppSettings().ReaderPageFrameStyle);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
