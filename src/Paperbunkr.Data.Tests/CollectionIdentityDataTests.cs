using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.Data.Tests;

/// <summary>Ordering, pin, identity fields, the chosen cover member and progress aggregation (docs/superpowers/specs/2026-11-06-collection-identity-design.md).</summary>
public sealed class CollectionIdentityDataTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"paperbunkr_collection_identity_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;

    public CollectionIdentityDataTests()
    {
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_path}").Options;
        _context = new PaperbunkrDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }

    private Series AddSeries(string name, params int?[] lastPageRead)
    {
        var series = new Series { Name = name, ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
        int n = 1;
        foreach (int? page in lastPageRead)
        {
            series.Issues.Add(new Issue { Number = (n++).ToString(), FilePath = $"C:\\x\\{name}{n}.cbz", LastPageRead = page });
        }

        _context.Series.Add(series);
        _context.SaveChanges();
        return series;
    }

    [Fact]
    public void Ordering_PinnedFirst_ThenSortOrder_StableWithinGroups()
    {
        var a = CollectionService.Create(_context, "A");
        var b = CollectionService.Create(_context, "B");
        var c = CollectionService.Create(_context, "C");
        var d = CollectionService.Create(_context, "D");
        CollectionService.SetPinned(_context, c.Id, true);
        CollectionService.SetPinned(_context, d.Id, true);

        var names = CollectionOrdering.Apply(_context.Collections).Select(x => x.Name).ToList();
        Assert.Equal(new[] { "C", "D", "A", "B" }, names);

        CollectionService.SetPinned(_context, c.Id, false);
        names = CollectionOrdering.Apply(_context.Collections).Select(x => x.Name).ToList();
        Assert.Equal(new[] { "D", "A", "B", "C" }, names); // C keeps its SortOrder place among the unpinned

        Assert.Equal(names, CollectionOrdering.Sorted(_context.Collections.ToList()).Select(x => x.Name).ToList());
    }

    [Fact]
    public void Reorder_WithAMixedList_KeepsGroupOrderUnderTheOrderingHelper()
    {
        var a = CollectionService.Create(_context, "A");
        var b = CollectionService.Create(_context, "B");
        var c = CollectionService.Create(_context, "C");
        CollectionService.SetPinned(_context, b.Id, true);
        CollectionService.SetPinned(_context, c.Id, true);

        // Displayed order is B, C, A; swapping the pinned pair gives C, B, A.
        CollectionService.Reorder(_context, new[] { c.Id, b.Id, a.Id });

        Assert.Equal(new[] { "C", "B", "A" }, CollectionOrdering.Apply(_context.Collections).Select(x => x.Name).ToList());
    }

    [Fact]
    public void SetIdentity_PersistsFields_AndDropsAForeignOrStaleCoverMember()
    {
        var series = AddSeries("S", 1);
        var other = CollectionService.Create(_context, "Other");
        var collection = CollectionService.Create(_context, "Mine");
        CollectionService.AddItems(_context, collection.Id, seriesIds: new[] { series.Id });
        CollectionService.AddItems(_context, other.Id, seriesIds: new[] { series.Id });
        int ownItem = _context.CollectionItems.Single(ci => ci.CollectionId == collection.Id).Id;
        int foreignItem = _context.CollectionItems.Single(ci => ci.CollectionId == other.Id).Id;

        CollectionService.SetIdentity(_context, collection.Id, "Rocket", CollectionCoverStyle.Fan, ownItem, isPinned: true);
        var saved = _context.Collections.AsNoTracking().Single(c => c.Id == collection.Id);
        Assert.Equal("Rocket", saved.IconGlyph);
        Assert.Equal(CollectionCoverStyle.Fan, saved.CoverStyle);
        Assert.Equal(ownItem, saved.CoverMemberItemId);
        Assert.True(saved.IsPinned);

        CollectionService.SetIdentity(_context, collection.Id, "  ", CollectionCoverStyle.Auto, foreignItem);
        saved = _context.Collections.AsNoTracking().Single(c => c.Id == collection.Id);
        Assert.Null(saved.IconGlyph);
        Assert.Null(saved.CoverMemberItemId);
        Assert.True(saved.IsPinned); // isPinned null leaves it alone
    }

    [Fact]
    public void CoverHint_HonoursTheChosenMember_AndFallsBackWhenItIsGone()
    {
        var first = AddSeries("First", 1);
        var second = AddSeries("Second", 1);
        var collection = CollectionService.Create(_context, "C");
        CollectionService.AddItems(_context, collection.Id, seriesIds: new[] { first.Id, second.Id });
        int secondItem = _context.CollectionItems.Single(ci => ci.SeriesId == second.Id).Id;

        Assert.Equal("First", CollectionResolver.GetCoverHint(_context, collection.Id).FirstMember!.DisplayTitle);

        CollectionService.SetIdentity(_context, collection.Id, null, CollectionCoverStyle.Auto, secondItem);
        Assert.Equal("Second", CollectionResolver.GetCoverHint(_context, collection.Id).FirstMember!.DisplayTitle);

        CollectionService.RemoveItem(_context, secondItem);
        Assert.Equal("First", CollectionResolver.GetCoverHint(_context, collection.Id).FirstMember!.DisplayTitle);
    }

    [Fact]
    public void Progress_CountsSeriesIssuesAndBooks_AndSkipsEmptyCollections()
    {
        var series = AddSeries("S", 5, null, 0, 3);   // 2 of 4 started
        var standalone = AddSeries("Solo", 9).Issues.Single();
        var book = new Book { Title = "B", FilePath = "C:\\x\\b.epub", Status = BookReadingStatus.Finished };
        var unfinished = new Book { Title = "U", FilePath = "C:\\x\\u.epub", Status = BookReadingStatus.Reading };
        _context.Books.AddRange(book, unfinished);
        _context.SaveChanges();

        var mixed = CollectionService.Create(_context, "Mixed");
        CollectionService.AddItems(_context, mixed.Id, seriesIds: new[] { series.Id }, issueIds: new[] { standalone.Id }, bookIds: new[] { book.Id, unfinished.Id });
        var empty = CollectionService.Create(_context, "Empty");

        var progress = CollectionProgressResolver.GetProgress(_context, new[] { mixed.Id, empty.Id });

        Assert.False(progress.ContainsKey(empty.Id));
        var p = progress[mixed.Id];
        Assert.Equal(7, p.Total);   // 4 series issues + 1 standalone issue + 2 books
        Assert.Equal(4, p.Read);    // 2 started series issues + the standalone + the finished book
        Assert.Equal(4 / 7.0, p.Fraction, 6);
    }

    [Fact]
    public void Progress_IncludesRuleMatchedMembers()
    {
        var series = AddSeries("Rule", 1, 1);
        var smart = new SmartList { Name = "All", TargetKind = SmartListTargetKind.Series };
        _context.SmartLists.Add(smart);
        _context.SaveChanges();
        var collection = CollectionService.Create(_context, "Smart");
        CollectionService.SetSeriesSmartList(_context, collection.Id, smart.Id);

        var progress = CollectionProgressResolver.GetProgress(_context, new[] { collection.Id });

        // An empty rule matches every series; at least this one's two started issues are counted.
        Assert.True(progress.TryGetValue(collection.Id, out var p));
        Assert.True(p.Total >= 2);
        Assert.True(p.Read >= 2);
    }
}
