using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>
/// The map's continuity scope (docs/superpowers/specs/2026-09-27-continuity-map-design.md §4), with injected data and synchronous
/// runners. The screen-side tests that were here moved with the screen to ContinuityScreen/*Tests (2026-09-28 redesign).
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class ContinuityMapViewModelTests : IDisposable
{
    private readonly string? _originalDbPathOverride;
    private readonly string _dbPath;

    public ContinuityMapViewModelTests()
    {
        _originalDbPathOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_continuity_map_test_{Guid.NewGuid():N}.db");
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

    private sealed class Harness
    {
        public readonly List<(int IssueId, int? EventId)> OpenedReader = new();
        public readonly List<int> OpenedEventMaps = new();
        public int DuplicatesShown;

        public EventMapViewModel Create(ContinuityMapData data) => new(
            openReader: (issueId, eventId) => OpenedReader.Add((issueId, eventId)),
            activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
            runInBackground: work => Task.FromResult(work()),
            loadContinuity: _ => data,
            openEventMap: id => OpenedEventMaps.Add(id),
            showDuplicates: () => DuplicatesShown++);
    }

    [Fact]
    public async Task ContinuityScope_BuildsBlocks_AndSwapsTheToolbar()
    {
        var vm = new Harness().Create(ContinuityMapTests.Sample());

        await vm.LoadContinuityAsync(7);

        Assert.True(vm.IsContinuityScope);
        Assert.False(vm.IsSpinePickerVisible);
        Assert.False(vm.CanFilterSpineOnly);
        Assert.True(vm.CanFilterEventsOnly);
        Assert.Equal(50, vm.RulerHeight);
        Assert.True(vm.Layout.IsBlockMode);
        Assert.Equal(8, vm.Cards.Count);
        Assert.Single(vm.Connectors);
        Assert.Equal("4 issues in 2 events · 4 outside events", vm.StatusText);
        Assert.True(vm.HasPendingDuplicates);
        Assert.Equal("Events in order", vm.CornerLabel);
    }

    [Fact]
    public async Task EventsOnly_AndTheEventsPicker_Relayout()
    {
        var vm = new Harness().Create(ContinuityMapTests.Sample());
        await vm.LoadContinuityAsync(7);

        vm.SetFilterCommand.Execute(EventMapFilter.EventsOnly);
        Assert.Equal(4, vm.Cards.Count);

        vm.SetFilterCommand.Execute(EventMapFilter.All);
        vm.EventChoices.Single(c => c.Name == "Planet Hulk").IsShown = false;

        Assert.DoesNotContain(vm.Cards, c => c.IssueId == 92);
        Assert.Equal(2, vm.EventChoices.Count);
    }

    [Fact]
    public async Task OpenReader_AnchorsToTheCardsEvent_LooseIssuesOpenUnanchored()
    {
        var harness = new Harness();
        var vm = harness.Create(ContinuityMapTests.Sample());
        await vm.LoadContinuityAsync(7);

        vm.Select(vm.Cards.Single(c => c.IssueId == 105).Index);
        vm.OpenReaderCommand.Execute(null);
        vm.Select(vm.Cards.Single(c => c.IssueId == 12).Index);
        vm.OpenReaderCommand.Execute(null);

        Assert.Equal(new (int, int?)[] { (105, 200), (12, null) }, harness.OpenedReader);
    }

    [Fact]
    public async Task EventAndAlsoInLinks_OpenThoseEventsMaps_AfterTheDispatcherRuns()
    {
        var harness = new Harness();
        var vm = harness.Create(ContinuityMapTests.Sample());
        await vm.LoadContinuityAsync(7);
        vm.ActivateCard(vm.Cards.Single(c => c.IssueId == 93).Index);

        var alsoIn = vm.Connections.Single(l => l.Relation == "Also in");
        Assert.Equal("World War Hulk", alsoIn.Title);
        Assert.Contains(vm.Connections, l => l.Relation == "Event" && l.Title == "Planet Hulk");

        vm.SelectLinkCommand.Execute(alsoIn);
        Assert.Empty(harness.OpenedEventMaps);
        TestDispatcher.Drain();
        Assert.Equal(new[] { 200 }, harness.OpenedEventMaps);
    }

    [Fact]
    public async Task ClickingAnEventBand_OpensThatEventsMap_DuplicateNoteOpensTheReviewList()
    {
        var harness = new Harness();
        var vm = harness.Create(ContinuityMapTests.Sample());
        await vm.LoadContinuityAsync(7);

        vm.OpenBand(1);      // Planet Hulk's band
        vm.OpenBand(0);      // a between-events band: nothing to open
        TestDispatcher.Drain();
        vm.ShowDuplicatesCommand.Execute(null);

        Assert.Equal(new[] { 100 }, harness.OpenedEventMaps);
        Assert.Equal(1, harness.DuplicatesShown);
    }

    [Fact]
    public async Task EventLoad_LeavesContinuityScope()
    {
        var vm = new EventMapViewModel(
            load: _ => new EventMapSource(1, "Crisis", null, new[] { EventMapFixtures.Row(EventMapFixtures.Crisis, "1", 1) }),
            runInBackground: work => Task.FromResult(work()),
            loadContinuity: _ => ContinuityMapTests.Sample(),
            activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }));
        await vm.LoadContinuityAsync(7);

        await vm.LoadAsync(1);

        Assert.False(vm.IsContinuityScope);
        Assert.True(vm.IsSpinePickerVisible);
        Assert.Empty(vm.EventChoices);
        Assert.Equal(28, vm.RulerHeight);
    }
}
