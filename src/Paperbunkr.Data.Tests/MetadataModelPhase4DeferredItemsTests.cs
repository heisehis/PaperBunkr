using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>MetadataModelPhase4DeferredItems</c> migration and the resolvers built on its
/// new tables (docs/superpowers/specs/2026-08-27-metadata-model-phase4d-4g "deferred items") -
/// purely additive schema (three new tables).
/// </summary>
public class MetadataModelPhase4DeferredItemsTests : IDisposable
{
    private const string PriorMigration = "20260827193943_MetadataModelPhase4dEventRelations";
    private readonly string _dbPath;

    public MetadataModelPhase4DeferredItemsTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_phase4deferred_migration_test_{Guid.NewGuid():N}.db");
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

    private PaperbunkrDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        return new PaperbunkrDbContext(options);
    }

    [Fact]
    public void Migration_PreservesExistingRows_AddsEmptyDeferredItemTables()
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();
            migrator.Migrate(PriorMigration);
            context.Database.ExecuteSql($"INSERT INTO Series (Name, ContentType, ReadingMode, Status) VALUES ('Batman', 'Unknown', 'LeftToRight', 'Unknown');");
            migrator.Migrate();
        }

        using (var context = CreateContext())
        {
            Assert.Single(context.Series);
            Assert.Empty(context.Characters);
            Assert.Empty(context.CharacterAppearances);
            Assert.Empty(context.EventSuggestionDismissals);
        }
    }

    [Fact]
    public void EventSuggestionDismissal_CascadesFromEitherEndpoint_AndFiltersSuggestions()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var series = new Series { Name = "Avengers" };
        context.Series.Add(series);
        context.SaveChanges();
        var issue = new Issue { SeriesId = series.Id, Number = "1", Format = "Prologue", Year = 2015 };
        context.Issues.Add(issue);
        context.SaveChanges();
        var storyEvent = new StoryEvent { Name = "Secret Wars", StartDate = new DateTime(2015, 1, 1), EndDate = new DateTime(2016, 1, 1), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.Add(storyEvent);
        context.SaveChanges();

        Assert.Single(EventSuggestionResolver.GetSuggestions(context, storyEvent.Id));

        EventSuggestionResolver.Dismiss(context, storyEvent.Id, issue.Id);
        Assert.Empty(EventSuggestionResolver.GetSuggestions(context, storyEvent.Id));
        Assert.Single(EventSuggestionResolver.GetDismissed(context, storyEvent.Id));

        EventSuggestionResolver.Restore(context, storyEvent.Id, issue.Id);
        Assert.Single(EventSuggestionResolver.GetSuggestions(context, storyEvent.Id));

        // Cascade from the issue side.
        EventSuggestionResolver.Dismiss(context, storyEvent.Id, issue.Id);
        context.Issues.Remove(context.Issues.Find(issue.Id)!);
        context.SaveChanges();
        Assert.Empty(context.EventSuggestionDismissals);
    }

    [Fact]
    public void EventRelationSuggestionResolver_SuggestsBySharedWord_DateProximity_AndSharedSeries()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var a = new StoryEvent { Name = "Secret Wars", StartDate = new DateTime(2015, 5, 1), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var b = new StoryEvent { Name = "Secret Empire", StartDate = new DateTime(2017, 4, 1), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var c = new StoryEvent { Name = "Totally Unrelated", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.AddRange(a, b, c);
        context.SaveChanges();

        var suggestions = EventRelationSuggestionResolver.GetSuggestions(context, a.Id);

        Assert.Contains(suggestions, s => s.Candidate.Id == b.Id && s.Reason.Contains("Secret"));
        Assert.DoesNotContain(suggestions, s => s.Candidate.Id == c.Id);
    }

    [Fact]
    public void EventRelationResolver_GetEventFamily_ReturnsTransitiveGraphWithDepth()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var a = new StoryEvent { Name = "A", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var b = new StoryEvent { Name = "B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var d = new StoryEvent { Name = "D", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.StoryEvents.AddRange(a, b, d);
        context.SaveChanges();
        EventRelationResolver.TryCreate(context, a.Id, b.Id, RelationType.Sequel);
        EventRelationResolver.TryCreate(context, b.Id, d.Id, RelationType.Sequel);

        var family = EventRelationResolver.GetEventFamily(context, a.Id);

        Assert.Equal(3, family.Count);
        Assert.Equal(0, family.Single(f => f.Event.Id == a.Id).Depth);
        Assert.Equal(1, family.Single(f => f.Event.Id == b.Id).Depth);
        Assert.Equal(2, family.Single(f => f.Event.Id == d.Id).Depth);
    }

    [Fact]
    public void SeriesFamilyResolver_CharacterAware_PicksUpUnrelatedCharacterSharer()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var batman = new Series { Name = "Batman" };
        var oneShot = new Series { Name = "Elseworlds One-Shot" };
        context.Series.AddRange(batman, oneShot);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = batman.Id, Number = "1", Characters = "Batman" });
        context.Issues.Add(new Issue { SeriesId = oneShot.Id, Number = "1", Characters = "Batman" });
        context.SaveChanges();
        CharacterResolver.RebuildAll(context);

        Assert.Single(SeriesFamilyResolver.GetFamily(context, batman.Id, characterAware: false));
        Assert.Equal(2, SeriesFamilyResolver.GetFamily(context, batman.Id, characterAware: true).Count);
    }

    [Fact]
    public void ContinuityResolver_CrossContinuityOverlap()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var a = new Series { Name = "A" };
        var shared = new Series { Name = "Shared" };
        var b = new Series { Name = "B" };
        context.Series.AddRange(a, shared, b);
        context.SaveChanges();
        var e616 = ContinuityResolver.GetOrCreate(context, "Earth-616");
        var ult = ContinuityResolver.GetOrCreate(context, "Ultimate");
        ContinuityResolver.AddSeriesToContinuity(context, a.Id, e616.Id);
        ContinuityResolver.AddSeriesToContinuity(context, shared.Id, e616.Id);
        ContinuityResolver.AddSeriesToContinuity(context, shared.Id, ult.Id);
        ContinuityResolver.AddSeriesToContinuity(context, b.Id, ult.Id);

        var overlap = ContinuityResolver.GetOverlappingContinuities(context, e616.Id);
        Assert.Equal(ult.Id, Assert.Single(overlap).Continuity.Id);
        Assert.Equal(1, overlap[0].SharedSeriesCount);

        var both = ContinuityResolver.GetSeriesInBothContinuities(context, e616.Id, ult.Id);
        Assert.Equal("Shared", Assert.Single(both).Name);
    }

    [Fact]
    public void ContinuityReadingListBuilder_BuildsPublicationOrderList()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var s1 = new Series { Name = "Alpha" };
        var s2 = new Series { Name = "Beta" };
        context.Series.AddRange(s1, s2);
        context.SaveChanges();
        context.Issues.Add(new Issue { SeriesId = s1.Id, Number = "1", Year = 1990 });
        context.Issues.Add(new Issue { SeriesId = s2.Id, Number = "1", Year = 1985 });
        context.SaveChanges();
        var continuity = ContinuityResolver.GetOrCreate(context, "Test-Verse");
        ContinuityResolver.AddSeriesToContinuity(context, s1.Id, continuity.Id);
        ContinuityResolver.AddSeriesToContinuity(context, s2.Id, continuity.Id);

        var list = ContinuityReadingListBuilder.CreateFromContinuity(context, continuity.Id);

        Assert.Equal("Test-Verse (continuity)", list.Name);
        Assert.Equal(ReadingListType.PublicationOrder, list.Type);
        Assert.Equal(2, list.Items.Count);
        // Beta (1985) comes before Alpha (1990) - publication order, not series-name order.
        Assert.Equal(s2.Id, context.ReadingListItems.OrderBy(i => i.SortOrder).First().Issue!.SeriesId);
    }

    [Fact]
    public void BookAgeReviewResolver_ListsInferred_AcceptWritesLabel()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var series = new Series { Name = "Flash" };
        context.Series.Add(series);
        context.SaveChanges();
        var inferred = new Issue { SeriesId = series.Id, Number = "1", Year = 1965 };
        var explicitAge = new Issue { SeriesId = series.Id, Number = "2", Year = 1965, BookAge = "Golden (1938-55)" };
        context.Issues.AddRange(inferred, explicitAge);
        context.SaveChanges();

        var rows = BookAgeReviewResolver.GetInferred(context, new[] { series.Id });
        var row = Assert.Single(rows);
        Assert.Equal(inferred.Id, row.Issue.Id);
        Assert.Equal(ComicAge.Silver, row.Age);

        BookAgeReviewResolver.Accept(context, inferred.Id, ComicAge.Silver);
        Assert.Equal("Silver (1956-69)", context.Issues.Find(inferred.Id)!.BookAge);
        Assert.Empty(BookAgeReviewResolver.GetInferred(context, new[] { series.Id }));
    }
}
