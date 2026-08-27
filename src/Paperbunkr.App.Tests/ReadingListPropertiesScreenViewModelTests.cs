using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="ReadingListPropertiesScreenViewModel"/>'s buffered Load/Save/Cancel
/// (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md), including the fields this
/// screen newly consolidates (Name/Description/Type/arc-link had no prior editing UI at all -
/// Type was the only one previously edited inline on <see cref="ReadingScreenViewModel"/>) and the
/// cover picker's buffered-with-precedence behavior.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ReadingListPropertiesScreenViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _listId;

    public ReadingListPropertiesScreenViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_readinglistpropsvm_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var list = new ReadingList
        {
            Name = "Original Name",
            Description = "Original description",
            Type = ReadingListType.User,
            Source = "comicvine",
            ArcId = "arc-1",
            ArcName = "Original Arc",
            CoverImageUrl = "https://example.test/original.jpg",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        list.Tags.Add(new ReadingListTag { Value = "Dark", Category = "Tone", Weight = IssueTagWeight.Core });
        context.ReadingLists.Add(list);
        context.SaveChanges();
        _listId = list.Id;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private ReadingList GetList()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.ReadingLists.Include(r => r.Tags).First(r => r.Id == _listId);
    }

    [Fact]
    public void Load_PopulatesBufferFromDatabase_IncludingTagRows()
    {
        var vm = new ReadingListPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));

        vm.Load(_listId);

        Assert.Equal("Original Name", vm.Name);
        Assert.Equal("Original description", vm.Description);
        Assert.Equal(nameof(ReadingListType.User), vm.TypeText);
        Assert.Equal("comicvine", vm.Source);
        Assert.Equal("arc-1", vm.ArcId);
        Assert.Equal("Original Arc", vm.ArcName);
        Assert.Equal("https://example.test/original.jpg", vm.CoverImageUrl);
        Assert.Equal("Dark", vm.TagsText);

        var row = Assert.Single(vm.TagRows);
        Assert.Equal("Dark", row.Value);
        Assert.Equal("Tone", row.Category);
        Assert.Equal(IssueTagWeight.Core, row.Weight);
    }

    [Fact]
    public void Save_WritesEveryField_AndAppliesTagRowEdits()
    {
        var vm = new ReadingListPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_listId);

        vm.Name = "New Name";
        vm.Description = "New description";
        vm.TypeText = nameof(ReadingListType.Event);
        vm.Source = "mal";
        vm.ArcId = "arc-2";
        vm.ArcName = "New Arc";
        vm.TagsText = "Dark, Recommended Order";
        vm.TagRows.Single(r => r.Value == "Dark").Category = "Mood";
        vm.TagRows.Single(r => r.Value == "Dark").Weight = IssueTagWeight.Defining;

        vm.SaveCommand.Execute(null);

        var list = GetList();
        Assert.Equal("New Name", list.Name);
        Assert.Equal("New description", list.Description);
        Assert.Equal(ReadingListType.Event, list.Type);
        Assert.Equal("mal", list.Source);
        Assert.Equal("arc-2", list.ArcId);
        Assert.Equal("New Arc", list.ArcName);

        Assert.Equal(2, list.Tags.Count);
        var dark = list.Tags.Single(t => t.Value == "Dark");
        Assert.Equal("Mood", dark.Category);
        Assert.Equal(IssueTagWeight.Defining, dark.Weight);
        var newTag = list.Tags.Single(t => t.Value == "Recommended Order");
        Assert.Equal(IssueTagWeight.Unset, newTag.Weight);
    }

    [Fact]
    public void Cancel_NeverTouchesTheDatabase()
    {
        var vm = new ReadingListPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_listId);

        vm.Name = "Should Not Persist";
        vm.CancelCommand.Execute(null);

        Assert.Equal("Original Name", GetList().Name);
    }

    [Fact]
    public void Save_RemovingATagValue_DeletesItsRow()
    {
        var vm = new ReadingListPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_listId);

        vm.TagsText = string.Empty;
        vm.SaveCommand.Execute(null);

        Assert.Empty(GetList().Tags);
    }

    // ChangeCoverCommand itself calls `new FilePickerService().PickImageFileAsync(...)` directly
    // (same as DetailScreenViewModel's Issue-level "Change Cover", per its own doc comment - not on
    // IFilePickerService, so there's no fake to drive here headlessly). The buffered-until-Save and
    // local-pick-wins-over-URL behaviors this unlocks are verified on-screen instead, per this
    // project's standing practice for anything touching a real StorageProvider dialog.

    [Fact]
    public void Save_ChangedCoverImageUrlOnly_UpdatesTheField()
    {
        var vm = new ReadingListPropertiesScreenViewModel(() => { }, () => new PaperbunkrDbContext(_dbOptions));
        vm.Load(_listId);

        vm.CoverImageUrl = "https://example.test/new.jpg";
        vm.SaveCommand.Execute(null);

        Assert.Equal("https://example.test/new.jpg", GetList().CoverImageUrl);
    }
}
