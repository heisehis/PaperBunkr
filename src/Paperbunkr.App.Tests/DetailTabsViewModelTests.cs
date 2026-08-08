using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// First test coverage for <see cref="DetailTabsViewModel"/> - previously had none. Exercises
/// <c>ToggleReadingModeCommand</c> (docs/superpowers/specs/2026-08-07-reader-rtl-navigation-design.md
/// §5), added alongside its own <c>Func&lt;PaperbunkrDbContext&gt;</c> test-injection seam. Joins
/// <see cref="AvaloniaTestCollection"/> to match every other ViewModel test in this suite that
/// touches series/issue cover rendering (<c>SeriesCardSample</c>/<c>CoverImageCache</c>).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class DetailTabsViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;
    private readonly int _seriesId;

    public DetailTabsViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_detailtabsvm_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();

        var series = new Series { Name = "Test Series" };
        context.Series.Add(series);
        context.SaveChanges();
        _seriesId = series.Id;

        context.Issues.AddRange(
            new Issue { SeriesId = series.Id, Number = "1" },
            new Issue { SeriesId = series.Id, Number = "2" },
            new Issue { SeriesId = series.Id, Number = "3" });
        context.SaveChanges();
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

    private DetailTabsViewModel CreateViewModel(Action<int>? goToProperties = null, Action<IReadOnlyList<int>>? goToBulkProperties = null, Action? onSelectionChanged = null) =>
        new(goToProperties ?? (_ => { }), goToBulkProperties ?? (_ => { }), onSelectionChanged, () => new PaperbunkrDbContext(_dbOptions));

    private Series LoadSeriesEntity()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        return context.Series.Include(s => s.Issues).First(s => s.Id == _seriesId);
    }

    [Fact]
    public void LoadSeries_PopulatesReadingModeLabel()
    {
        var vm = CreateViewModel();

        vm.LoadSeries(LoadSeriesEntity());

        Assert.Equal("Left to Right ▾", vm.ReadingModeLabel);
    }

    [Fact]
    public void ToggleReadingMode_LeftToRight_FlipsToRightToLeft_AndPersists()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleReadingModeCommand.Execute(null);

        Assert.Equal("Right to Left ▾", vm.ReadingModeLabel);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(ReadingMode.RightToLeft, context.Series.First(s => s.Id == _seriesId).ReadingMode);
    }

    [Fact]
    public void ToggleReadingMode_RightToLeft_FlipsBackToLeftToRight()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        vm.ToggleReadingModeCommand.Execute(null);
        Assert.Equal("Right to Left ▾", vm.ReadingModeLabel);

        vm.ToggleReadingModeCommand.Execute(null);

        Assert.Equal("Left to Right ▾", vm.ReadingModeLabel);
        using var context = new PaperbunkrDbContext(_dbOptions);
        Assert.Equal(ReadingMode.LeftToRight, context.Series.First(s => s.Id == _seriesId).ReadingMode);
    }

    [Fact]
    public void EditIssueProperties_NoSelection_InvokesSinglePropertiesCallback_WithClickedIssueId()
    {
        int? capturedId = null;
        var vm = CreateViewModel(id => capturedId = id);
        vm.LoadSeries(LoadSeriesEntity());
        var issueCard = vm.Issues.First();

        vm.EditIssuePropertiesCommand.Execute(issueCard);

        Assert.Equal(issueCard.Id, capturedId);
    }

    [Fact]
    public void ToggleIssueSelection_PlainClick_TogglesSelection()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());
        var first = vm.Issues[0];

        vm.ToggleIssueSelection(first, isShiftHeld: false);
        Assert.True(first.IsSelected);
        Assert.Contains(first.Id, vm.SelectedIssueIds);

        vm.ToggleIssueSelection(first, isShiftHeld: false);
        Assert.False(first.IsSelected);
        Assert.DoesNotContain(first.Id, vm.SelectedIssueIds);
    }

    [Fact]
    public void ToggleIssueSelection_ShiftClick_SelectsContiguousRange()
    {
        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleIssueSelection(vm.Issues[0], isShiftHeld: false);
        vm.ToggleIssueSelection(vm.Issues[2], isShiftHeld: true);

        Assert.True(vm.Issues[0].IsSelected);
        Assert.True(vm.Issues[1].IsSelected);
        Assert.True(vm.Issues[2].IsSelected);
        Assert.Equal(3, vm.SelectedIssueIds.Count);
    }

    [Fact]
    public void EditIssueProperties_TwoOrMoreSelected_InvokesBulkPropertiesCallback_WithUnionOfIds()
    {
        IReadOnlyList<int>? capturedIds = null;
        var vm = CreateViewModel(goToBulkProperties: ids => capturedIds = ids);
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleIssueSelection(vm.Issues[0], isShiftHeld: false);
        vm.EditIssuePropertiesCommand.Execute(vm.Issues[1]); // right-click an unselected tile - unions it in

        Assert.NotNull(capturedIds);
        Assert.Equal(2, capturedIds!.Count);
        Assert.Contains(vm.Issues[0].Id, capturedIds);
        Assert.Contains(vm.Issues[1].Id, capturedIds);
    }

    [Fact]
    public void ToggleReadingMode_FromVerticalContinuous_CollapsesToRightToLeft()
    {
        using (var context = new PaperbunkrDbContext(_dbOptions))
        {
            context.Series.First(s => s.Id == _seriesId).ReadingMode = ReadingMode.VerticalContinuous;
            context.SaveChanges();
        }

        var vm = CreateViewModel();
        vm.LoadSeries(LoadSeriesEntity());

        vm.ToggleReadingModeCommand.Execute(null);

        Assert.Equal("Right to Left ▾", vm.ReadingModeLabel);
    }
}
