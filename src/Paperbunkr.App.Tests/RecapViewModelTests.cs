using System;
using System.Linq;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="RecapViewModel"/> - the session cache, named tile accessors, and year switching
/// (docs/superpowers/specs/2026-09-23-insights-year-in-review-recap-design.md, grid layout revision in
/// 2026-09-23-insights-redesign-design.md). Tile computation itself is covered by
/// <c>RecapResolverTests</c> (Paperbunkr.Data.Tests). Same fixture shape as
/// <see cref="StatsScreenViewModelTests"/>.
/// </summary>
public class RecapViewModelTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    public RecapViewModelTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_recap_vm_test_{Guid.NewGuid():N}.db");
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

    private static RecapViewModel NewVm() => new(nowUtc: () => Now);

    [Fact]
    public void Refresh_OnEmptyLibrary_DoesNotThrow_AndProducesNineEmptyTiles()
    {
        var vm = NewVm();
        vm.Refresh();

        Assert.Empty(vm.YearOptions);
        Assert.Equal(9, vm.Tiles.Count);
        Assert.Equal("ITEMS FINISHED", vm.ItemsFinishedTile.Label);
        Assert.Equal("0", vm.ItemsFinishedTile.Value);
    }

    [Fact]
    public void Refresh_WithHistory_PopulatesYearOptionsAndTiles()
    {
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 1, Kind = ReadingEventKind.Finished, TimestampUtc = Now, PagesRead = 20 });
            ctx.SaveChanges();
        }

        var vm = NewVm();
        vm.Refresh();

        Assert.Equal(new[] { 2026 }, vm.YearOptions.Select(o => o.Value));
        Assert.True(vm.YearOptions.Single().IsActive);
        Assert.Equal("2026 (so far)", vm.YearOptions.Single().Label);
        Assert.Equal("1", vm.Tiles[0].Value);
    }

    [Fact]
    public void ChangingSelectedYear_ReResolvesSnapshotAndTiles()
    {
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 1, Kind = ReadingEventKind.Finished, TimestampUtc = Now, PagesRead = 20 });
            ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 2, Kind = ReadingEventKind.Finished, TimestampUtc = Now.AddYears(-2), PagesRead = 300 });
            ctx.SaveChanges();
        }

        var vm = NewVm();
        vm.Refresh();
        var snapshot2026 = vm.Snapshot;

        vm.SelectedYear = 2024;

        Assert.NotSame(snapshot2026, vm.Snapshot);
        Assert.Equal(2024, vm.Snapshot!.Year);
        Assert.Equal("300", vm.PagesReadTile.Value);
    }

    [Fact]
    public void TopSeriesTile_ResolvesCoverKey()
    {
        int issueId;
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            var series = new Series { Name = "Saga" };
            ctx.Series.Add(series);
            ctx.SaveChanges();
            var issue = new Issue { SeriesId = series.Id };
            ctx.Issues.Add(issue);
            ctx.SaveChanges();
            issueId = issue.Id;

            ctx.ReadingEvents.Add(new ReadingEvent
            {
                ItemType = ReadingItemType.Comic,
                ItemId = issue.Id,
                Kind = ReadingEventKind.Finished,
                TimestampUtc = Now,
                SeriesId = series.Id,
            });
            ctx.SaveChanges();
        }

        var vm = NewVm();
        vm.Refresh();

        // docs/superpowers/specs/2026-09-23-insights-redesign-design.md's global cover rule - CoverKey
        // is the cover issue's own id per CoverFingerprint.Stem, same as InsightsScreenViewModelTests'
        // equivalent assertion.
        Assert.Equal(issueId.ToString(), vm.TopSeriesTile.CoverKey);

        // ITEMS FINISHED has no coverable identity.
        Assert.Null(vm.ItemsFinishedTile.CoverKey);
    }

    [Fact]
    public void EmptyTile_RendersItsOwnMessage_NotBlank()
    {
        using (var ctx = PaperbunkrDb.CreateContext())
        {
            ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = 1, Kind = ReadingEventKind.Finished, TimestampUtc = Now, PagesRead = 20 });
            ctx.SaveChanges();
        }

        var vm = NewVm();
        vm.Refresh();

        var mostReread = vm.Tiles.Single(t => t.Label == "MOST REREAD");
        Assert.Equal("No rereads this year", mostReread.Value);
    }
}
