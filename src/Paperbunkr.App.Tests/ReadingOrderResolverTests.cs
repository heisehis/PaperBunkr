using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services.Reader;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Covers <see cref="ReadingOrderResolver"/> (docs/superpowers/specs/2026-09-21-comic-reader-flow-and-defaults-design.md §1) against a real temp SQLite database.</summary>
public class ReadingOrderResolverTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_reading_order_test_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;

    public ReadingOrderResolverTests()
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

    private Series AddSeries(string name, params string[] numbers)
    {
        var series = new Series { Name = name };
        foreach (var n in numbers)
        {
            series.Issues.Add(new Issue { Number = n });
        }

        _context.Series.Add(series);
        _context.SaveChanges();
        return series;
    }

    private int AddList(string name, params Issue[] inOrder)
    {
        var list = new ReadingList { Name = name };
        int order = 0;
        foreach (var issue in inOrder)
        {
            list.Items.Add(new ReadingListItem { IssueId = issue.Id, SortOrder = order++ });
        }

        _context.ReadingLists.Add(list);
        _context.SaveChanges();
        return list.Id;
    }

    [Fact]
    public void Series_StepsByIssueNumber_NotInsertionOrder()
    {
        var series = AddSeries("Alpha", "10", "2", "1");
        var one = series.Issues.Single(i => i.Number == "1");
        var two = series.Issues.Single(i => i.Number == "2");

        var next = ReadingOrderResolver.ResolveNeighbour(_context, one.Id, series.Id, null, forward: true);

        Assert.NotNull(next);
        Assert.Equal(two.Id, next!.To.Id);
        Assert.Equal(2, next.ToPosition);
        Assert.Equal(3, next.Total);
        Assert.Equal("Series: Alpha", next.SourceLabel);
    }

    [Fact]
    public void Series_AtEitherEnd_ReturnsNull()
    {
        var series = AddSeries("Alpha", "1", "2");
        var one = series.Issues.Single(i => i.Number == "1");
        var two = series.Issues.Single(i => i.Number == "2");

        Assert.Null(ReadingOrderResolver.ResolveNeighbour(_context, one.Id, series.Id, null, forward: false));
        Assert.Null(ReadingOrderResolver.ResolveNeighbour(_context, two.Id, series.Id, null, forward: true));
    }

    [Fact]
    public void ReadingList_CrossesSeries_AndSkipsMissingFiles()
    {
        var a = AddSeries("A", "1");
        var b = AddSeries("B", "1", "2");
        var missing = b.Issues.Single(i => i.Number == "1");
        missing.FileIsMissing = true;
        _context.SaveChanges();
        var a1 = a.Issues.Single();
        var b2 = b.Issues.Single(i => i.Number == "2");
        int listId = AddList("Crossover", a1, missing, b2);

        var next = ReadingOrderResolver.ResolveNeighbour(_context, a1.Id, a.Id, listId, forward: true);

        Assert.NotNull(next);
        Assert.Equal(b2.Id, next!.To.Id);
        Assert.Equal(3, next.ToPosition);
        Assert.Equal("Reading list: Crossover", next.SourceLabel);
    }

    [Fact]
    public void ReadingList_StopsAtBoundary_WithNoSeriesFallback()
    {
        var a = AddSeries("A", "1", "2");
        var a1 = a.Issues.Single(i => i.Number == "1");
        int listId = AddList("Just one", a1);

        Assert.Null(ReadingOrderResolver.ResolveNeighbour(_context, a1.Id, a.Id, listId, forward: true));
    }

    [Fact]
    public void ReadingList_IssueNotInList_ReturnsNull()
    {
        var a = AddSeries("A", "1", "2");
        var a1 = a.Issues.Single(i => i.Number == "1");
        var a2 = a.Issues.Single(i => i.Number == "2");
        int listId = AddList("Just one", a1);

        Assert.Null(ReadingOrderResolver.ResolveNeighbour(_context, a2.Id, a.Id, listId, forward: false));
    }

    [Fact]
    public void Context_OpenedFromList_WinsOverEventMembership()
    {
        var a = AddSeries("A", "1", "2", "3");
        var a1 = a.Issues.Single(i => i.Number == "1");
        var a2 = a.Issues.Single(i => i.Number == "2");
        var a3 = a.Issues.Single(i => i.Number == "3");
        int listId = AddList("My order", a3, a2, a1);
        var evt = new StoryEvent { Name = "Big Event" };
        evt.Members.Add(new EventMembership { IssueId = a2.Id, Position = 0 });
        _context.StoryEvents.Add(evt);
        _context.SaveChanges();

        var ctx = ReadingOrderResolver.ResolveContext(_context, a2.Id, listId);

        Assert.NotNull(ctx);
        Assert.Equal(ReadingContextKind.ReadingList, ctx!.Kind);
        Assert.Equal("My order", ctx.Label);
        Assert.Equal(2, ctx.Position);
        Assert.Equal(3, ctx.Total);
        Assert.Equal(a3.Id, ctx.PrevIssueId);
        Assert.Equal(a1.Id, ctx.NextIssueId);
    }

    [Fact]
    public void Context_NoList_FallsBackToEventOrder_SkippingMissingFiles()
    {
        var a = AddSeries("A", "1", "2", "3");
        var a1 = a.Issues.Single(i => i.Number == "1");
        var a2 = a.Issues.Single(i => i.Number == "2");
        var a3 = a.Issues.Single(i => i.Number == "3");
        a2.FileIsMissing = true;
        var evt = new StoryEvent { Name = "Absolute Universe" };
        evt.Members.Add(new EventMembership { IssueId = a1.Id, Position = 0 });
        evt.Members.Add(new EventMembership { IssueId = a2.Id, Position = 1 });
        evt.Members.Add(new EventMembership { IssueId = a3.Id, Position = 2 });
        _context.StoryEvents.Add(evt);
        _context.SaveChanges();

        var ctx = ReadingOrderResolver.ResolveContext(_context, a1.Id, readingListId: null);

        Assert.NotNull(ctx);
        Assert.Equal(ReadingContextKind.StoryEvent, ctx!.Kind);
        Assert.Equal("Absolute Universe", ctx.Label);
        Assert.Equal(1, ctx.Position);
        Assert.Equal(3, ctx.Total);
        Assert.Null(ctx.PrevIssueId);
        Assert.Equal(a3.Id, ctx.NextIssueId);
    }

    [Fact]
    public void Context_NoListAndNoEvent_ReturnsNull()
    {
        var a = AddSeries("A", "1");

        Assert.Null(ReadingOrderResolver.ResolveContext(_context, a.Issues.Single().Id, readingListId: null));
    }
}
