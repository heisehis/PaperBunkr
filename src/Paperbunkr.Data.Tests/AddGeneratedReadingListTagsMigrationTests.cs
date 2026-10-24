using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

public sealed class AddGeneratedReadingListTagsMigrationTests : IDisposable
{
    private const string PriorMigration = "20261014100022_AddMangaSeriesMetadata";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_tag_origin_migration_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    private PaperbunkrDbContext CreateContext() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>()
        .UseSqlite($"Data Source={_path}").Options);

    [Fact]
    public void Migration_MarksLegacyTagsAsUserOwned_AndPreservesTheirMetadata()
    {
        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw("INSERT INTO ReadingLists (Name, SortOrder, FollowArc) VALUES ('Legacy', 0, 0);");
            context.Database.ExecuteSqlRaw("INSERT INTO ReadingListTags (ReadingListId, Value, Category, Weight) VALUES (1, 'Space Opera', 'Genre', 'Core');");
        }

        using var upgraded = CreateContext();
        upgraded.Database.Migrate();
        var tag = Assert.Single(upgraded.ReadingListTags);
        Assert.Equal("Space Opera", tag.Value);
        Assert.Equal("Genre", tag.Category);
        Assert.Equal(IssueTagWeight.Core, tag.Weight);
        Assert.Equal(ReadingListTagOrigin.User, tag.Origin);
        Assert.Null(tag.GeneratedKey);
    }
}
