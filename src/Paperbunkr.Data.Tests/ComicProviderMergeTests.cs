using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.ComicVine.Scraping;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="ComicProviderMerge"/> (docs/superpowers/specs/2026-09-23-metron-api-
/// utilization-design.md) against a real SQLite database.
/// </summary>
public class ComicProviderMergeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ComicProviderMergeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_providermerge_test_{Guid.NewGuid():N}.db");
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

    private static int SeedIssue(PaperbunkrDbContext context, Action<Issue>? configure = null)
    {
        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };
        configure?.Invoke(issue);
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    private static ComicVineIssueDetails MetronDetails() => new(
        900, 10, "Test Series", "1", "Endgame", null, new ComicVineDatePart(2018, 7, 1), new ComicVineDatePart(2018, 5, 9), "Metron summary",
        Array.Empty<ComicVineIdName>(),
        new[] { new ComicVineIdName(7, "Captain America") },
        new[] { new ComicVineIdName(8, "Avengers") },
        Array.Empty<ComicVineIdName>(),
        new[] { new ComicVineCredit("Ta-Nehisi Coates", "Writer") },
        AgeRating: "Teen", Genres: new[] { "Superhero" });

    private static ComicVineIssueDetails ComicVineDetails() => new(
        4321, 10, "Test Series", "1", "Endgame", null, new ComicVineDatePart(2018, 7, 1), new ComicVineDatePart(2018, 5, 9), "CV summary",
        Array.Empty<ComicVineIdName>(),
        new[] { new ComicVineIdName(601, "Someone Else") },     // should NOT win over Metron's characters
        Array.Empty<ComicVineIdName>(),
        new[] { new ComicVineIdName(701, "Rat City") },          // Metron has none - ComicVine fills this gap
        Array.Empty<ComicVineCredit>(),
        AgeRating: "Mature");                                     // should NOT overwrite Metron's "Teen"

    [Fact]
    public void MergeIssue_MetronWinsForCharacters_ComicVineFillsLocationsGap()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context);

        ComicProviderMerge.MergeIssue(context, issueId, MetronDetails(), ComicVineDetails());

        var issue = context.Issues.Single(i => i.Id == issueId);
        Assert.Equal("Captain America", issue.Characters);   // Metron's, not ComicVine's "Someone Else"
        Assert.Equal("Rat City", issue.Locations);            // ComicVine fills the gap Metron can't
        Assert.Equal("Teen", issue.AgeRating);                // Metron's rating kept, ComicVine's "Mature" didn't overwrite it
        Assert.Equal(ComicProvider.Metron, issue.MetadataSource);
        Assert.Contains(context.Teams, t => t.Name == "Avengers");
        Assert.Contains(context.Locations, l => l.Name == "Rat City");
        Assert.Contains(issue.Tags, t => t.Field == IssueTagField.Genre && t.Value == "Superhero");
    }

    [Fact]
    public void MergeIssue_AttachesExternalIdsForBothProviders()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context);

        ComicProviderMerge.MergeIssue(context, issueId, MetronDetails(), ComicVineDetails());

        var team = context.Teams.Single(t => t.Name == "Avengers");
        var teamExternalId = context.ComicMetadataExternalIds.Single(e => e.EntityKind == ComicMetadataEntityKind.Team);
        Assert.Equal(team.Id, teamExternalId.EntityId);
        Assert.Equal(ComicProvider.Metron, teamExternalId.Provider);

        var location = context.Locations.Single(l => l.Name == "Rat City");
        var locationExternalId = context.ComicMetadataExternalIds.Single(e => e.EntityKind == ComicMetadataEntityKind.Location);
        Assert.Equal(location.Id, locationExternalId.EntityId);
        Assert.Equal(ComicProvider.ComicVine, locationExternalId.Provider);
    }

    [Fact]
    public void MergeIssue_RunTwice_IsIdempotent()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context);

        ComicProviderMerge.MergeIssue(context, issueId, MetronDetails(), ComicVineDetails());
        var secondRunChanged = ComicProviderMerge.MergeIssue(context, issueId, MetronDetails(), ComicVineDetails());

        Assert.Empty(secondRunChanged);
        Assert.Single(context.Teams);
        Assert.Single(context.Locations);
        Assert.Equal(3, context.ComicMetadataExternalIds.Count());   // one Character, one Team (both Metron), one Location (ComicVine) - not doubled
        Assert.Single(context.Issues.Single(i => i.Id == issueId).Tags.Where(t => t.Value == "Superhero"));
    }

    [Fact]
    public void MergeIssue_NoComicVineSnapshot_StillAppliesMetronPass()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context);

        ComicProviderMerge.MergeIssue(context, issueId, MetronDetails(), fromComicVine: null);

        var issue = context.Issues.Single(i => i.Id == issueId);
        Assert.Equal("Captain America", issue.Characters);
        Assert.Null(issue.Locations);   // no ComicVine snapshot supplied - nothing to fill the gap
    }
}
