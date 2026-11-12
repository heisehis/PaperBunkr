using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Continuity map M4 data (docs/superpowers/specs/2026-11-11-continuity-map-completeness-design.md §1-§2): the saved provider lists and the
/// ghost resolver. Throwaway SQLite databases only, never the user's.
/// </summary>
public sealed class EventMissingDataTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"paperbunkr_missing_{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<PaperbunkrDbContext> _options;

    public EventMissingDataTests()
    {
        _options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_path}").Options;
        using var context = new PaperbunkrDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private PaperbunkrDbContext Context() => new(_options);

    private int NewEvent(PaperbunkrDbContext context, string name)
    {
        var e = new StoryEvent { Name = name };
        context.StoryEvents.Add(e);
        context.SaveChanges();
        return e.Id;
    }

    private static ArcEntryList List(int count, int? reported = null, int skipped = 0) => new(
        Enumerable.Range(1, count).Select(i => new ArcIssue("Thor", i.ToString(), 2015, null, MetronIssueId: $"{100 + i}", MetronSeriesId: "77")).ToList(),
        Enumerable.Range(0, skipped).Select(i => $"line {i}").ToList(), reported);

    // ===================== Snapshots =====================

    [Fact]
    public void SaveReplacesTheListPerProviderAndFlagsIncompleteOnes()
    {
        using var context = Context();
        int id = NewEvent(context, "Secret Wars");
        EventArcSnapshots.Save(context, id, EventArcSnapshots.Metron, "12", List(3));
        EventArcSnapshots.Save(context, id, EventArcSnapshots.Metron, "12", List(5, reported: 9));
        EventArcSnapshots.Save(context, id, EventArcSnapshots.ComicVine, "4045", List(2, skipped: 1));

        var lists = EventArcSnapshots.Load(context, new[] { id });
        Assert.Equal(new[] { EventArcSnapshots.Metron, EventArcSnapshots.ComicVine }, lists.Select(l => l.Provider));
        Assert.Equal(5, lists[0].Entries.Count);
        Assert.False(lists[0].IsComplete);                 // reported 9, got 5
        Assert.False(lists[1].IsComplete);                 // a skipped line
        Assert.Equal("101", lists[0].Entries[0].MetronIssueId);
        Assert.Equal(2, context.EventArcSnapshots.Count());
    }

    [Fact]
    public void DeletingTheEventDeletesItsLists()
    {
        using var context = Context();
        int id = NewEvent(context, "Secret Wars");
        EventArcSnapshots.Save(context, id, EventArcSnapshots.Metron, "12", List(2));
        context.StoryEvents.Remove(context.StoryEvents.Find(id)!);
        context.SaveChanges();
        Assert.Empty(context.EventArcSnapshots);
    }

    [Fact]
    public void AMergeKeepsTheNewerListPerProvider()
    {
        using var context = Context();
        int survivor = NewEvent(context, "Secret Wars");
        int loser = NewEvent(context, "Secret Wars (2015)");
        EventArcSnapshots.Save(context, survivor, EventArcSnapshots.Metron, "1", List(2), new DateTime(2026, 1, 1));
        EventArcSnapshots.Save(context, loser, EventArcSnapshots.Metron, "2", List(4), new DateTime(2026, 6, 1));
        EventArcSnapshots.Save(context, loser, EventArcSnapshots.ComicVine, "3", List(1), new DateTime(2026, 2, 1));

        EventArcSnapshots.Merge(context, survivor, loser);
        context.SaveChanges();

        var lists = EventArcSnapshots.Load(context, new[] { survivor, loser });
        Assert.All(lists, l => Assert.Equal(survivor, l.StoryEventId));
        Assert.Equal("2", lists.Single(l => l.Provider == EventArcSnapshots.Metron).ArcId);
        Assert.Equal("3", lists.Single(l => l.Provider == EventArcSnapshots.ComicVine).ArcId);
    }

    // ===================== Ghost resolver (pure) =====================

    private static EventArcEntry Entry(string series, string number, string? metron = null, string? cv = null, int year = 2015) =>
        new(series, number, year, null, null, null, null, cv, cv is null ? null : "900", metron, metron is null ? null : "77");

    private static EventArcList Metron(params EventArcEntry[] entries) => new(1, EventArcSnapshots.Metron, "12", DateTime.UtcNow, true, entries);

    private static EventArcList ComicVine(params EventArcEntry[] entries) => new(1, EventArcSnapshots.ComicVine, "4045", DateTime.UtcNow, true, entries);

    [Fact]
    public void GhostsSitAfterTheMemberTheyFollowAndMembersAnchorByIdOrName()
    {
        var members = new[] { new EventGhostMember(10, 1, "Secret Wars", "1"), new EventGhostMember(11, 2, "Thor", "1") };
        var owned = new Dictionary<(ComicProvider, string), int> { [(ComicProvider.Metron, "m1")] = 10 };
        var ghosts = EventGhostResolver.Resolve(new[]
        {
            Metron(Entry("Weirdworld", "1", "w1"), Entry("Secret Wars", "1", "m1"), Entry("Thor", "1"), Entry("Thor", "2", "t2"), Entry("Secret Wars", "2", "s2")),
        }, members, (p, id) => owned.TryGetValue((p, id), out int i) ? i : null, (_, _, _) => false);

        Assert.Equal(new[] { "Weirdworld 1", "Thor 2", "Secret Wars 2" }, ghosts.Select(g => $"{g.Entry.Series} {g.Entry.Number}"));
        Assert.Null(ghosts[0].AfterIssueId);                       // before the first member
        Assert.Null(ghosts[0].LaneSeriesId);                       // a series with nothing on the map
        Assert.Equal(11, ghosts[1].AfterIssueId);                  // Thor #1 anchored by name and number (no provider id)
        Assert.Equal(2, ghosts[1].LaneSeriesId);
        Assert.Equal(11, ghosts[2].AfterIssueId);
        Assert.Equal(1, ghosts[2].LaneSeriesId);
        Assert.All(ghosts, g => Assert.Equal(EventGhostKind.Missing, g.Kind));
        Assert.Equal("77", ghosts[0].ProviderSeriesId);
        Assert.Equal("w1", ghosts[0].ProviderIssueId);
    }

    [Fact]
    public void OwnedNonMembersAreInYourLibraryAndLikelyOwnedOnesShowNothing()
    {
        var members = new[] { new EventGhostMember(10, 1, "Secret Wars", "1") };
        var ghosts = EventGhostResolver.Resolve(new[] { Metron(Entry("Secret Wars", "1"), Entry("Thor", "3", "t3"), Entry("Thor (2014)", "4"), Entry("Loki", "1")) },
            members,
            (p, id) => p == ComicProvider.Metron && id == "t3" ? 55 : null,
            (series, number, year) => series.StartsWith("Thor", StringComparison.Ordinal) && number == "4" && year == 2015);

        var owned = Assert.Single(ghosts, g => g.Kind == EventGhostKind.OwnedNotMember);
        Assert.Equal(55, owned.OwnedIssueId);
        Assert.DoesNotContain(ghosts, g => g.Entry.Number == "4");  // likely owned: nothing shown
        Assert.Contains(ghosts, g => g.Entry.Series == "Loki" && g.Kind == EventGhostKind.Missing);
    }

    [Fact]
    public void AComicVineListOnlyAddsWhatMetronDoesNotList()
    {
        var members = new[] { new EventGhostMember(10, 1, "Secret Wars", "1") };
        var ghosts = EventGhostResolver.Resolve(new[]
        {
            ComicVine(Entry("Thor", "1", cv: "c1"), Entry("Loki", "1", cv: "c2")),
            Metron(Entry("Thor", "1", "m1") with { ComicVineIssueId = "c1" }),
        }, members, (_, _) => null, (_, _, _) => false);

        Assert.Equal(2, ghosts.Count);
        Assert.Equal(EventArcSnapshots.Metron, ghosts.Single(g => g.Entry.Series == "Thor").Provider);
        Assert.Equal(EventArcSnapshots.ComicVine, ghosts.Single(g => g.Entry.Series == "Loki").Provider);
    }

    [Fact]
    public void SeriesNamesMatchIgnoringVolumeAndYear() =>
        Assert.True(EventGhostResolver.SameSeries("Thor (2014)", "Thor Vol. 4"));

    // ===================== Migration =====================

    [Fact]
    public void MigrationAddsTheTableAndShowMissingStaysOffForExistingSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), $"paperbunkr_arc_snap_mig_{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={path}").Options;
            using var context = new PaperbunkrDbContext(options);
            context.GetService<IMigrator>().Migrate("20261111231845_AddEventMapProgressLine");
            var connection = context.Database.GetDbConnection();
            connection.Open();
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA table_info('AppSettings')";
                using var rows = pragma.ExecuteReader();
                var names = new List<string>();
                var values = new List<string>();
                while (rows.Read())
                {
                    string name = rows.GetString(1);
                    if (name != "Id" && rows.GetInt32(3) == 0) continue;
                    names.Add("\"" + name.Replace("\"", "\"\"") + "\"");
                    values.Add(name == "Id" ? "1" : rows.GetString(2).Contains("TEXT", StringComparison.OrdinalIgnoreCase) ? "''" : "0");
                }

                rows.Close();
                using var insert = connection.CreateCommand();
                insert.CommandText = $"INSERT INTO AppSettings ({string.Join(",", names)}) VALUES ({string.Join(",", values)})";
                insert.ExecuteNonQuery();
            }

            context.GetService<IMigrator>().Migrate();
            Assert.False(context.AppSettings.AsNoTracking().Select(s => s.EventMapShowMissing).Single());
            Assert.Empty(context.EventArcSnapshots);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MigrationDownDropsOnlyItsOwnTableAndColumn()
    {
        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.Sqlite");
        typeof(Migration).GetMethod("Down", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(new Migrations.AddEventArcSnapshots(), new object[] { builder });
        Assert.Equal(2, builder.Operations.Count);
        Assert.Contains(builder.Operations, o => o is Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation { Name: "EventArcSnapshots" });
        Assert.Contains(builder.Operations, o => o is Microsoft.EntityFrameworkCore.Migrations.Operations.DropColumnOperation { Name: "EventMapShowMissing", Table: "AppSettings" });
    }
}
