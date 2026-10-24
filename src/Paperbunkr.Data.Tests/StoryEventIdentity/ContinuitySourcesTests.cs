using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests.StoryEventIdentity;

/// <summary>
/// The rest of docs/superpowers/specs/2026-10-12-continuity-sources-design.md: Metron universe designations (§3), character/team provider ids
/// that survive a prune (§5) and the <c>AddEventOrderEvidence</c> migration. No network.
/// </summary>
public class ContinuitySourcesTests : IdentityTestDb
{
    private sealed class FakeUniverses : IMetronUniverses
    {
        public Dictionary<int, MetronUniverseInfo> Universes { get; } = new();

        public List<int> Asked { get; } = new();

        public bool Throw { get; set; }

        public Task<IReadOnlyList<MetronUniverseInfo>> SearchUniversesAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MetronUniverseInfo>>(Universes.Values.Where(universe => universe.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task<MetronUniverseInfo?> GetUniverseAsync(int universeId, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new ComicVineException("Metron's daily limit is nearly used up.", 107);
            }

            Asked.Add(universeId);
            return Task.FromResult(Universes.GetValueOrDefault(universeId));
        }
    }

    private int AddContinuity(string name, string? metronId, string? publisher = null, string? designation = null)
    {
        using var context = NewContext();
        var continuity = new Continuity { Name = name, MetronId = metronId, Publisher = publisher, Designation = designation, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Continuities.Add(continuity);
        context.SaveChanges();
        return continuity.Id;
    }

    private Continuity Read(int id)
    {
        using var context = NewContext();
        return context.Continuities.Single(c => c.Id == id);
    }

    // ---------- universe designations ----------

    [Fact]
    public async Task UniverseSweep_RefreshesAllFourFieldsFromMetron()
    {
        int blank = AddContinuity("Earth-616", "42");
        int mine = AddContinuity("Ultimate", "43", publisher: "Marvel (my spelling)");
        int handmade = AddContinuity("My Universe", metronId: null);
        var fake = new FakeUniverses();
        fake.Universes[42] = new MetronUniverseInfo(42, "Earth-616", "Earth-616", "Marvel", "The main universe.");
        fake.Universes[43] = new MetronUniverseInfo(43, "Ultimate Universe", "Earth-1610", "Marvel", "A rebuilt universe.");

        int updated = await ContinuityUniverseSweep.RunAsync(Factory, fake, 20, CancellationToken.None);

        Assert.Equal(2, updated);
        Assert.Equal(("Earth-616", "Earth-616", "Marvel", "The main universe."), (Read(blank).Name, Read(blank).Designation, Read(blank).Publisher, Read(blank).Description));
        Assert.Equal(("Ultimate Universe", "Earth-1610", "Marvel", "A rebuilt universe."), (Read(mine).Name, Read(mine).Designation, Read(mine).Publisher, Read(mine).Description));
        Assert.Null(Read(handmade).Designation);
        Assert.DoesNotContain(0, fake.Asked);
    }

    [Fact]
    public async Task UniverseSweep_AUniverseWithNoDesignation_IsNotAskedAgain_AndDoesNotBlockTheOnesBehindIt()
    {
        int plain = AddContinuity("Plain", "1");
        int named = AddContinuity("Named", "2");
        var fake = new FakeUniverses();
        fake.Universes[1] = new MetronUniverseInfo(1, "Plain", null, null, null);
        fake.Universes[2] = new MetronUniverseInfo(2, "Named", "Earth-2", "DC", "The DC Universe.");

        await ContinuityUniverseSweep.RunAsync(Factory, fake, 1, CancellationToken.None);
        await ContinuityUniverseSweep.RunAsync(Factory, fake, 1, CancellationToken.None);
        await ContinuityUniverseSweep.RunAsync(Factory, fake, 1, CancellationToken.None);

        Assert.Equal(new[] { 1, 2 }, fake.Asked);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (Read(plain).Designation, Read(plain).Publisher, Read(plain).Description)); // "asked, none"
        Assert.Equal("Earth-2", Read(named).Designation);
        Assert.Equal("The DC Universe.", Read(named).Description);
    }

    [Fact]
    public async Task UniverseSweep_ALimitOrLoginHold_StopsQuietly_AndNeverOverwritesYourDesignation()
    {
        int kept = AddContinuity("Kept", "7", designation: "My Earth");
        int waiting = AddContinuity("Waiting", "8");
        var fake = new FakeUniverses { Throw = true };

        int updated = await ContinuityUniverseSweep.RunAsync(Factory, fake, 20, CancellationToken.None);

        Assert.Equal(0, updated);
        Assert.Equal("My Earth", Read(kept).Designation);
        Assert.Null(Read(waiting).Designation);                              // still to be asked next run
    }

    // ---------- character / team provider ids ----------

