using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// <see cref="EntityIndexSync"/> (docs/superpowers/specs/2026-11-10-series-structure-and-discovery-plan.md D1.1): one call keeps all four derived
/// indexes in step with an issue's text fields, the rebuild runs once per version, and an entity with a custom image survives pruning.
/// </summary>
public class EntityIndexSyncTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_entity_index_test_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public EntityIndexSyncTests()
    {
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

    private static int SeedIssue(PaperbunkrDbContext context, Action<Issue> configure)
    {
        var series = new Series { Name = "Uncanny X-Men" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "94" };
        configure(issue);
        context.Issues.Add(issue);
        context.SaveChanges();
        return issue.Id;
    }

    [Fact]
    public void SyncIssue_FillsAllFourIndexes()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, i =>
        {
            i.Characters = "Storm, Wolverine";
            i.Teams = "X-Men";
            i.Locations = "Westchester";
            i.Writer = "Chris Claremont";
        });

        EntityIndexSync.SyncIssue(context, issueId);

        Assert.Equal(2, context.CharacterAppearances.Count(a => a.IssueId == issueId));
        Assert.Equal(1, context.TeamAppearances.Count(a => a.IssueId == issueId));
        Assert.Equal(1, context.LocationAppearances.Count(a => a.IssueId == issueId));
        Assert.Equal(1, context.CreatorCredits.Count(c => c.IssueId == issueId));
    }

    [Fact]
    public void SyncIssue_AfterAnEdit_RemovesWhatTheTextNoLongerSays()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, i => { i.Characters = "Storm, Wolverine"; i.Writer = "Chris Claremont"; });
        EntityIndexSync.SyncIssue(context, issueId);

        var issue = context.Issues.Single(i => i.Id == issueId);
        issue.Characters = "Storm";
        issue.Writer = null;
        context.SaveChanges();
        EntityIndexSync.SyncIssue(context, issueId);

        Assert.Equal(new[] { "Storm" }, context.Characters.Select(c => c.Name).ToArray());
        Assert.Empty(context.CreatorCredits);
        Assert.Empty(context.Creators);
    }

    [Fact]
    public void Prune_KeepsAnEntityWithACustomImage()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int issueId = SeedIssue(context, i => i.Characters = "Storm");
        EntityIndexSync.SyncIssue(context, issueId);
        context.Characters.Single().CustomImagePath = "entity-images/character-1.jpg";
        context.SaveChanges();

        context.Issues.Single(i => i.Id == issueId).Characters = null;
        context.SaveChanges();
        EntityIndexSync.SyncIssue(context, issueId);

        var storm = Assert.Single(context.Characters);
        Assert.Equal("Storm", storm.Name);
    }

    [Fact]
    public void RebuildAll_RemovesStaleIndexesAfterFieldsWereClearedWithoutSync()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int id = SeedIssue(context, i => { i.Characters = "Storm"; i.Teams = "X-Men"; i.Locations = "Westchester"; i.Writer = "Claremont"; });
        EntityIndexSync.SyncIssue(context, id);
        var issue = context.Issues.Single();
        issue.Characters = issue.Teams = issue.Locations = issue.Writer = null;
        context.SaveChanges();

        EntityIndexSync.RebuildAll(context);

        Assert.Empty(context.CharacterAppearances);
        Assert.Empty(context.TeamAppearances);
        Assert.Empty(context.LocationAppearances);
        Assert.Empty(context.CreatorCredits);
    }

    [Fact]
    public void CancelledRebuild_DoesNotRecordTheVersion()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => EntityIndexSync.RebuildAll(context, cancellationToken: cts.Token));
        Assert.True(EntityIndexSync.NeedsRebuild(context));
    }

    [Fact]
    public void RebuildAll_RunsOnceThenReportsCurrent()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedIssue(context, i => { i.Characters = "Storm"; i.Teams = "X-Men"; i.Writer = "Chris Claremont"; });
        Assert.True(EntityIndexSync.NeedsRebuild(context));

        EntityIndexSync.RebuildAll(context);

        Assert.False(EntityIndexSync.NeedsRebuild(context));
        Assert.Single(context.Characters);
        Assert.Single(context.Teams);
        Assert.Single(context.Creators);
    }

    // ---- set-based engine (EntityIndexBulk) ----

    [Fact]
    public void Bulk_SecondRunChangesNothing()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        for (int n = 0; n < 30; n++)
        {
            SeedIssue(context, i => { i.Characters = $"Storm, Hero {n % 7}"; i.Writer = "Chris Claremont"; i.Penciller = $"Artist {n % 5}"; });
        }

        int first = EntityIndexBulk.Sync(context, issueIds: null);
        int second = EntityIndexBulk.Sync(context, issueIds: null);

        Assert.True(first > 0);
        Assert.Equal(0, second);
        Assert.Equal(8, context.Characters.Count()); // Storm + Hero 0..6
        Assert.Equal(30, context.CharacterAppearances.Count(a => a.Character!.Name == "Storm"));
        Assert.Equal(6, context.Creators.Count()); // Claremont + Artist 0..4
    }

    [Fact]
    public void Bulk_ReusesAnExistingEntityCaseInsensitively_AndIgnoresRepeatsInOneIssue()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        SeedIssue(context, i => i.Characters = "Storm");
        SeedIssue(context, i => i.Characters = "STORM; storm, Storm");
        EntityIndexBulk.Sync(context, issueIds: null);

        var storm = Assert.Single(context.Characters);
        Assert.Equal(2, context.CharacterAppearances.Count(a => a.CharacterId == storm.Id));
    }

    [Fact]
    public void Bulk_OneCreatorInTwoRoles_GetsACreditPerRole()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int id = SeedIssue(context, i => { i.Writer = "Todd McFarlane"; i.Penciller = "Todd McFarlane"; i.Inker = "Todd McFarlane"; });

        EntityIndexSync.SyncIssue(context, id);

        Assert.Single(context.Creators);
        Assert.Equal(new[] { "Inker", "Penciller", "Writer" }, context.CreatorCredits.Select(c => c.Role).OrderBy(r => r).ToArray());
    }

    [Fact]
    public void Bulk_PrunedCreator_LosesItsProviderIds()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int id = SeedIssue(context, i => i.Writer = "Chris Claremont");
        EntityIndexSync.SyncIssue(context, id);
        int creatorId = context.Creators.Single().Id;
        context.ComicMetadataExternalIds.Add(new ComicMetadataExternalId
        {
            EntityKind = ComicMetadataEntityKind.Creator, EntityId = creatorId, Provider = ComicProvider.ComicVine, ExternalId = "40439",
        });
        context.SaveChanges();

        context.Issues.Single(i => i.Id == id).Writer = null;
        context.SaveChanges();
        EntityIndexSync.SyncIssue(context, id);

        Assert.Empty(context.Creators);
        Assert.Empty(context.ComicMetadataExternalIds);
    }

    [Fact]
    public void SyncIssues_OnlyTouchesTheGivenIssues()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int a = SeedIssue(context, i => i.Characters = "Storm");
        int b = SeedIssue(context, i => i.Characters = "Wolverine");

        EntityIndexSync.SyncIssues(context, new[] { a });

        Assert.Equal(new[] { "Storm" }, context.Characters.Select(c => c.Name).ToArray());
        Assert.DoesNotContain(context.CharacterAppearances, x => x.IssueId == b);
    }

    [Fact]
    public void FindOrIndex_IndexesAnUnindexedName_ThenFindsIt()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        int a = SeedIssue(context, i => i.Penciller = "Brent Bristol, Ryan Ottley");
        SeedIssue(context, i => i.Writer = "Robert Kirkman");
        Assert.Null(EntityNameLookup.Find(context, ComicMetadataEntityKind.Creator, "brent bristol"));

        int? id = EntityNameLookup.FindOrIndex(context, ComicMetadataEntityKind.Creator, "brent bristol");

        Assert.NotNull(id);
        Assert.Equal("Brent Bristol", context.Creators.Single(c => c.Id == id).Name);
        Assert.Contains(context.CreatorCredits, c => c.IssueId == a && c.CreatorId == id && c.Role == "Penciller");
        Assert.Null(context.Creators.FirstOrDefault(c => c.Name == "Robert Kirkman")); // only the issues that mention the name were indexed
        Assert.Null(EntityNameLookup.FindOrIndex(context, ComicMetadataEntityKind.Creator, "Nobody At All"));
    }
}
