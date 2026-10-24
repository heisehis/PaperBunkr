using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.Tests.ReadingLists;

/// <summary>The gallery's wide list cards and their caps type caption (docs/superpowers/specs/2026-10-20-reading-lists-visual-identity-design.md).</summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadingListCardTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public ReadingListCardTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rl_cards_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var context = PaperbunkrDb.CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalDbPathOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData(ReadingListType.User, false, false, null)]
    [InlineData(ReadingListType.Community, false, false, "COMMUNITY")]
    [InlineData(ReadingListType.PublicationOrder, false, false, "PUBLICATION ORDER")]
    [InlineData(ReadingListType.Event, false, false, "EVENT")]
    [InlineData(ReadingListType.Community, true, false, "ARC")]
    [InlineData(ReadingListType.Event, true, false, "EVENT")]
    [InlineData(ReadingListType.Event, true, true, "CONTINUITY")]
    [InlineData(ReadingListType.User, false, true, "CONTINUITY")]
    public void TypeCaption_NamesTheListByWhereItCameFrom(ReadingListType type, bool hasSource, bool hasContinuity, string? expected) =>
        Assert.Equal(expected, ReadingListTypeCaption.For(type, hasSource, hasContinuity));

    [Theory]
    [InlineData(1240, 2, 620)]
    [InlineData(1000, 2, 500)]
    [InlineData(471, 1, 471)]
    [InlineData(1700, 3, 566.6666666666666)]
    [InlineData(300, 1, 300)]
    public void Layout_TakesAsManyColumnsAsFitAtTheMinimumWidth(double available, int columns, double cell)
    {
        var layout = ReadingGalleryView.Layout(available);
        Assert.Equal(columns, layout.Columns);
        Assert.Equal(cell, layout.Cell, 3);
    }

    [Theory]
    [InlineData(1240, 3, 413.333)]
    [InlineData(1700, 4, 425)]
    [InlineData(471, 1, 471)]
    public void FolderLayout_HasOneMoreColumnThanTheLists_OnceThereAreTwo(double available, int columns, double cell)
    {
        var layout = ReadingGalleryView.FolderLayout(available);
        Assert.Equal(columns, layout.Columns);
        Assert.Equal(cell, layout.Cell, 3);
    }

    [Fact]
    public void Layout_BeforeTheFirstMeasure_KeepsTheMinimumWidth()
    {
        var layout = ReadingGalleryView.Layout(0);
        Assert.Equal(1, layout.Columns);
        Assert.Equal(ReadingGalleryView.MinCardWidth + ReadingGalleryView.CardGap, layout.Cell);
    }

    // 3 folders (3 across), 5 lists (2 across): folders 0-2, lists 3-7.
    [Theory]
    [InlineData(true, 0, 3)]     // down from the first folder: the first list
    [InlineData(true, 1, 3)]     // column 1 of 3 maps to list column 0 (1 * 2 / 3)
    [InlineData(true, 2, 4)]     // column 2 of 3 maps to list column 1
    [InlineData(true, 3, 5)]     // within the lists: down a row
    [InlineData(true, 6, 7)]     // list rows are (3,4) (5,6) (7): down from the middle row reaches the short last row
    [InlineData(true, 7, null)]  // bottom row, nothing below
    [InlineData(false, 3, 0)]    // up from list column 0 -> folder column 0
    [InlineData(false, 4, 1)]    // up from list column 1 -> folder column 1 (1 * 3 / 2 = 1)
    [InlineData(false, 5, 3)]    // within the lists: up a row
    public void Vertical_CrossesTheSeamBetweenFolderAndListBands(bool down, int index, int? target)
    {
        var result = ReadingGalleryNavigation.Vertical(down, index, folders: 3, total: 8, folderColumns: 3, listColumns: 2);
        Assert.Equal(target, result.Target);
        Assert.False(result.Leaves);
    }

    [Fact]
    public void Vertical_UpFromTheTopRow_LeavesTheGrid_WithOrWithoutFolders()
    {
        Assert.True(ReadingGalleryNavigation.Vertical(false, 1, folders: 3, total: 8, folderColumns: 3, listColumns: 2).Leaves);
        Assert.True(ReadingGalleryNavigation.Vertical(false, 1, folders: 0, total: 4, folderColumns: 3, listColumns: 2).Leaves);
    }

    [Fact]
    public void Vertical_FromAShortFolderRow_AndPastAShortListRow_Clamp()
    {
        // 4 folders, 3 across: row 0 = 0-2, row 1 = 3. Down from folder 2 lands on the last folder (3), not past it.
        Assert.Equal(3, ReadingGalleryNavigation.Vertical(true, 2, folders: 4, total: 6, folderColumns: 3, listColumns: 2).Target);
        // 3 lists, 2 across: row 0 = 4-5, row 1 = 6. Down from list column 1 of row 0 lands on the last list.
        Assert.Equal(6, ReadingGalleryNavigation.Vertical(true, 5, folders: 4, total: 7, folderColumns: 3, listColumns: 2).Target);
        // Only folders: down from the last row goes nowhere.
        Assert.Null(ReadingGalleryNavigation.Vertical(true, 3, folders: 4, total: 4, folderColumns: 3, listColumns: 2).Target);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(3, true)]   // first list
    [InlineData(4, false)]
    public void IsFirstColumn_IsPerBand(int index, bool expected) =>
        Assert.Equal(expected, ReadingGalleryNavigation.IsFirstColumn(index, folders: 3, folderColumns: 3, listColumns: 2));

    private static int SeedList(string name, int total, int read, string? description = null, ReadingListType type = ReadingListType.User, string? source = null)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = new Series { Name = name };
        var list = new ReadingList { Name = name, Description = description, Type = type, Source = source };
        for (int n = 1; n <= total; n++)
        {
            var issue = new Issue
            {
                Series = series, Number = n.ToString(), FilePath = $"c:/{name}{n}.cbz", PageCount = 10, LastPageRead = n <= read ? 9 : null,
            };
            list.Items.Add(new ReadingListItem { Issue = issue, SortOrder = n });
        }

        context.ReadingLists.Add(list);
        context.SaveChanges();
        return list.Id;
    }

    private static ReadingGalleryViewModel NewGallery()
    {
        var gallery = new ReadingGalleryViewModel(new NoPicker(), new ActivityService(), null, _ => { }, _ => Task.CompletedTask);
        gallery.Refresh();
        return gallery;
    }

    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }

    [Fact]
    public void ListCard_CarriesTheTypeCaptionCountSynopsisAndFooter()
    {
        SeedList("Absolute Carnage", 3, 0, "Carnage is back and deadlier than ever, hunting every hero who has ever worn a symbiote.", ReadingListType.Community);
        var tile = NewGallery().Tiles.Single();

        Assert.Equal("COMMUNITY", tile.TypeCaption);
        Assert.True(tile.HasTypeCaption);
        Assert.Equal("· 3 ISSUES", tile.CaptionLine);
        Assert.True(tile.ShowSynopsis);
        Assert.Equal("Not started", tile.FooterLeft);
        Assert.False(tile.IsFinished);
        Assert.Equal(string.Empty, tile.FooterRight);   // no reading pace in an empty database, so no forecast
    }

    [Fact]
    public void ListCard_WithoutADescription_HasNoSynopsis_AndAPlainUserListNoCaption()
    {
        SeedList("Mine", 1, 0);
        var tile = NewGallery().Tiles.Single();

        Assert.False(tile.ShowSynopsis);
        Assert.False(tile.HasTypeCaption);
        Assert.Equal("1 ISSUE", tile.CaptionLine);
    }

    [Fact]
    public void ListCard_ReadsFinished_WhenEveryIssueIsRead_AndPartlyReadShowsTheCount()
    {
        SeedList("Done", 2, 2);
        SeedList("Half", 4, 1);
        var tiles = NewGallery().Tiles.ToDictionary(t => t.Name);

        Assert.True(tiles["Done"].IsFinished);
        Assert.Equal("✓ Finished", tiles["Done"].FooterLeft);
        Assert.False(tiles["Half"].IsFinished);
        Assert.Equal("1 / 4 read", tiles["Half"].FooterLeft);
    }

    [Fact]
    public void ListCard_ForecastsAFinishDate_OnceThereIsAReadingPace()
    {
        SeedList("Slow burn", 10, 0);
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        using (var context = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "History" };
            for (int i = 0; i < 12; i++)
            {
                var issue = new Issue { Series = series, Number = i.ToString(), FilePath = $"c:/h{i}.cbz", PageCount = 10 };
                context.Issues.Add(issue);
                context.SaveChanges();
                context.ReadingEvents.Add(new ReadingEvent
                {
                    ItemType = ReadingItemType.Comic, ItemId = issue.Id, Kind = ReadingEventKind.Finished, TimestampUtc = now.AddDays(-i),
                });
            }

            context.SaveChanges();
        }

        var gallery = new ReadingGalleryViewModel(new NoPicker(), new ActivityService(), null, _ => { }, _ => Task.CompletedTask) { UtcNow = () => now };
        gallery.Refresh();

        Assert.False(string.IsNullOrEmpty(gallery.Tiles.Single(t => t.Name == "Slow burn").FooterRight));
    }
}
