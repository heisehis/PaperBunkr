using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>M3 (docs/superpowers/specs/2026-11-11-continuity-matrix-rollcall-design.md §1 and §3): the Map | Matrix sub-switch and the
/// roll-call panel's hide / show-all / reset / search behaviour, on the view model.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventMapMatrixModeTests
{
    private const int EventId = 1;

    private sealed class Harness(EventMapSource source)
    {
        public EventMapViewModel Create() => new(
            load: _ => source,
            saveSpine: (_, _) => { },
            setRead: (_, read) => read ? EventMapReadState.Read : EventMapReadState.Unread,
            openReader: (_, _) => { },
            activity: new Paperbunkr.App.Services.ActivityService(dispatch: a => a(), recordRun: _ => { }),
            runInBackground: work => Task.FromResult(work()));
    }

    private static EventMapSource ThreeSeries() =>
        Source("No match", Row(SeriesA, "1", 1), Row(SeriesB, "1", 2), Row(SeriesA, "2", 3), Row(Crisis, "1", 4));

    [Fact]
    public async Task SwitchingModesKeepsTheSelection()
    {
        var vm = new Harness(ThreeSeries()).Create();
        await vm.LoadAsync(EventId);
        vm.Select(2);
        int issue = vm.SelectedCard!.IssueId;

        vm.ShowMatrixModeCommand.Execute(null);
        Assert.True(vm.IsMatrixMode);
        Assert.False(vm.IsMapMode);
        Assert.Equal(issue, vm.SelectedCard!.IssueId);

        vm.ShowMapModeCommand.Execute(null);
        Assert.True(vm.IsMapMode);
        Assert.Equal(issue, vm.SelectedCard!.IssueId);
    }

    [Fact]
    public async Task HidingASeriesInTheRollCallDropsItFromMapAndMatrixButKeepsItListed()
    {
        var vm = new Harness(ThreeSeries()).Create();
        await vm.LoadAsync(EventId);
        Assert.False(vm.HasHiddenSeries);

        vm.RollCallRows.Single(r => r.SeriesId == SeriesA).IsShown = false;
        TestDispatcher.Drain();

        Assert.True(vm.HasHiddenSeries);
        Assert.DoesNotContain(vm.Cards, c => c.SeriesId == SeriesA);
        Assert.DoesNotContain(vm.Matrix.Rows, r => r.SeriesId == SeriesA);
        var row = vm.RollCallRows.Single(r => r.SeriesId == SeriesA);
        Assert.False(row.IsShown);

        vm.ShowAllSeriesCommand.Execute(null);
        TestDispatcher.Drain();
        Assert.False(vm.HasHiddenSeries);
        Assert.Contains(vm.Cards, c => c.SeriesId == SeriesA);
        Assert.Contains(vm.Matrix.Rows, r => r.SeriesId == SeriesA);
    }

    [Fact]
    public async Task LeavingTheScreenResetsHiddenSeries()
    {
        var vm = new Harness(ThreeSeries()).Create();
        await vm.LoadAsync(EventId);
        vm.RollCallRows.Single(r => r.SeriesId == SeriesB).IsShown = false;
        TestDispatcher.Drain();
        Assert.DoesNotContain(vm.Cards, c => c.SeriesId == SeriesB);

        vm.ResetHiddenSeries();
        Assert.False(vm.HasHiddenSeries);
        Assert.Contains(vm.Cards, c => c.SeriesId == SeriesB);
    }

    [Fact]
    public async Task SearchAppearsAboveTwelveSeriesAndFiltersByName()
    {
        var rows = Enumerable.Range(0, 14).Select(i => Row(100 + i, "1", i + 1)).ToArray();
        var vm = new Harness(Source("No match", rows)).Create();
        await vm.LoadAsync(EventId);
        Assert.True(vm.ShowRollCallSearch);
        Assert.Equal(14, vm.RollCallRows.Count);

        string target = vm.RollCallRows[3].Name;
        vm.RollCallSearch = target;
        Assert.All(vm.RollCallRows, r => Assert.Contains(target, r.Name, StringComparison.CurrentCultureIgnoreCase));
        Assert.NotEmpty(vm.RollCallRows);

        vm.RollCallSearch = string.Empty;
        Assert.Equal(14, vm.RollCallRows.Count);
    }

    [Fact]
    public async Task SmallMapsHaveNoRollCallSearch()
    {
        var vm = new Harness(ThreeSeries()).Create();
        await vm.LoadAsync(EventId);
        Assert.False(vm.ShowRollCallSearch);
    }

    [Fact]
    public async Task OpeningAMatrixIssueOnTheMapSwitchesModeAndSelectsIt()
    {
        var vm = new Harness(ThreeSeries()).Create();
        await vm.LoadAsync(EventId);
        vm.ShowMatrixModeCommand.Execute(null);
        vm.OpenMatrixIssueOnMap(3);
        Assert.True(vm.IsMapMode);
        Assert.Equal(3, vm.SelectedIndex);
    }
}
