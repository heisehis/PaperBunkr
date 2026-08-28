using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="CharacterResolver"/> (docs/superpowers/specs/2026-08-27-metadata-model-
/// phase4g-age-progression-design.md) against a real SQLite database.
/// </summary>
public class CharacterResolverTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public CharacterResolverTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_character_test_{Guid.NewGuid():N}.db");
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

    private static int SeedIssue(PaperbunkrDbContext context, string seriesName, string? characters)
    {
        var series = new Series { Name = seriesName };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", Characters = characters };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Theory]
    [InlineData("Batman, Robin; Nightwing", 3)]
    [InlineData("Batman,Batman , batman", 1)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseNames_SplitsAndDedupesCaseInsensitively(string? text, int expected)
    {
        Assert.Equal(expected, CharacterResolver.ParseNames(text).Count);
    }

    [Fact]
    public void SyncFromIssue_MaterializesAppearances()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Batman", "Batman, Robin");

        CharacterResolver.SyncFromIssue(context, issueId);

        Assert.Equal(2, context.CharacterAppearances.Count(a => a.IssueId == issueId));
        Assert.Equal(2, context.Characters.Count());
    }

    [Fact]
    public void SyncFromIssue_RemovesStale_AndPrunesOrphanCharacters()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Batman", "Batman, Robin");
        CharacterResolver.SyncFromIssue(context, issueId);

        context.Issues.Find(issueId)!.Characters = "Batman";
        context.SaveChanges();
        CharacterResolver.SyncFromIssue(context, issueId);

        Assert.Single(context.CharacterAppearances.Where(a => a.IssueId == issueId));
        Assert.Single(context.Characters); // "Robin" pruned
        Assert.Equal("Batman", context.Characters.Single().Name);
    }

    [Fact]
    public void RebuildAll_BackfillsWholeLibrary()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedIssue(context, "Batman", "Batman, Robin");
        SeedIssue(context, "Superman", "Superman, Batman");

        CharacterResolver.RebuildAll(context);

        Assert.Equal(3, context.Characters.Count()); // Batman, Robin, Superman
        Assert.Equal(4, context.CharacterAppearances.Count());
    }

    [Fact]
    public void GetSeriesIdsSharingCharacterWith_FindsCrossSeriesOverlap()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int batmanIssue = SeedIssue(context, "Batman", "Batman, Alfred");
        int supermanIssue = SeedIssue(context, "Superman", "Superman, Batman");
        SeedIssue(context, "Spawn", "Spawn"); // no overlap
        CharacterResolver.RebuildAll(context);

        int batmanSeriesId = context.Issues.Find(batmanIssue)!.SeriesId;
        int supermanSeriesId = context.Issues.Find(supermanIssue)!.SeriesId;

        var shared = CharacterResolver.GetSeriesIdsSharingCharacterWith(context, new[] { batmanSeriesId });

        Assert.Equal(new[] { supermanSeriesId }, shared);
    }

    [Fact]
    public void DeletingIssue_CascadesAppearances()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, "Batman", "Batman");
        CharacterResolver.SyncFromIssue(context, issueId);

        context.Issues.Remove(context.Issues.Find(issueId)!);
        context.SaveChanges();

        Assert.Empty(context.CharacterAppearances);
    }
}
