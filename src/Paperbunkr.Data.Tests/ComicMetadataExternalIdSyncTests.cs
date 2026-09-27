using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="ComicMetadataExternalIdSync"/> (docs/superpowers/specs/2026-09-23-metron-api-
/// utilization-design.md) against a real SQLite database.
/// </summary>
public class ComicMetadataExternalIdSyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public ComicMetadataExternalIdSyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_externalidsync_test_{Guid.NewGuid():N}.db");
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

    private static ComicVineIssueDetails Details() => new(
        900, 10, "Captain America", "1", "Winter Soldier", null, new ComicVineDatePart(2018, 7, 1), new ComicVineDatePart(2018, 5, 9), null,
        Array.Empty<ComicVineIdName>(),
        new[] { new ComicVineIdName(7, "Captain America") },
        new[] { new ComicVineIdName(8, "Avengers") },
        Array.Empty<ComicVineIdName>(),
        new[] { new ComicVineCredit("Ta-Nehisi Coates", "Writer", RoleExternalId: 1) });

    /// <summary>Seeds an issue whose flat text already reflects <see cref="Details"/> - matching what a real scrape would have written via <c>IssueDetailsApplier</c> before this sync ever runs.</summary>
    private static int SeedAppliedIssue(PaperbunkrDbContext context)
    {
        var series = new Series { Name = "Captain America" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", Characters = "Captain America", Teams = "Avengers", Writer = "Ta-Nehisi Coates" };
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void SyncFromIssueDetails_AttachesCharacterAndTeamExternalIds()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedAppliedIssue(context);

        ComicMetadataExternalIdSync.SyncFromIssueDetails(context, issueId, ComicProvider.Metron, Details());

        var character = context.Characters.Single(c => c.Name == "Captain America");
        var team = context.Teams.Single(t => t.Name == "Avengers");

        var characterExternalId = context.ComicMetadataExternalIds.Single(e => e.EntityKind == ComicMetadataEntityKind.Character);
        Assert.Equal(character.Id, characterExternalId.EntityId);
        Assert.Equal(ComicProvider.Metron, characterExternalId.Provider);
        Assert.Equal("7", characterExternalId.ExternalId);

        var teamExternalId = context.ComicMetadataExternalIds.Single(e => e.EntityKind == ComicMetadataEntityKind.Team);
        Assert.Equal(team.Id, teamExternalId.EntityId);
        Assert.Equal("8", teamExternalId.ExternalId);
    }

    [Fact]
    public void AttachEntityId_ProviderIdAlreadyOwnedByAnotherIssue_IsSkippedNotThrown()
    {
        // Real crash (2026-09-25): once per-issue details started applying for real, two local issues
        // resolving to one ComicVine issue id (a duplicate copy, or "1" vs "01") hit the unique
        // (EntityKind, Provider, ExternalId) index and threw out of the whole whole-series scrape.
        using var context = new PaperbunkrDbContext(_dbOptions);
        int firstId = SeedAppliedIssue(context);
        var secondIssue = new Issue { SeriesId = context.Issues.Single(i => i.Id == firstId).SeriesId, Number = "01" };
        context.Issues.Add(secondIssue);
        context.SaveChanges();

        ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Issue, ComicProvider.ComicVine, firstId, 4321);
        var exception = Record.Exception(() =>
            ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Issue, ComicProvider.ComicVine, secondIssue.Id, 4321));

        Assert.Null(exception);
        var row = context.ComicMetadataExternalIds.Single(e => e.EntityKind == ComicMetadataEntityKind.Issue);
        Assert.Equal(firstId, row.EntityId);   // the first issue keeps the link
    }

    [Fact]
    public void SyncFromIssueDetails_NameNotInTheIssuesOwnText_IsNeverAttached()
    {
        // A restrictive ScrapeFieldPolicy (or ComicProviderMerge's gap-fill policy) can leave
        // `details` carrying a name that was never actually written to the issue - this must not
        // materialize an entity/external-id for it regardless.
        using var context = new PaperbunkrDbContext(_dbOptions);
        var series = new Series { Name = "Captain America" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1" };   // Characters/Teams left blank
        context.Issues.Add(issue);
        context.SaveChanges();

        ComicMetadataExternalIdSync.SyncFromIssueDetails(context, issue.Id, ComicProvider.Metron, Details());

        Assert.Empty(context.Characters);
        Assert.Empty(context.Teams);
        Assert.Empty(context.ComicMetadataExternalIds);
    }

    [Fact]
    public void SyncFromIssueDetails_CreatorExternalId_OnlyAttachedWhenCreditCarriesOne()
    {
        // Metron credits never carry a creator external id (confirmed during design) - RoleExternalId
        // being set doesn't mean CreatorExternalId is, and this must not attach a Creator external id
        // from Metron data.
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedAppliedIssue(context);

        ComicMetadataExternalIdSync.SyncFromIssueDetails(context, issueId, ComicProvider.Metron, Details());

        Assert.Empty(context.ComicMetadataExternalIds.Where(e => e.EntityKind == ComicMetadataEntityKind.Creator));
    }

    [Fact]
    public void SyncFromIssueDetails_RunTwice_IsIdempotent()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedAppliedIssue(context);

        ComicMetadataExternalIdSync.SyncFromIssueDetails(context, issueId, ComicProvider.Metron, Details());
        ComicMetadataExternalIdSync.SyncFromIssueDetails(context, issueId, ComicProvider.Metron, Details());

        Assert.Equal(2, context.ComicMetadataExternalIds.Count());   // one Character row, one Team row - not doubled
    }
}
