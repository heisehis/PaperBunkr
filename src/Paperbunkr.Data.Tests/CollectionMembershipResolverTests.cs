using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Collections;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.Data.Tests;

/// <summary>Series / issue memberships for the Library's collection badges (docs/superpowers/specs/2026-11-06-collection-surfaces-design.md).</summary>
public sealed class CollectionMembershipResolverTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"paperbunkr_membership_{Guid.NewGuid():N}.db");
    private readonly PaperbunkrDbContext _context;

    public CollectionMembershipResolverTests()
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

    private Series AddSeries(string name)
    {
        var series = new Series { Name = name, ContentType = ContentType.Comic, ReadingMode = ReadingMode.LeftToRight, Status = SeriesStatus.Unknown };
        series.Issues.Add(new Issue { Number = "1", FilePath = $"C:\\x\\{name}.cbz" });
        _context.Series.Add(series);
        _context.SaveChanges();
        return series;
    }

    [Fact]
    public void ASeriesInSeveralCollections_ListsThemInDisplayOrder()
    {
        var series = AddSeries("S");
        var a = CollectionService.Create(_context, "A");
        var b = CollectionService.Create(_context, "B");
        var c = CollectionService.Create(_context, "C");
        CollectionService.AddItems(_context, a.Id, seriesIds: new[] { series.Id });
        CollectionService.AddItems(_context, c.Id, seriesIds: new[] { series.Id });
        CollectionService.SetPinned(_context, c.Id, true);

        var memberships = CollectionMembershipResolver.GetMemberships(_context);

        Assert.Equal(new[] { c.Id, a.Id }, memberships.BySeries[series.Id]); // pinned first, like the sidebar
        Assert.DoesNotContain(b.Id, memberships.BySeries[series.Id]);
    }

    [Fact]
    public void StandaloneIssueMembers_AreKeyedByIssue_AndNotBySeries()
    {
        var series = AddSeries("S");
        int issueId = series.Issues.Single().Id;
        var a = CollectionService.Create(_context, "A");
        CollectionService.AddItems(_context, a.Id, issueIds: new[] { issueId });

        var memberships = CollectionMembershipResolver.GetMemberships(_context);

        Assert.Equal(new[] { a.Id }, memberships.ByIssue[issueId]);
        Assert.False(memberships.BySeries.ContainsKey(series.Id));
    }

    [Fact]
    public void RuleMatchedSeries_CountAsMembers()
    {
        var series = AddSeries("Rule");
        var smart = new SmartList { Name = "All", TargetKind = SmartListTargetKind.Series };
        _context.SmartLists.Add(smart);
        _context.SaveChanges();
        var collection = CollectionService.Create(_context, "Smart");
        CollectionService.SetSeriesSmartList(_context, collection.Id, smart.Id);

        var memberships = CollectionMembershipResolver.GetMemberships(_context);

        Assert.Contains(collection.Id, memberships.BySeries[series.Id]);
    }

    [Fact]
    public void NoCollections_MeansEmpty()
    {
        AddSeries("S");
        var memberships = CollectionMembershipResolver.GetMemberships(_context);
        Assert.Empty(memberships.BySeries);
        Assert.Empty(memberships.ByIssue);
    }
}
