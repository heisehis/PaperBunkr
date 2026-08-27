using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="ReadingListItemLinker"/> (docs/superpowers/specs/2026-08-23-cbl-manager-
/// manual-editing-and-list-aware-reading-design.md §1), same DbContext-per-test setup as
/// <see cref="ArcReadingListBuilderTests"/>.
/// </summary>
public class ReadingListItemLinkerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PaperbunkrDbContext _context;

    public ReadingListItemLinkerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_relink_test_{Guid.NewGuid():N}.db");
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

    private Series AddSeries(string name)
    {
        var series = new Series { Name = name, SortName = name };
        _context.Series.Add(series);
        _context.SaveChanges();
        return series;
    }

    private ReadingList AddList(string name)
    {
        var list = new ReadingList { Name = name, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow.AddDays(-1) };
        _context.ReadingLists.Add(list);
        _context.SaveChanges();
        return list;
    }

    [Fact]
    public void Relink_RepointsIssueId_PreservingPositionRoleAndNotes()
    {
        var series = AddSeries("Kilo Station");
        var placeholder = new Issue { SeriesId = series.Id, Number = "1", IsPlaceholder = true, FileIsMissing = true };
        var realIssue = new Issue { SeriesId = series.Id, Number = "1", IsPlaceholder = false, FileIsMissing = false };
        _context.Issues.AddRange(placeholder, realIssue);
        _context.SaveChanges();

        var list = AddList("Signal War");
        var item = new ReadingListItem { ReadingListId = list.Id, IssueId = placeholder.Id, SortOrder = 3, Role = EventMembershipRole.TieIn, Notes = "read after #2" };
        _context.ReadingListItems.Add(item);
        _context.SaveChanges();
        var beforeUpdatedAt = list.UpdatedAt;

        ReadingListItemLinker.Relink(_context, item.Id, realIssue.Id);

        var reloaded = _context.ReadingListItems.First(i => i.Id == item.Id);
        Assert.Equal(realIssue.Id, reloaded.IssueId);
        Assert.Equal(3, reloaded.SortOrder);
        Assert.Equal(EventMembershipRole.TieIn, reloaded.Role);
        Assert.Equal("read after #2", reloaded.Notes);
        Assert.True(_context.ReadingLists.First(l => l.Id == list.Id).UpdatedAt > beforeUpdatedAt);
    }

    [Fact]
    public void Relink_DeletesOrphanedPlaceholder_WhenNoOtherItemReferencesIt()
    {
        var series = AddSeries("Kilo Station");
        var placeholder = new Issue { SeriesId = series.Id, Number = "1", IsPlaceholder = true, FileIsMissing = true };
        var realIssue = new Issue { SeriesId = series.Id, Number = "1", IsPlaceholder = false, FileIsMissing = false };
        _context.Issues.AddRange(placeholder, realIssue);
        _context.SaveChanges();
        int placeholderId = placeholder.Id;

        var list = AddList("Signal War");
        var item = new ReadingListItem { ReadingListId = list.Id, IssueId = placeholderId, SortOrder = 0 };
        _context.ReadingListItems.Add(item);
        _context.SaveChanges();

        ReadingListItemLinker.Relink(_context, item.Id, realIssue.Id);

        Assert.Null(_context.Issues.FirstOrDefault(i => i.Id == placeholderId));
    }

    [Fact]
    public void Relink_KeepsPlaceholder_WhenAnotherListItemStillReferencesIt()
    {
        var series = AddSeries("Kilo Station");
        var placeholder = new Issue { SeriesId = series.Id, Number = "1", IsPlaceholder = true, FileIsMissing = true };
        var realIssue = new Issue { SeriesId = series.Id, Number = "1", IsPlaceholder = false, FileIsMissing = false };
        _context.Issues.AddRange(placeholder, realIssue);
        _context.SaveChanges();
        int placeholderId = placeholder.Id;

        var list = AddList("Signal War");
        var otherList = AddList("Another List");
        var item = new ReadingListItem { ReadingListId = list.Id, IssueId = placeholderId, SortOrder = 0 };
        var otherItem = new ReadingListItem { ReadingListId = otherList.Id, IssueId = placeholderId, SortOrder = 0 };
        _context.ReadingListItems.AddRange(item, otherItem);
        _context.SaveChanges();

        ReadingListItemLinker.Relink(_context, item.Id, realIssue.Id);

        Assert.NotNull(_context.Issues.FirstOrDefault(i => i.Id == placeholderId));
    }

    [Fact]
    public void Relink_DoesNotDeleteARealIssueWithAMissingFile()
    {
        var series = AddSeries("Kilo Station");
        var missingFileIssue = new Issue { SeriesId = series.Id, Number = "1", IsPlaceholder = false, FileIsMissing = true };
        var realIssue = new Issue { SeriesId = series.Id, Number = "2", IsPlaceholder = false, FileIsMissing = false };
        _context.Issues.AddRange(missingFileIssue, realIssue);
        _context.SaveChanges();
        int missingFileIssueId = missingFileIssue.Id;

        var list = AddList("Signal War");
        var item = new ReadingListItem { ReadingListId = list.Id, IssueId = missingFileIssueId, SortOrder = 0 };
        _context.ReadingListItems.Add(item);
        _context.SaveChanges();

        ReadingListItemLinker.Relink(_context, item.Id, realIssue.Id);

        Assert.NotNull(_context.Issues.FirstOrDefault(i => i.Id == missingFileIssueId));
    }
}
