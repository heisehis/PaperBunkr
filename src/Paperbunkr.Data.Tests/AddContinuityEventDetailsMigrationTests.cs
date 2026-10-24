using Microsoft.EntityFrameworkCore;

namespace Paperbunkr.Data.Tests;

public sealed class AddContinuityEventDetailsMigrationTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"paperbunkr_event_details_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    [Fact]
    public void MigrationAddsEventCoverAndTagStorageWithLegacySafeDefaults()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_path}").Options;
        using (var db = new PaperbunkrDbContext(options))
        {
            db.Database.Migrate();
            var e = new Paperbunkr.Data.Entities.StoryEvent { Name = "Legacy event", Description = "Keep this" };
            db.StoryEvents.Add(e);
            db.SaveChanges();
            db.ChangeTracker.Clear();
            var loaded = db.StoryEvents.Include(x => x.Tags).Single(x => x.Id == e.Id);
            Assert.Equal("Keep this", loaded.Description);
            Assert.Equal(Paperbunkr.Data.Entities.StoryEventCoverSource.None, loaded.CoverSource);
            Assert.Null(loaded.StartDate);
            Assert.Null(loaded.EndDate);
            Assert.Empty(loaded.Tags);
            loaded.Tags.Add(new Paperbunkr.Data.Entities.StoryEventTag { Value = "Cosmic", Category = "Theme", Weight = Paperbunkr.Data.Entities.IssueTagWeight.Unset });
            db.SaveChanges();
        }
        using var reopened = new PaperbunkrDbContext(options);
        Assert.Equal("Cosmic", Assert.Single(reopened.StoryEventTags).Value);
    }
}
