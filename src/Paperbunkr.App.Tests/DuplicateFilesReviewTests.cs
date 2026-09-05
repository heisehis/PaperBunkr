using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="NeedsReviewViewModel"/>'s "Duplicate Files" section (docs/superpowers/specs/
/// 2026-09-05-duplicate-files-review-design.md) - the fifth of its five current sections. Same
/// <see cref="PaperbunkrDbContext.DatabasePathOverride"/> + <see cref="AvaloniaTestCollection"/>
/// approach <see cref="NeedsReviewViewModelTests"/> already established, kept in its own file rather
/// than folded into that one (which is scoped to Metadata Proposals).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DuplicateFilesReviewTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private int _seriesId;

    public DuplicateFilesReviewTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_dupreview_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;

        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Kilo Station", ContentType = ContentType.Comic };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;
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

    private static NeedsReviewViewModel CreateViewModel() => new(onOpenSeriesDetail: _ => { }, filePicker: new NoOpFilePicker());

    private PaperbunkrDbContext OpenContext() => new(_dbOptions);

    /// <summary>Seeds a duplicate pair (same series+number, different file/size) - the larger sorts first per <c>BuildDuplicateGroups</c>.</summary>
    private (int LargerId, int SmallerId) SeedDuplicatePair(string number = "12", bool acknowledged = false)
    {
        using var context = OpenContext();
        var larger = new Issue { SeriesId = _seriesId, Number = number, FilePath = $@"C:\lib\kilo{number}_v1.cbz", FileSize = 50_000_000, DuplicateAcknowledged = acknowledged };
        var smaller = new Issue { SeriesId = _seriesId, Number = number, FilePath = $@"C:\lib\kilo{number}_v2.cbz", FileSize = 10_000_000, DuplicateAcknowledged = acknowledged };
        context.Issues.AddRange(larger, smaller);
        context.SaveChanges();
        return (larger.Id, smaller.Id);
    }

    [Fact]
    public void Refresh_GroupsDuplicateIssues_ExcludingFullyAcknowledgedClusters()
    {
        var (largerId, _) = SeedDuplicatePair();

        var vm = CreateViewModel();

        Assert.True(vm.HasDuplicateFileItems);
        var group = Assert.Single(vm.DuplicateGroupItems);
        Assert.Equal(2, group.Candidates.Count);
        Assert.Equal(largerId, group.Candidates[0].IssueId);
        Assert.True(group.Candidates[0].IsKeep);
        Assert.False(group.Candidates[1].IsKeep);
    }

    [Fact]
    public void Refresh_ClusterFullyAcknowledged_DoesNotAppear()
    {
        SeedDuplicatePair(acknowledged: true);

        var vm = CreateViewModel();

        Assert.False(vm.HasDuplicateFileItems);
        Assert.Empty(vm.DuplicateGroupItems);
    }

    [Fact]
    public void Refresh_ReshowsCluster_WhenNewIssueJoinsAPreviouslyDismissedGroup()
    {
        SeedDuplicatePair(acknowledged: true);

        using (var context = OpenContext())
        {
            context.Issues.Add(new Issue { SeriesId = _seriesId, Number = "12", FilePath = @"C:\lib\kilo12_v3.cbz", FileSize = 5_000_000, DuplicateAcknowledged = false });
            context.SaveChanges();
        }

        var vm = CreateViewModel();

        Assert.True(vm.HasDuplicateFileItems);
        var group = Assert.Single(vm.DuplicateGroupItems);
        Assert.Equal(3, group.Candidates.Count); // both previously-dismissed members reappear alongside the new one
    }

    [Fact]
    public void ResolveCommand_DeletesNonKeptCandidates_KeepsSelected_EvenWhenOneIsInAReadingList()
    {
        var (largerId, smallerId) = SeedDuplicatePair();

        using (var context = OpenContext())
        {
            var readingList = new ReadingList { Name = "RL" };
            context.ReadingLists.Add(readingList);
            context.SaveChanges();
            context.ReadingListItems.Add(new ReadingListItem { ReadingListId = readingList.Id, IssueId = smallerId, SortOrder = 0 });
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        var group = Assert.Single(vm.DuplicateGroupItems);

        group.ResolveCommand.Execute(null);

        using var verifyContext = OpenContext();
        var remaining = Assert.Single(verifyContext.Issues);
        Assert.Equal(largerId, remaining.Id);
        Assert.False(vm.HasDuplicateFileItems);
    }

    [Fact]
    public void DismissCommand_AcknowledgesEveryCurrentMember_WithoutDeletingAnything()
    {
        var (largerId, smallerId) = SeedDuplicatePair();
        var vm = CreateViewModel();
        var group = Assert.Single(vm.DuplicateGroupItems);

        group.DismissCommand.Execute(null);

        Assert.False(vm.HasDuplicateFileItems);
        using var verifyContext = OpenContext();
        Assert.Equal(2, verifyContext.Issues.Count());
        Assert.True(verifyContext.Issues.Single(i => i.Id == largerId).DuplicateAcknowledged);
        Assert.True(verifyContext.Issues.Single(i => i.Id == smallerId).DuplicateAcknowledged);
    }

    [Fact]
    public void KeepLargestInAllGroupsCommand_ResolvesEveryVisibleGroup()
    {
        SeedDuplicatePair(number: "12");
        SeedDuplicatePair(number: "13");
        var vm = CreateViewModel();
        Assert.Equal(2, vm.DuplicateGroupItems.Count);

        vm.KeepLargestInAllGroupsCommand.Execute(null);

        Assert.False(vm.HasDuplicateFileItems);
        using var verifyContext = OpenContext();
        Assert.Equal(2, verifyContext.Issues.Count()); // one survivor per pair
        Assert.All(verifyContext.Issues, i => Assert.Equal(50_000_000, i.FileSize));
    }

    private sealed class NoOpFilePicker : IFilePickerService
    {
        public Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel) => Task.FromResult<string?>(null);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

        public Task SetClipboardTextAsync(string text) => Task.CompletedTask;
    }
}
