using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>Covers <see cref="EventMapLoader"/> (docs/superpowers/specs/2026-09-25-event-map-design.md §1 "Loading", §4) against a real temp SQLite database.</summary>
public class EventMapLoaderTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_event_map_loader_test_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;

    public EventMapLoaderTests()
    {
        _context = new PaperbunkrDbContext(new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private Series AddSeries(string name, params Issue[] issues)
    {
        var series = new Series { Name = name };
        series.Issues.AddRange(issues);
        _context.Series.Add(series);
        _context.SaveChanges();
        return series;
    }

    private StoryEvent AddEvent(string name, params (Issue Issue, int Position)[] members)
    {
        var storyEvent = new StoryEvent { Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        foreach (var (issue, position) in members)
        {
            storyEvent.Members.Add(new EventMembership { IssueId = issue.Id, Position = position, Role = EventMembershipRole.Core });
        }

        _context.StoryEvents.Add(storyEvent);
        _context.SaveChanges();
        return storyEvent;
    }

    [Fact]
    public void Load_MapsReadState_FromReadPercentage()
    {
        var unread = new Issue { Number = "1", PageCount = 20 };
        var inProgress = new Issue { Number = "2", PageCount = 20, LastPageRead = 5 };
        var read = new Issue { Number = "3", PageCount = 20, LastPageRead = 19 };
        AddSeries("Crisis", unread, inProgress, read);
        var storyEvent = AddEvent("Crisis", (unread, 1), (inProgress, 2), (read, 3));

        var source = EventMapLoader.Load(_context, storyEvent.Id)!;

        Assert.Equal(new[] { EventMapReadState.Unread, EventMapReadState.InProgress, EventMapReadState.Read }, source.Rows.Select(r => r.ReadState));
    }

    [Fact]
    public void Load_CarriesTheMissingFileFlag_AndSeriesDetails()
    {
        var missing = new Issue { Number = "4", FileIsMissing = true, Year = 1985, Month = 4, Summary = "The end." };
        var series = AddSeries("Crisis", missing);
        var storyEvent = AddEvent("Crisis on Infinite Earths", (missing, 1));

        var row = Assert.Single(EventMapLoader.Load(_context, storyEvent.Id)!.Rows);

        Assert.True(row.FileIsMissing);
        Assert.Equal(series.Id, row.SeriesId);
        Assert.Equal("Crisis", row.SeriesName);
        Assert.Equal("4", row.Number);
        Assert.Equal(1985, row.Year);
        Assert.Equal(4, row.Month);
        Assert.Equal("The end.", row.Summary);
        Assert.False(string.IsNullOrEmpty(row.CoverKey));
    }

    [Fact]
    public void Load_OrdersByPosition_ThenMembershipId()
    {
        var a = new Issue { Number = "1" };
        var b = new Issue { Number = "2" };
        var c = new Issue { Number = "3" };
        AddSeries("Crisis", a, b, c);
        var storyEvent = AddEvent("Crisis", (c, 5), (a, 9), (b, 5));

        var source = EventMapLoader.Load(_context, storyEvent.Id)!;

        Assert.Equal(new[] { "3", "2", "1" }, source.Rows.Select(r => r.Number));
        Assert.True(source.Rows[0].MembershipId < source.Rows[1].MembershipId);
    }

    [Fact]
    public void Load_UnknownEvent_ReturnsNull()
    {
        Assert.Null(EventMapLoader.Load(_context, 12345));
    }

    [Fact]
    public void SaveSpine_PersistsNullZeroAndSeriesIds()
    {
        var issue = new Issue { Number = "1" };
        AddSeries("Crisis", issue);
        var storyEvent = AddEvent("Crisis", (issue, 1));

        EventMapLoader.SaveSpine(_context, storyEvent.Id, 0);
        Assert.Equal(0, EventMapLoader.Load(_context, storyEvent.Id)!.SpineSeriesId);

        EventMapLoader.SaveSpine(_context, storyEvent.Id, null);
        Assert.Null(EventMapLoader.Load(_context, storyEvent.Id)!.SpineSeriesId);
    }

    [Fact]
    public void SetRead_MarksReadAndUnread()
    {
        var issue = new Issue { Number = "1", PageCount = 20 };
        AddSeries("Crisis", issue);

        Assert.Equal(EventMapReadState.Read, EventMapLoader.SetRead(_context, issue.Id, read: true));
        Assert.Equal(EventMapReadState.Unread, EventMapLoader.SetRead(_context, issue.Id, read: false));
    }

    [Fact]
    public void EventsInContinuity_FindsEventsThroughMemberSeries_ListedOnce()
    {
        var a1 = new Issue { Number = "1" };
        var b1 = new Issue { Number = "1" };
        var other = new Issue { Number = "1" };
        var seriesA = AddSeries("Adventure", a1);
        var seriesB = AddSeries("Brave", b1);
        AddSeries("Elsewhere", other);

        var spanning = AddEvent("Zeta Crisis", (a1, 1), (b1, 2));
        var single = AddEvent("Alpha Event", (b1, 1));
        AddEvent("Unrelated", (other, 1));

        var continuity = new Continuity { Name = "Earth-1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        continuity.Memberships.Add(new ContinuityMembership { SeriesId = seriesA.Id, SortOrder = 0 });
        continuity.Memberships.Add(new ContinuityMembership { SeriesId = seriesB.Id, SortOrder = 1 });
        _context.Continuities.Add(continuity);
        _context.SaveChanges();

        var events = EventMapLoader.EventsInContinuity(_context, continuity.Id);

        Assert.Equal(new[] { single.Id, spanning.Id }, events.Select(e => e.StoryEventId));
        Assert.Equal(2, events.Single(e => e.StoryEventId == spanning.Id).MemberCount);
    }
}
