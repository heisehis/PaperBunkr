using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>docs/superpowers/specs/2026-11-12-entity-pages-redesign-design.md §6.</summary>
public class EntityRelationsTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PaperbunkrDbContext _context;

    public EntityRelationsTests()
    {
        _connection.Open();
        _context = new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose() { _context.Dispose(); _connection.Dispose(); }

    private Issue Add(string series, string number, int? year, string characters, string teams = "", string writer = "Claremont", string penciller = "Cockrum",
        string? imprint = null, bool read = false)
    {
        var s = _context.Series.Local.FirstOrDefault(x => x.Name == series) ?? new Series { Name = series, Imprint = imprint };
        var issue = new Issue { Series = s, Number = number, Year = year, Characters = characters, Teams = teams, Writer = writer, Penciller = penciller,
            PageCount = 20, LastPageRead = read ? 19 : 0 };
        _context.Issues.Add(issue);
        _context.SaveChanges();
        EntityIndexSync.SyncIssue(_context, issue.Id);
        return issue;
    }

    private int CharacterId(string name) => _context.Characters.Single(c => c.Name == name).Id;
    private int TeamId(string name) => _context.Teams.Single(c => c.Name == name).Id;
    private int CreatorId(string name) => _context.Creators.Single(c => c.Name == name).Id;

    [Fact]
    public void Seen_RanksBySharedIssues_ExcludesSelf_AndTakesTheFirstSharedIssueInReadingOrder()
    {
        var a = Add("X-Men", "1", 1975, "Storm, Wolverine, Cyclops", "X-Men");
        var b = Add("X-Men", "2", 1976, "Storm, Wolverine", "X-Men");
        var c = Add("X-Men", "3", 1977, "Storm, Wolverine, Cyclops");
        int storm = CharacterId("Storm");
        var order = EntityPageResolver.Build(_context, ComicMetadataEntityKind.Character, storm).ReadingOrder;

        var seen = EntityRelations.Seen(_context, order, take: 12, exclude: (ComicMetadataEntityKind.Character, storm));

        Assert.Equal(new[] { "Wolverine", "Cyclops", "X-Men" }, seen.Select(r => r.Name));
        Assert.Equal(new[] { 3, 2, 2 }, seen.Select(r => r.SharedCount));
        Assert.DoesNotContain(seen, r => r.Name == "Storm");
        Assert.Equal(a.Id, seen[0].FirstIssueId);
        Assert.Equal(ComicMetadataEntityKind.Team, seen[2].Kind);
        Assert.Equal(2, EntityRelations.Seen(_context, order, take: 2).Count);
        Assert.Empty(EntityRelations.Seen(_context, Array.Empty<int>(), take: 12));
        _ = b; _ = c;
    }

    [Fact]
    public void Seen_ForATeam_ListsCharactersBeforeOtherTeams_AndCanSkipTeams()
    {
        Add("Avengers", "1", 1963, "Thor", "Avengers, X-Men");
        Add("Avengers", "2", 1963, "Thor", "Avengers, X-Men");
        Add("Avengers", "3", 1963, "Hulk", "Avengers, X-Men");
        int avengers = TeamId("Avengers");
        var order = EntityPageResolver.Build(_context, ComicMetadataEntityKind.Team, avengers).ReadingOrder;

        var seen = EntityRelations.Seen(_context, order, take: 12, exclude: (ComicMetadataEntityKind.Team, avengers), charactersFirst: true);

        Assert.Equal(new[] { "Thor", "Hulk", "X-Men" }, seen.Select(r => r.Name));
        Assert.All(EntityRelations.Seen(_context, order, take: 12, includeTeams: false), r => Assert.Equal(ComicMetadataEntityKind.Character, r.Kind));
    }

    [Fact]
    public void Seen_FollowsTheContinuityFilteredReadingOrder()
    {
        var a = Add("A", "1", 1980, "Storm, Rogue");
        var b = Add("B", "1", 1990, "Storm, Gambit");
        var first = new Continuity { Name = "First" };
        _context.ContinuityMemberships.Add(new ContinuityMembership { SeriesId = a.SeriesId, Continuity = first });
        _context.ContinuityMemberships.Add(new ContinuityMembership { SeriesId = b.SeriesId, Continuity = new Continuity { Name = "Second" } });
        _context.SaveChanges();
        int storm = CharacterId("Storm");
        var order = EntityPageResolver.Build(_context, ComicMetadataEntityKind.Character, storm, first.Id).ReadingOrder;

        var seen = EntityRelations.Seen(_context, order, take: 12, exclude: (ComicMetadataEntityKind.Character, storm));

        Assert.Equal("Rogue", Assert.Single(seen).Name);
    }

    [Fact]
    public void Collaborators_CountsSharedIssuesOnce_AndExcludeTheCreator()
    {
        Add("X-Men", "1", 1975, "Storm", writer: "Claremont", penciller: "Cockrum");
        Add("X-Men", "2", 1975, "Storm", writer: "Claremont", penciller: "Byrne");
        Add("X-Men", "3", 1975, "Storm", writer: "Claremont", penciller: "Byrne");
        int claremont = CreatorId("Claremont");
        var order = EntityPageResolver.Build(_context, ComicMetadataEntityKind.Creator, claremont).ReadingOrder;

        var with = EntityRelations.Collaborators(_context, claremont, order, take: 12);

        Assert.Equal(new[] { ("Byrne", 2), ("Cockrum", 1) }, with.Select(r => (r.Name, r.SharedCount)));
        Assert.All(with, r => Assert.Equal(ComicMetadataEntityKind.Creator, r.Kind));
    }

    [Fact]
    public void CreatorCareer_FindsRunsWithTheirYears_AndRoleCountsAreDistinctIssues()
    {
        for (int n = 1; n <= 7; n++) Add("Uncanny", n.ToString(), 1980 + n / 4, "Storm", writer: "Claremont", penciller: "Claremont", read: n <= 2);
        int claremont = CreatorId("Claremont");
        var page = EntityPageResolver.Build(_context, ComicMetadataEntityKind.Creator, claremont);

        var career = EntityRelations.CreatorCareer(_context, claremont, page.ReadingOrder);

        Assert.NotEmpty(career);
        var run = career[0];
        Assert.Equal("Uncanny", run.Series);
        Assert.Equal(1980, run.FirstYear);
        Assert.Equal(1981, run.LastYear);
        Assert.Equal(7, run.IssueIds.Count);
        Assert.Equal(2, run.ReadCount);
        Assert.All(page.Roles, r => Assert.Equal(7, r.Count));
    }

    [Fact]
    public void Build_RoleNarrowsTheCards_ButKeepsEveryRoleChip()
    {
        Add("X-Men", "1", 1975, "Storm", writer: "Claremont", penciller: "Byrne");
        Add("Alpha Flight", "1", 1983, "Snowbird", writer: "Byrne", penciller: "Byrne");
        int byrne = CreatorId("Byrne");

        var writing = EntityPageResolver.Build(_context, ComicMetadataEntityKind.Creator, byrne, role: "Writer");

        Assert.Equal("Alpha Flight", Assert.Single(writing.Series).Name);
        Assert.Equal(new[] { ("Penciller", 2), ("Writer", 1) }, writing.Roles.Select(r => (r.Role, r.Count)));
    }

    [Fact]
    public void SeriesImprints_IgnoresBlankImprints()
    {
        var max = Add("Alias", "1", 2001, "Jessica Jones", imprint: "MAX");
        var blank = Add("Avengers", "1", 1963, "Thor", imprint: "  ");
        var none = Add("Thor", "1", 1966, "Thor");

        var imprints = EntityRelations.SeriesImprints(_context, new[] { max.SeriesId, blank.SeriesId, none.SeriesId });

        Assert.Equal("MAX", Assert.Single(imprints).Value);
    }
}
