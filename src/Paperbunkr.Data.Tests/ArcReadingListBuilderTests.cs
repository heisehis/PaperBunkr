using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="ArcReadingListBuilder"/> (docs/superpowers/specs/2026-08-22-cbl-manager-arc-
/// lookup-design.md §4) against a fake <see cref="IReadingListSource"/> - no live network, same
/// "small in-memory ArcIssue list" precedent the design doc's own testing section calls for.
/// </summary>
public class ArcReadingListBuilderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public ArcReadingListBuilderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_arcbuilder_test_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        _context = new PaperbunkrDbContext(options);
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

    [Fact]
    public async Task CreateFromArcAsync_BuildsListInOrderWithArcMetadata()
    {
        var source = new FakeReadingListSource("FakeSource",
            new[]
            {
                new ArcIssue("Kilo Station", "1", 2020, null),
                new ArcIssue("Kilo Station", "2", 2021, null),
            },
            new ArcOverviewInfo("A signal from the outer belt.", "https://example.com/cover.jpg"));

        var arc = new ArcSearchResult("arc-1", "Signal War", "deck", "Publisher", 2);

        var list = await ArcReadingListBuilder.CreateFromArcAsync(_context, source, arc, CancellationToken.None);

        Assert.Equal("Signal War", list.Name);
        Assert.Equal("FakeSource", list.Source);
        Assert.Equal("arc-1", list.ArcId);
        Assert.Equal("A signal from the outer belt.", list.Description);
        Assert.Equal("https://example.com/cover.jpg", list.CoverImageUrl);
        Assert.Equal(2, list.Items.Count);

        var ordered = list.Items.OrderBy(i => i.SortOrder).ToList();
        var issue1 = _context.Issues.First(i => i.Id == ordered[0].IssueId);
        var issue2 = _context.Issues.First(i => i.Id == ordered[1].IssueId);
        Assert.Equal("1", issue1.Number);
        Assert.Equal("2", issue2.Number);
        Assert.True(issue1.IsPlaceholder); // nothing in the library yet - both resolve to placeholders
    }

    [Fact]
    public async Task CreateFromArcAsync_ThrowsWhenArcHasNoIssues()
    {
        var source = new FakeReadingListSource("FakeSource", Array.Empty<ArcIssue>(), null);
        var arc = new ArcSearchResult("arc-1", "Empty Arc", null, null, 0);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ArcReadingListBuilder.CreateFromArcAsync(_context, source, arc, CancellationToken.None));
    }

    [Fact]
    public async Task RefreshAsync_KeepsRealMatchAndUpdatesSortOrder()
    {
        var series = new Series { Name = "Kilo Station", SortName = "Kilo Station" };
        _context.Series.Add(series);
        _context.SaveChanges();
        var realIssue = new Issue { SeriesId = series.Id, Number = "1", Year = 2020 };
        _context.Issues.Add(realIssue);
        _context.SaveChanges();

        var list = new ReadingList
        {
            Name = "Signal War", Source = "FakeSource", ArcId = "arc-1", ArcName = "Signal War",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        list.Items.Add(new ReadingListItem { IssueId = realIssue.Id, SortOrder = 5, Notes = "keep me" });
        _context.ReadingLists.Add(list);
        _context.SaveChanges();

        var source = new FakeReadingListSource("FakeSource", new[] { new ArcIssue("Kilo Station", "1", 2020, null) }, null);

        var result = await ArcReadingListBuilder.RefreshAsync(_context, list.Id, CancellationToken.None, source);

        Assert.Equal(0, result.AddedCount);
        Assert.Equal(0, result.ReplacedPlaceholderCount);
        var item = _context.ReadingListItems.Single(i => i.ReadingListId == list.Id);
        Assert.Equal(realIssue.Id, item.IssueId);
        Assert.Equal(0, item.SortOrder); // moved to the arc's (only) position
        Assert.Equal("keep me", item.Notes); // never touched by refresh
    }

    [Fact]
    public async Task RefreshAsync_ReplacesPlaceholderOnceARealMatchExists_AndDeletesOrphanedPlaceholder()
    {
        // Simulate an earlier Create that only found a placeholder.
        var placeholderSource = new FakeReadingListSource("FakeSource", new[] { new ArcIssue("Kilo Station", "1", 2020, null) }, null);
        var arc = new ArcSearchResult("arc-1", "Signal War", null, null, 1);
        var list = await ArcReadingListBuilder.CreateFromArcAsync(_context, placeholderSource, arc, CancellationToken.None);

        int placeholderIssueId = list.Items.Single().IssueId;
        Assert.True(_context.Issues.First(i => i.Id == placeholderIssueId).IsPlaceholder);

        // The user has since added the real book.
        var series = _context.Series.First();
        var realIssue = new Issue { SeriesId = series.Id, Number = "1", Year = 2020 };
        _context.Issues.Add(realIssue);
        _context.SaveChanges();

        var result = await ArcReadingListBuilder.RefreshAsync(_context, list.Id, CancellationToken.None, placeholderSource);

        Assert.Equal(1, result.ReplacedPlaceholderCount);
        Assert.Equal(0, result.StillMissingCount);
        var item = _context.ReadingListItems.Single(i => i.ReadingListId == list.Id);
        Assert.Equal(realIssue.Id, item.IssueId);
        Assert.Null(_context.Issues.FirstOrDefault(i => i.Id == placeholderIssueId)); // orphaned placeholder deleted
    }

    [Fact]
    public async Task RefreshAsync_KeepsOrphanedPlaceholderIssueWhenAnotherListStillReferencesIt()
    {
        var placeholderSource = new FakeReadingListSource("FakeSource", new[] { new ArcIssue("Kilo Station", "1", 2020, null) }, null);
        var arc = new ArcSearchResult("arc-1", "Signal War", null, null, 1);
        var list = await ArcReadingListBuilder.CreateFromArcAsync(_context, placeholderSource, arc, CancellationToken.None);
        int placeholderIssueId = list.Items.Single().IssueId;

        // A second, unrelated reading list also references the same placeholder.
        var otherList = new ReadingList { Name = "Other List", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        otherList.Items.Add(new ReadingListItem { IssueId = placeholderIssueId, SortOrder = 0 });
        _context.ReadingLists.Add(otherList);
        _context.SaveChanges();

        var series = _context.Series.First();
        _context.Issues.Add(new Issue { SeriesId = series.Id, Number = "1", Year = 2020 });
        _context.SaveChanges();

        await ArcReadingListBuilder.RefreshAsync(_context, list.Id, CancellationToken.None, placeholderSource);

        // Still referenced by otherList - must not be deleted.
        Assert.NotNull(_context.Issues.FirstOrDefault(i => i.Id == placeholderIssueId));
    }

    [Fact]
    public async Task RefreshAsync_AddsNewlyMatchedIssueFromExpandedArc()
    {
        var series = new Series { Name = "Kilo Station", SortName = "Kilo Station" };
        _context.Series.Add(series);
        _context.SaveChanges();
        var issue1 = new Issue { SeriesId = series.Id, Number = "1", Year = 2020 };
        _context.Issues.Add(issue1);
        _context.SaveChanges();

        var list = new ReadingList
        {
            Name = "Signal War", Source = "FakeSource", ArcId = "arc-1", ArcName = "Signal War",
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        list.Items.Add(new ReadingListItem { IssueId = issue1.Id, SortOrder = 0 });
        _context.ReadingLists.Add(list);
        _context.SaveChanges();

        // The arc now has a second issue not yet in the list.
        var source = new FakeReadingListSource("FakeSource",
            new[] { new ArcIssue("Kilo Station", "1", 2020, null), new ArcIssue("Kilo Station", "2", 2021, null) }, null);

        var result = await ArcReadingListBuilder.RefreshAsync(_context, list.Id, CancellationToken.None, source);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(2, _context.ReadingListItems.Count(i => i.ReadingListId == list.Id));
    }

    [Fact]
    public async Task RefreshAsync_ThrowsWhenListIsNotArcLinked()
    {
        var list = new ReadingList { Name = "Plain List", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _context.ReadingLists.Add(list);
        _context.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ArcReadingListBuilder.RefreshAsync(_context, list.Id, CancellationToken.None));
    }

}

/// <summary>Minimal in-memory <see cref="IReadingListSource"/> double for tests - no network.</summary>
internal sealed class FakeReadingListSource : IReadingListSource
{
    private readonly IReadOnlyList<ArcIssue> _issues;
    private readonly ArcOverviewInfo? _overview;

    public FakeReadingListSource(string sourceKey, IReadOnlyList<ArcIssue> issues, ArcOverviewInfo? overview)
    {
        SourceKey = sourceKey;
        _issues = issues;
        _overview = overview;
    }

    public string SourceKey { get; }
    public string DisplayName => SourceKey;
    public bool RequiresCredentials => false;
    public bool HasBrowsableCatalog => false;

    public Task<IReadOnlyList<ArcSearchResult>> SearchAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ArcSearchResult>>(Array.Empty<ArcSearchResult>());

    public Task<IReadOnlyList<ArcIssue>> GetArcIssuesInOrderAsync(string arcId, CancellationToken cancellationToken) =>
        Task.FromResult(_issues);

    public Task<ArcOverviewInfo?> GetArcOverviewAsync(string arcId, CancellationToken cancellationToken) =>
        Task.FromResult(_overview);
}
