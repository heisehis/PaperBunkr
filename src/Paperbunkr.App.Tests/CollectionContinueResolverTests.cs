using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>Which collection the Home hero is about and where its Continue chip goes (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md).</summary>
public class CollectionContinueResolverTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"paperbunkr_continue_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    public CollectionContinueResolverTests()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_path}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { }
    }

    private Series AddSeries(string name, params (string Number, int? Page, DateTime? Opened)[] issues)
    {
        var series = new Series { Name = name, ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
        foreach (var (number, page, opened) in issues)
        {
            series.Issues.Add(new Issue { Number = number, FilePath = $"C:\\x\\{name}{number}.cbz", LastPageRead = page, OpenedTime = opened });
        }

        _context.Series.Add(series);
        _context.SaveChanges();
        return series;
    }

    private Collection Collect(string name, params Series[] series)
    {
        var collection = CollectionService.Create(_context, name);
        CollectionService.AddItems(_context, collection.Id, seriesIds: series.Select(s => s.Id));
        return collection;
    }

    [Fact]
    public void NoActivity_MeansNoHero()
    {
        var c = Collect("A", AddSeries("S", ("1", null, null)));
        Assert.Null(CollectionContinueResolver.GetHero(_context, new[] { c.Id }));
        Assert.Null(CollectionContinueResolver.GetHero(_context, Array.Empty<int>()));
    }

    [Fact]
    public void TheNewestOpenedCollectionWins_AndItsNextUnreadIsTheChip()
    {
        var old = Collect("Old", AddSeries("OldS", ("1", 5, Now.AddDays(-10)), ("2", null, null)));
        var recent = Collect("Recent", AddSeries("RecentS", ("1", 9, Now.AddDays(-1)), ("2", null, null), ("3", null, null)));

        var hero = CollectionContinueResolver.GetHero(_context, new[] { old.Id, recent.Id });

        Assert.Equal(recent.Id, hero!.CollectionId);
        Assert.Equal("RecentS #2", hero.ContinueLabel);
        var issue = _context.Issues.Single(i => i.Id == hero.ContinueIssueId);
        Assert.Equal("2", issue.Number);
    }

    [Fact]
    public void NextUnread_FollowsCollectionOrder_AndSeriesNumberOrder()
    {
        var done = AddSeries("Done", ("1", 3, Now.AddDays(-1)));
        var open = AddSeries("Open", ("10", null, null), ("2", null, null));
        var c = Collect("A", done, open);

        var hero = CollectionContinueResolver.GetHero(_context, new[] { c.Id });

        Assert.Equal("Open #2", hero!.ContinueLabel); // first member with an unread issue; 2 before 10
    }

    [Fact]
    public void EverythingRead_GivesAHeroWithoutAChip()
    {
        var c = Collect("A", AddSeries("S", ("1", 4, Now.AddHours(-2))));

        var hero = CollectionContinueResolver.GetHero(_context, new[] { c.Id });

        Assert.Equal(c.Id, hero!.CollectionId);
        Assert.Null(hero.ContinueIssueId);
        Assert.Null(hero.ContinueLabel);
    }

    [Fact]
    public void Ties_FollowTheCallersOrder()
    {
        var s1 = AddSeries("One", ("1", 2, Now), ("2", null, null));
        var s2 = AddSeries("Two", ("1", 2, Now), ("2", null, null));
        var a = Collect("A", s1);
        var b = Collect("B", s2);

        Assert.Equal(b.Id, CollectionContinueResolver.GetHero(_context, new[] { b.Id, a.Id })!.CollectionId);
        Assert.Equal(a.Id, CollectionContinueResolver.GetHero(_context, new[] { a.Id, b.Id })!.CollectionId);
    }

    [Fact]
    public void StandaloneIssueMembers_CountForRecencyAndChip()
    {
        var series = AddSeries("Solo", ("7", null, Now.AddDays(-2)));
        var collection = CollectionService.Create(_context, "Issues");
        CollectionService.AddItems(_context, collection.Id, issueIds: new[] { series.Issues.Single().Id });

        var hero = CollectionContinueResolver.GetHero(_context, new[] { collection.Id });

        Assert.Equal("Solo #7", hero!.ContinueLabel);
    }

    [Fact]
    public void BooksAreSkipped()
    {
        var book = new Book { Title = "B", FilePath = "C:\\x\\b.epub", Status = BookReadingStatus.Unread };
        _context.Books.Add(book);
        _context.SaveChanges();
        var collection = CollectionService.Create(_context, "Books");
        CollectionService.AddItems(_context, collection.Id, bookIds: new[] { book.Id });

        Assert.Null(CollectionContinueResolver.GetHero(_context, new[] { collection.Id }));
    }
}