    [Fact]
    public void ProviderIds_GoWithAPrunedCharacter_SoARecreatedOneCanHaveItBack()
    {
        var issues = AddSeries("Spider-Man", "1", "2");
        using (var context = NewContext())
        {
            context.Issues.Single(i => i.Id == issues[0].Id).Characters = "Peter Parker";
            context.SaveChanges();
            CharacterResolver.SyncFromIssue(context, issues[0].Id);
            int characterId = context.Characters.Single().Id;
            ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Character, ComicProvider.Metron, characterId, 4242);
        }

        using (var context = NewContext())
        {
            context.Issues.Single(i => i.Id == issues[0].Id).Characters = string.Empty;
            context.SaveChanges();
            CharacterResolver.SyncFromIssue(context, issues[0].Id);                 // the only appearance goes; the character is pruned
            Assert.Empty(context.Characters);
            Assert.Empty(context.ComicMetadataExternalIds);
        }

        using (var context = NewContext())
        {
            context.Issues.Single(i => i.Id == issues[1].Id).Characters = "Peter Parker";
            context.SaveChanges();
            CharacterResolver.SyncFromIssue(context, issues[1].Id);
            int again = context.Characters.Single().Id;
            ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Character, ComicProvider.Metron, again, 4242);

            var row = Assert.Single(context.ComicMetadataExternalIds);
            Assert.Equal((again, "4242"), (row.EntityId, row.ExternalId));
        }
    }

    [Fact]
    public void ProviderIds_LeftBehindByAnEarlierVersion_AreTakenOverButALiveOwnerKeepsItsId()
    {
        using var context = NewContext();
        var live = new Character { Name = "Live" };
        var fresh = new Character { Name = "Fresh" };
        var team = new Team { Name = "Avengers" };
        context.AddRange(live, fresh, team);
        context.SaveChanges();
        context.ComicMetadataExternalIds.AddRange(
            new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Character, EntityId = 9999, Provider = ComicProvider.Metron, ExternalId = "10" },     // dead owner
            new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Character, EntityId = live.Id, Provider = ComicProvider.Metron, ExternalId = "11" },
            new ComicMetadataExternalId { EntityKind = ComicMetadataEntityKind.Team, EntityId = 8888, Provider = ComicProvider.ComicVine, ExternalId = "20" });
        context.SaveChanges();

        ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Character, ComicProvider.Metron, fresh.Id, 10);      // taken over
        ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Character, ComicProvider.Metron, fresh.Id, 11);      // owned by a live row: refused
        ComicMetadataExternalIdSync.AttachEntityId(context, ComicMetadataEntityKind.Team, ComicProvider.ComicVine, team.Id, 20);

        var ids = context.ComicMetadataExternalIds.ToList();
        Assert.Equal(fresh.Id, ids.Single(e => e.EntityKind == ComicMetadataEntityKind.Character && e.ExternalId == "10").EntityId);
        Assert.Equal(live.Id, ids.Single(e => e.EntityKind == ComicMetadataEntityKind.Character && e.ExternalId == "11").EntityId);
        Assert.Equal(team.Id, ids.Single(e => e.EntityKind == ComicMetadataEntityKind.Team).EntityId);
        Assert.Equal(3, ids.Count);
    }

    // ---------- migration ----------

    [Fact]
    public void Migration_AddsTheEvidenceTable_AndTheDesignationColumn_ForwardAndBack()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_event_order_migration_test_{Guid.NewGuid():N}.db");
        try
        {
            PaperbunkrDbContext Open() => new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options);

            using (var context = Open())
            {
                context.Database.Migrate();
                var ev = new StoryEvent { Name = "Civil War", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                context.StoryEvents.Add(ev);
                context.Continuities.Add(new Continuity { Name = "Earth-616", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
                context.SaveChanges();
                context.EventOrderEvidence.Add(new EventOrderEvidence { StoryEventId = ev.Id, Provider = EventOrderProvider.Cbro, ListKey = "marvel", Rank = 5 });
                context.SaveChanges();

                Assert.Throws<DbUpdateException>(() =>
                {
                    context.EventOrderEvidence.Add(new EventOrderEvidence { StoryEventId = ev.Id, Provider = EventOrderProvider.Cbro });       // one row per (event, provider)
                    context.SaveChanges();
                });
                context.ChangeTracker.Clear();

                context.EventOrderEvidence.Add(new EventOrderEvidence { StoryEventId = ev.Id, Provider = EventOrderProvider.MetronList, SourceId = "77", NextName = "X" });
                context.SaveChanges();
                Assert.Null(context.Continuities.Single().Designation);

                context.StoryEvents.Remove(context.StoryEvents.Single());                       // evidence goes with its event
                context.SaveChanges();
                Assert.Empty(context.EventOrderEvidence);
            }

            using (var context = Open())
            {
                var migrator = context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
                var all = context.Database.GetMigrations().ToList();
                string previous = all[all.IndexOf(all.Single(m => m.EndsWith("_AddEventOrderEvidence", StringComparison.Ordinal))) - 1];
                migrator.Migrate(previous);                                                      // down …
                migrator.Migrate();                                                              // … and up again
                Assert.Empty(context.EventOrderEvidence);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}
