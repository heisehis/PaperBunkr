using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// <see cref="ReadingStatusPickerViewModel"/> - the clickable reading-status setter shared by the
/// detail hero and band (docs/superpowers/specs/2026-09-04-detail-screen-icons-and-glyphs-
/// design.md Part 2 §C). Uses the internal context-factory seam so no real per-user DB is touched.
/// </summary>
public class ReadingStatusPickerViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _seriesId;

    public ReadingStatusPickerViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_rspicker_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
        var series = new Series { Name = "Status Series" };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    private ReadingStatusPickerViewModel Create(Action? onChanged = null) =>
        new(_seriesId, () => new PaperbunkrDbContext(_dbOptions), onChanged);

    [Fact]
    public void SeedsFromTheDb_UnknownByDefault()
    {
        var vm = Create();
        Assert.Equal(ReadingStatus.Unknown, vm.Current);
        Assert.Null(vm.CurrentValue);
        Assert.False(vm.HasStatus);
        Assert.Contains(vm.Options, o => o.Value == ReadingStatus.Unknown && o.IsChecked);
    }

    [Fact]
    public void SetCommand_WritesTheDbRow_AndUpdatesState()
    {
        int changed = 0;
        var vm = Create(onChanged: () => changed++);

        vm.SetCommand.Execute(ReadingStatus.Reading);

        Assert.Equal(ReadingStatus.Reading, vm.Current);
        Assert.Equal("Reading", vm.CurrentValue);
        Assert.True(vm.HasStatus);
        Assert.Equal(1, changed);

        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(ReadingStatus.Reading, context.Series.Single(s => s.Id == _seriesId).ReadingStatus);

        Assert.Contains(vm.Options, o => o.Value == ReadingStatus.Reading && o.IsChecked);
        Assert.DoesNotContain(vm.Options, o => o.Value == ReadingStatus.Unknown && o.IsChecked);
    }

    [Fact]
    public void SetCommand_NotSet_ClearsToUnknown()
    {
        var vm = Create();
        vm.SetCommand.Execute(ReadingStatus.Completed);
        vm.SetCommand.Execute(ReadingStatus.Unknown);

        Assert.Equal(ReadingStatus.Unknown, vm.Current);
        Assert.Null(vm.CurrentValue);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(ReadingStatus.Unknown, context.Series.Single(s => s.Id == _seriesId).ReadingStatus);
    }

    [Fact]
    public void SetCommand_SameValue_IsANoOp()
    {
        int changed = 0;
        var vm = Create(onChanged: () => changed++);
        vm.SetCommand.Execute(ReadingStatus.Unknown); // already Unknown
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Options_CoverEveryStatus_WithFriendlyLabels()
    {
        var vm = Create();
        Assert.Equal(7, vm.Options.Count);
        Assert.Contains(vm.Options, o => o.Label == "Not set");
        Assert.Contains(vm.Options, o => o.Label == "On Hold");     // Paused
        Assert.Contains(vm.Options, o => o.Label == "Re-reading");   // ReReading
    }
}
