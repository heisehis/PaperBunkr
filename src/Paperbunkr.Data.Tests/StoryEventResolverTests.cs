using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>Exercises <see cref="StoryEventResolver.GetOrCreate"/> against a real SQLite database.</summary>
public class StoryEventResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public StoryEventResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_storyeventresolver_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

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

    [Fact]
    public void GetOrCreate_NoExistingRow_CreatesOne()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var storyEvent = StoryEventResolver.GetOrCreate(context, "Civil War");

        Assert.Equal("Civil War", storyEvent.Name);
        Assert.Single(context.StoryEvents);
    }

    [Fact]
    public void GetOrCreate_CaseInsensitiveDifferentCasing_ReturnsExistingRow()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var first = StoryEventResolver.GetOrCreate(context, "Civil War");
        var second = StoryEventResolver.GetOrCreate(context, "civil war");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(context.StoryEvents);
    }

    [Fact]
    public void GetOrCreate_TrimsWhitespace()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var first = StoryEventResolver.GetOrCreate(context, "Civil War");
        var second = StoryEventResolver.GetOrCreate(context, "  Civil War  ");

        Assert.Equal(first.Id, second.Id);
    }

    /// <summary>docs/superpowers/specs/2026-09-17-series-name-matching-and-empty-row-cleanup-design.md - a punctuation-only difference reuses the existing row instead of creating a near-duplicate.</summary>
    [Fact]
    public void GetOrCreate_PunctuationVariant_ReturnsExistingRow()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var first = StoryEventResolver.GetOrCreate(context, "Cataclysm: The Ultimates");
        var second = StoryEventResolver.GetOrCreate(context, "Cataclysm - The Ultimates");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(context.StoryEvents);
    }
}
