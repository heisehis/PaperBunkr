using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Verifies the <c>AddStoryEventContinuityAutoPopulationSchema</c> migration (docs/superpowers/
/// specs/2026-09-17-storyevent-continuity-autopopulate-design.md) - two new nullable columns
/// (<c>StoryEvent.ComicVineArcId</c>/<c>MetronArcId</c>, <c>Continuity.WikidataId</c>) and four new
/// tables, no data fix needed since every new column is nullable and every new table is brand-new.
/// Run against real pre-existing rows, per this project's established migration-test pattern.
/// </summary>
public class AddStoryEventContinuityAutoPopulationSchemaMigrationTests : IDisposable
{
    private const string PriorMigration = "20260917034558_AddThemeSystemExtensions";
    private readonly string _dbPath;

    public AddStoryEventContinuityAutoPopulationSchemaMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_storyevent_continuity_autopop_migration_test_{Guid.NewGuid():N}.db");
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
    public void Migration_AddsNullableColumns_ToExistingRows_WithoutBreakingThem()
    {
        using (var context = CreateContext())
        {
            var migrator = context.GetService<IMigrator>();

            // Migrate to the PRIOR migration first and seed via raw SQL against that OLD schema
            // shape - a real pre-existing StoryEvent/Continuity row, exactly as it looked before
            // this migration added new columns. Seeding through the current EF model instead (which
            // already knows about WikidataId/etc.) would generate an INSERT against columns that
            // don't exist yet on this not-yet-migrated table - this is the scenario that caught the
            // SQLite non-constant-default bug in an earlier phase (see project memory), and the same
            // real-pre-existing-data discipline applies here.
            migrator.Migrate(PriorMigration);
            context.Database.ExecuteSqlRaw("INSERT INTO Series (Name, ContentType, ReadingMode) VALUES ('Avengers', 'Unknown', 'LeftToRight');");
            context.Database.ExecuteSqlRaw("INSERT INTO StoryEvents (Name, CreatedAt, UpdatedAt) VALUES ('Civil War', '2026-09-17', '2026-09-17');");
            context.Database.ExecuteSqlRaw("INSERT INTO Continuities (Name, CreatedAt, UpdatedAt) VALUES ('Earth-616', '2026-09-17', '2026-09-17');");

            migrator.Migrate();
        }

        int seriesId;
        using (var context = CreateContext())
        {
            seriesId = context.Series.Single(s => s.Name == "Avengers").Id;
            var storyEvent = context.StoryEvents.Single();
            Assert.Null(storyEvent.ComicVineArcId);
            Assert.Null(storyEvent.MetronArcId);

            var continuity = context.Continuities.Single();
            Assert.Null(continuity.WikidataId);

            storyEvent.ComicVineArcId = "4045-12345";
            continuity.WikidataId = "Q2246088";
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Equal("4045-12345", context.StoryEvents.Single().ComicVineArcId);
            Assert.Equal("Q2246088", context.Continuities.Single().WikidataId);
            Assert.True(context.Series.Any(s => s.Id == seriesId)); // untouched by the migration
        }
    }

    [Fact]
    public void Migration_CreatesFourNewTables_ThatRoundTrip()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
        }

        using (var context = CreateContext())
        {
            var series = new Series { Name = "Avengers" };
            var issue = new Issue { SeriesId = 0, Number = "1" };
            context.Series.Add(series);
            context.SaveChanges();
            issue.SeriesId = series.Id;
            context.Issues.Add(issue);
            context.SaveChanges();

            var character = new Character { Name = "Spider-Man" };
            context.Characters.Add(character);
            context.SaveChanges();

            context.StoryEventCandidateDismissals.Add(new StoryEventCandidateDismissal { ArcName = "Civil War", Publisher = "Marvel" });
            context.StoryEventVerificationNegativeCaches.Add(new StoryEventVerificationNegativeCache { ArcName = "Civil War", Publisher = "Marvel", Source = ArcVerificationSource.ComicVine });
            context.ContinuitySuggestionDismissals.Add(new ContinuitySuggestionDismissal { SeriesId = series.Id, WikidataQid = "Q2246088" });
            context.ContinuityCharacterLookupNegativeCaches.Add(new ContinuityCharacterLookupNegativeCache { CharacterId = character.Id });
            context.SaveChanges();
        }

        using (var context = CreateContext())
        {
            Assert.Single(context.StoryEventCandidateDismissals);
            Assert.Single(context.StoryEventVerificationNegativeCaches);
            Assert.Single(context.ContinuitySuggestionDismissals);
            Assert.Single(context.ContinuityCharacterLookupNegativeCaches);

            var sourceText = context.Database
                .SqlQueryRaw<string>("SELECT Source AS Value FROM StoryEventVerificationNegativeCaches LIMIT 1")
                .Single();
            Assert.Equal("ComicVine", sourceText);

            var dismissalIndexes = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_index_list('StoryEventCandidateDismissals') WHERE \"unique\" = 1;")
                .ToList();
            Assert.NotEmpty(dismissalIndexes);
        }
    }

    [Fact]
    public void Migration_IsReversible_DroppingColumnsAndTables()
    {
        using (var context = CreateContext())
        {
            context.Database.Migrate();
        }

        using (var context = CreateContext())
        {
            context.GetService<IMigrator>().Migrate(PriorMigration);

            var tables = context.Database
                .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name IN " +
                    "('StoryEventCandidateDismissals','StoryEventVerificationNegativeCaches'," +
                    "'ContinuitySuggestionDismissals','ContinuityCharacterLookupNegativeCaches');")
                .ToList();
            Assert.Empty(tables);

            var storyEventColumns = context.Database
                .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('StoryEvents') WHERE name IN ('ComicVineArcId','MetronArcId');")
                .ToList();
            Assert.Empty(storyEventColumns);
        }
    }
}
