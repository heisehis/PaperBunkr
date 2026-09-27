using System;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="StatsScreenViewModel"/> - the session cache and event-driven invalidation
/// (docs/superpowers/specs/2026-09-08-stats-v2-mangabaka-design.md §7). Mirrors
/// <see cref="InsightsScreenViewModelTests"/>'s shape; tile computation itself is covered by
/// <c>StatsResolverTests</c> (Paperbunkr.Data.Tests).
/// </summary>
public class StatsScreenViewModelTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;

    public StatsScreenViewModelTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_stats_vm_test_{Guid.NewGuid():N}.db");
        PaperbunkrDbContext.DatabasePathOverride = _dbPath;
        using var ctx = PaperbunkrDb.CreateContext();
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        PaperbunkrDbContext.DatabasePathOverride = _originalOverride;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }

    private static StatsScreenViewModel NewVm(IReadingEventRecorder? recorder = null)
        => new(_ => { }, recorder, () => new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Refresh_OnEmptyLibrary_DoesNotThrow_AndDefaultsToNinetyDays()
    {
        var vm = NewVm();
        vm.Refresh();

        Assert.Equal(InsightsRange.Days90, vm.Range);
        Assert.NotNull(vm.Snapshot);
        Assert.False(vm.HasPaceData);
        Assert.False(vm.HasRatings);
    }

    [Fact]
    public void SwitchingRange_BuildsAndCachesEachRangeOnce()
    {
        var vm = NewVm();
        vm.Refresh();
        var first90 = vm.Snapshot;

        vm.Range = InsightsRange.Days30;
        Assert.NotSame(first90, vm.Snapshot);

        vm.Range = InsightsRange.Days90;
        Assert.Same(first90, vm.Snapshot); // served from cache, same instance
    }

    [Fact]
    public void ReadingEvent_WhileActive_InvalidatesCacheAndRebuilds()
    {
        var recorder = new FakeRecorder();
        var vm = NewVm(recorder);
        vm.IsActive = true;
        vm.Refresh();
        var before = vm.Snapshot;

        using (var ctx = PaperbunkrDb.CreateContext())
        {
            ctx.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Comic,
                ItemId = 1,
                Kind = ReadingEventKind.Finished,
                TimestampUtc = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc),
            });
            ctx.SaveChanges();
        }

        recorder.Raise();

        Assert.NotSame(before, vm.Snapshot);
        Assert.Equal(1, vm.Snapshot!.FinishedInRange.Items);
    }

    [Fact]
    public void FinishedTrend_PopulatesFromSnapshot_AndClearsForAllTime()
    {
        // 2026-09-05 clock (NewVm): Days90's prior window needs history back to 2026-03-08.
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 1, Kind = ReadingEventKind.Opened, TimestampUtc = new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc) });
            ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 2, Kind = ReadingEventKind.Finished, TimestampUtc = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc) });
            ctx.SaveChanges();
        }

        var vm = NewVm();
        vm.Refresh();

        Assert.NotNull(vm.FinishedTrend);
        Assert.True(vm.FinishedTrend!.IsUp);
        Assert.Equal("new", vm.FinishedTrend.Text); // prior window had no Finished events at all

        vm.Range = InsightsRange.AllTime;
        Assert.Null(vm.FinishedTrend);
        Assert.Null(vm.PaceTrend);
        Assert.Null(vm.AvgToFinishTrend);
    }

    [Fact]
    public void BurnDown_ExposesStatusText_AcrossClearedAndEmptyStates()
    {
        var vm = NewVm();
        vm.Refresh();
        Assert.False(vm.HasBurnDownData); // no LibrarySnapshot rows at all yet
        Assert.Equal(string.Empty, vm.BurnDownStatusText);

        using (var ctx = PaperbunkrDb.CreateContext())
        {
            ctx.LibrarySnapshots.Add(new LibrarySnapshot { SnapshotDate = new DateOnly(2026, 9, 4), TotalOwnedComics = 10, BacklogComics = 2 });
            ctx.LibrarySnapshots.Add(new LibrarySnapshot { SnapshotDate = new DateOnly(2026, 9, 5), TotalOwnedComics = 10, BacklogComics = 0 });
            ctx.SaveChanges();
        }

        vm.Range = InsightsRange.Days30; // forces a rebuild against the fresh rows
        Assert.True(vm.HasBurnDownData);
        Assert.Equal("No backlog - all caught up", vm.BurnDownStatusText);
    }

    [Fact]
    public void SelectedTrendsGroup_DefaultsToActivity_AndSwitchingUpdatesOptionsAndBooleans()
    {
        var vm = NewVm();

        Assert.Equal(TrendsGroup.Activity, vm.SelectedTrendsGroup);
        Assert.True(vm.IsActivityGroupSelected);
        Assert.False(vm.IsCompositionGroupSelected);
        Assert.False(vm.IsTopListsGroupSelected);
        Assert.True(Assert.Single(vm.TrendsGroupOptions, o => o.Value == TrendsGroup.Activity).IsActive);

        vm.SetTrendsGroupCommand.Execute(TrendsGroup.Composition);

        Assert.Equal(TrendsGroup.Composition, vm.SelectedTrendsGroup);
        Assert.False(vm.IsActivityGroupSelected);
        Assert.True(vm.IsCompositionGroupSelected);
        Assert.False(vm.IsTopListsGroupSelected);
        Assert.True(Assert.Single(vm.TrendsGroupOptions, o => o.Value == TrendsGroup.Composition).IsActive);
        Assert.False(Assert.Single(vm.TrendsGroupOptions, o => o.Value == TrendsGroup.Activity).IsActive);
    }

    private sealed class FakeRecorder : IReadingEventRecorder
    {
        public event Action? ReadingEventRecorded;

        public void Raise() => ReadingEventRecorded?.Invoke();

        public void RecordOpened(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre) { }

        public void RecordFinished(ReadingItemType itemType, int itemId, int? seriesId, string? publisher, string? primaryGenre, int? pagesRead) { }

        public void UpdateSessionPages(ReadingItemType itemType, int itemId, int pagesRead) { }
    }
}
