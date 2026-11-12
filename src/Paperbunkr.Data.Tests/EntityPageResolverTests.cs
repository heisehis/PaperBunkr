using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

public class EntityPageResolverTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PaperbunkrDbContext _context;

    public EntityPageResolverTests()
    {
        _connection.Open();
        _context = new(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite(_connection).Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose() { _context.Dispose(); _connection.Dispose(); }

    private Issue Add(string seriesName, string number, int? year, bool read = false)
    {
        var series = _context.Series.Local.FirstOrDefault(s => s.Name == seriesName) ?? new Series { Name = seriesName };
        var issue = new Issue { Series = series, Number = number, Year = year, Characters = "Storm", Teams = "X-Men",
            Locations = "Westchester", Writer = "Claremont", Penciller = "Claremont", PageCount = 20, LastPageRead = read ? 19 : 0 };
        _context.Issues.Add(issue);
        _context.SaveChanges();
        EntityIndexSync.SyncIssue(_context, issue.Id);
        return issue;
    }

    [Theory]
    [InlineData(ComicMetadataEntityKind.Character)]
    [InlineData(ComicMetadataEntityKind.Team)]
    [InlineData(ComicMetadataEntityKind.Location)]
    [InlineData(ComicMetadataEntityKind.Creator)]
    public void AllKinds_DeduplicateCredits_CountRead_AndSortDatesThenNaturalNumbers(ComicMetadataEntityKind kind)
    {
        var ten = Add("Uncanny", "10", 1980);
        var two = Add("Uncanny", "2", 1980, read: true);
        var early = Add("X-Men", "1", 1975);
        var undated = Add("Alpha", "1", null);
        int id = kind switch {
            ComicMetadataEntityKind.Character => _context.Characters.Single().Id,
            ComicMetadataEntityKind.Team => _context.Teams.Single().Id,
            ComicMetadataEntityKind.Location => _context.Locations.Single().Id,
            _ => _context.Creators.Single().Id };

        var page = EntityPageResolver.Build(_context, kind, id);

        Assert.Equal(new[] { early.Id, two.Id, ten.Id, undated.Id }, page.ReadingOrder);
        Assert.Equal(4, page.AppearanceCount);
        Assert.Equal(3, page.SeriesCount);
        Assert.Equal(1, page.ReadCount);
        Assert.Equal(early.Id, page.FirstAppearance!.Id);
        Assert.Equal(3, page.Unread.Count);
        Assert.Equal(new int?[] { 1975, 1980, null }, page.ByYear.Select(y => y.Year));
        Assert.Equal(2, page.ByYear[1].Count);
        Assert.Equal(kind == ComicMetadataEntityKind.Creator ? 2 : 0, page.Roles.Count);
    }

    [Fact]
    public void Continuity_NarrowsAllCards_ButKeepsTheSwitchChoices()
    {
        var a = Add("A", "1", 1980, read: true);
        var b = Add("B", "1", 1990);
        var c1 = new Continuity { Name = "First" };
        var c2 = new Continuity { Name = "Second" };
        _context.ContinuityMemberships.AddRange(new ContinuityMembership { SeriesId = a.SeriesId, Continuity = c1 },
            new ContinuityMembership { SeriesId = b.SeriesId, Continuity = c2 });
        var e1 = new StoryEvent { Name = "Early", StartDate = new DateTime(1980, 1, 1) };
        var e2 = new StoryEvent { Name = "Later", StartDate = new DateTime(1990, 1, 1) };
        _context.EventMemberships.AddRange(new EventMembership { StoryEvent = e1, IssueId = a.Id },
            new EventMembership { StoryEvent = e2, IssueId = b.Id });
        _context.SaveChanges();
        int id = _context.Creators.Single().Id;
        var page = EntityPageResolver.Build(_context, ComicMetadataEntityKind.Creator, id, c1.Id);

        Assert.Equal(new[] { a.Id }, page.ReadingOrder);
        Assert.Equal(2, page.Continuities.Count);
        Assert.Equal("A", Assert.Single(page.Series).Name);
        Assert.Equal(1980, Assert.Single(page.ByYear).Year);
        Assert.All(page.Roles, role => Assert.Equal(1, role.Count));
        Assert.Empty(page.Unread);
        var storyEvent = Assert.Single(page.Events);
        Assert.Equal(e1.Id, storyEvent.Id);
        Assert.Equal("First", storyEvent.Continuity);
        Assert.Equal(1980, storyEvent.Year);
        Assert.Equal(new[] { e1.Id, e2.Id }, EntityPageResolver.Build(_context, ComicMetadataEntityKind.Creator, id).Events.Select(e => e.Id));
    }

    [Fact]
    public void NameLookup_IsCaseInsensitiveIncludingAccentedNames_AndIsScopedByKind()
    {
        Add("Uncanny", "1", 1980);
        _context.Characters.Add(new Character { Name = "Élektra" }); _context.SaveChanges();
        Assert.NotNull(EntityNameLookup.Find(_context, ComicMetadataEntityKind.Character, "éLEKTRA"));
        Assert.Equal(_context.Characters.Single(c => c.Name == "Storm").Id, EntityNameLookup.Find(_context, ComicMetadataEntityKind.Character, "storm"));
        Assert.Null(EntityNameLookup.Find(_context, ComicMetadataEntityKind.Team, "storm"));
    }

    [Fact]
    public void Publisher_KeepsSeriesWithoutIssues_AndUnknownEntityIsEmpty()
    {
        var publisher = new Publisher { Name = "Marvel" };
        _context.Series.Add(new Series { Name = "Empty", PublisherEntity = publisher });
        _context.SaveChanges();
        var page = EntityPageResolver.Build(_context, ComicMetadataEntityKind.Publisher, publisher.Id);
        Assert.Equal("Marvel", page.Name);
        Assert.Equal("Empty", Assert.Single(page.Series).Name);
        Assert.Empty(page.Appearances);
        Assert.Empty(EntityPageResolver.Build(_context, ComicMetadataEntityKind.Character, 999).Appearances);
    }
}
