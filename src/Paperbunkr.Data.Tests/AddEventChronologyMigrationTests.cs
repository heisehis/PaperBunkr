using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddEventChronology</c> migration (docs/superpowers/specs/2026-09-27-continuity-map-design.md §1): the new
/// <c>StoryEvent</c> columns are nullable and unset, and the relation-dismissal table accepts rows and cascades with its events.
/// Forward-only.
/// </summary>
public class AddEventChronologyMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_event_chronology_migration_test_{Guid.NewGuid():N}.db");

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

    private PaperbunkrDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    [Fact]
    public void NewColumns_AreUnset_AndDismissalsCascade()
    {
        using var context = CreateContext();
        context.Database.Migrate();
        var a = new StoryEvent { Name = "Planet Hulk", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var b = new StoryEvent { Name = "World War Hulk", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, WikidataQid = "Q1048144" };
        context.StoryEvents.AddRange(a, b);
        context.SaveChanges();
        context.EventRelationDismissals.Add(new EventRelationDismissal { LowerEventId = a.Id, HigherEventId = b.Id });
        context.EventRelations.Add(new EventRelation
        {
            SourceEventId = a.Id, TargetEventId = b.Id, RelationType = RelationType.Prequel,
            Evidence = { new EventRelationEvidence { Provider = RelationEvidenceProvider.Wikidata, ProviderSourceId = "Q2526264", Confidence = 0.9m } },
        });
        context.SaveChanges();

        using (var check = CreateContext())
        {
            Assert.Null(check.StoryEvents.Single(e => e.Id == a.Id).WikidataQid);
            Assert.Null(check.StoryEvents.Single(e => e.Id == a.Id).ChronologyCheckedAt);
            Assert.Equal(RelationEvidenceProvider.Wikidata, check.EventRelationEvidence.Single().Provider);
        }

        context.StoryEvents.Remove(a);
        context.SaveChanges();
        Assert.Empty(context.EventRelationDismissals);
    }
}
