using Avalonia.Input;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using Paperbunkr.App.Views;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>
/// Headless tests for <see cref="EventMapViewModel"/> (docs/superpowers/specs/2026-09-25-event-map-design.md §4 and its
/// "View models" test list). The loader, spine writer and read-state writer are injected, so no database is involved.
/// Joins <see cref="AvaloniaTestCollection"/> for the pinned dispatcher thread that <see cref="TestDispatcher.Drain"/> pumps.
/// </summary>
[Collection(nameof(AvaloniaTestCollection))]
public class EventMapViewModelTests
{
    private const int EventId = 1;

    private sealed class Harness
    {
        public EventMapSource Source;
        public readonly List<(int EventId, int? Spine)> SavedSpines = new();
        public readonly List<(int IssueId, int? EventId)> OpenedReader = new();
        public readonly ActivityService Activity = new(dispatch: a => a(), recordRun: _ => { });
        public Exception? LoadFailure;
        public int Loads;

        public Harness(EventMapSource source) => Source = source;

        public EventMapViewModel Create() => new(
            load: id =>
            {
                Loads++;
                if (LoadFailure is not null) throw LoadFailure;
                return Source;
            },
            saveSpine: (id, spine) => SavedSpines.Add((id, spine)),
            setRead: (issueId, read) => read ? EventMapReadState.Read : EventMapReadState.Unread,
            openReader: (issueId, eventId) => OpenedReader.Add((issueId, eventId)),
            activity: Activity,
            runInBackground: work => Task.FromResult(work()));     // stay on the pinned test thread
    }

    private static Harness SpineHarness()
    {
        var rows = SpineSampleRows();
        rows[1] = rows[1] with { ReadState = EventMapReadState.Read };     // Crisis #1 read → first unread trunk is Crisis #2 (index 5)
        return new Harness(Source("Crisis", rows));
    }

    [Fact]
    public async Task FirstView_StandardDensity_FirstUnreadSelected_InspectorClosed()
    {
        var vm = SpineHarness().Create();

        await vm.LoadAsync(EventId);

        Assert.True(vm.HasMap);
        Assert.Equal(EventMapDensity.Standard, vm.Layout.Density);
        Assert.Equal(5, vm.SelectedIndex);
        Assert.False(vm.IsInspectorOpen);
        Assert.True(vm.Cards[5].IsSelected);
        Assert.True(vm.Cards[3].IsDimmed);          // B #1 is outside Crisis #2's related set
        Assert.False(vm.Cards[1].IsDimmed);         // the previous trunk card is related
        Assert.Equal("Spine: Crisis", vm.CornerLabel);
        Assert.StartsWith("10 issues · 3 series · spine: Crisis (auto)", vm.StatusText);
    }

    [Fact]
    public async Task EmptyEvent_GivesTheEmptyState()
    {
        var vm = new Harness(Source("Crisis")).Create();

        await vm.LoadAsync(EventId);

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasMap);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task LoaderException_GivesAnInlineErrorAndAnActivityAlert()
    {
        var harness = SpineHarness();
        harness.LoadFailure = new InvalidOperationException("database is locked");
        var vm = harness.Create();

        await vm.LoadAsync(EventId);

        Assert.True(vm.HasError);
        Assert.Contains("database is locked", vm.ErrorText);
        Assert.False(vm.HasMap);
        Assert.False(vm.IsEmpty);
        var alert = Assert.Single(harness.Activity.Alerts);
        Assert.Equal("Event map failed to load", alert.Title);
    }

    [Fact]
    public async Task SpinePicker_PersistsTheChoice_AndRelaysOut()
    {
        var harness = SpineHarness();
        var vm = harness.Create();
        await vm.LoadAsync(EventId);
        Assert.Contains("Crisis (auto)", vm.SpineOptions);
        Assert.Contains(EventMapViewModel.RelayOptionLabel, vm.SpineOptions);

        vm.SpineText = "Adventure";

        Assert.Equal((EventId, (int?)SeriesA), harness.SavedSpines.Last());
        Assert.True(vm.IsSpineMode);
        Assert.Equal("Spine: Adventure", vm.CornerLabel);
        Assert.Contains("(set)", vm.StatusText);

        vm.SpineText = EventMapViewModel.RelayOptionLabel;

        Assert.Equal((EventId, (int?)SpineResolver.ForceRelay), harness.SavedSpines.Last());
        Assert.False(vm.IsSpineMode);
        Assert.False(vm.CanFilterSpineOnly);

        vm.SpineText = "Crisis (auto)";

        Assert.Equal((EventId, (int?)null), harness.SavedSpines.Last());     // back to automatic
        Assert.True(vm.IsSpineMode);
    }

    [Fact]
    public async Task FilterChange_RelaysOut()
    {
        var harness = SpineHarness();
        harness.Source = Source("Crisis", SpineSampleRows().Select((r, i) => i == 4 ? r with { Role = EventMembershipRole.Optional } : r).ToArray());
        var vm = harness.Create();
        await vm.LoadAsync(EventId);
        var before = vm.Layout;

        vm.SetFilterCommand.Execute(EventMapFilter.HideOptional);

        Assert.NotSame(before, vm.Layout);
        Assert.Equal(9, vm.Cards.Count);
        Assert.Equal(6, vm.Layout.ColumnCount);

        vm.SetFilterCommand.Execute(EventMapFilter.SpineOnly);

        Assert.All(vm.Cards, c => Assert.True(c.IsTrunk));
    }

    [Fact]
    public async Task DensityChange_RelaysOut_AndKeepsTheSelection()
    {
        var vm = SpineHarness().Create();
        await vm.LoadAsync(EventId);
        vm.Select(7);
        int issue = vm.SelectedCard!.IssueId;

        vm.DensityIndex = (double)EventMapDensity.Covers;

        Assert.Equal(EventMapDensity.Covers, vm.Layout.Density);
        Assert.Equal(issue, vm.SelectedCard!.IssueId);

        vm.StepDensity(-1);
        vm.StepDensity(-1);
        vm.StepDensity(-1);                         // clamped at Compact

        Assert.Equal(EventMapDensity.Compact, vm.Layout.Density);
        Assert.Equal(issue, vm.SelectedCard!.IssueId);
    }

    [Fact]
    public async Task InspectorLink_ChangesTheSelectionOnlyAfterTheDispatcherRuns()
    {
        var vm = SpineHarness().Create();
        await vm.LoadAsync(EventId);
        vm.ActivateCard(7);                         // A #3
        Assert.True(vm.IsInspectorOpen);
        var tiesInto = vm.Connections.Single(l => l.Relation == "Ties into");

        vm.SelectLinkCommand.Execute(tiesInto);

        Assert.Equal(7, vm.SelectedIndex);          // deferred: the link's own list is rebuilt by the selection change
        TestDispatcher.Drain();
        Assert.Equal(5, vm.SelectedIndex);
    }

    [Fact]
    public async Task Inspector_ListsConnectionsAndSegmentOrder()
    {
        var vm = SpineHarness().Create();
        await vm.LoadAsync(EventId);

        vm.ActivateCard(7);

        Assert.Equal(new[] { "Follows", "Leads to", "Ties into" }, vm.Connections.Select(l => l.Relation));
        Assert.Equal(new[] { 5, 6, 7, 8, 9 }, vm.SegmentOrder.Select(l => l.Index));
        Assert.True(vm.SegmentOrder.Single(l => l.Index == 7).IsCurrent);
    }

    [Fact]
    public async Task MarkRead_UpdatesTheCardInPlace()
    {
        var vm = SpineHarness().Create();
        await vm.LoadAsync(EventId);
        vm.Select(7);
        var cards = vm.Cards;
        var card = vm.SelectedCard!;
        Assert.Equal("Mark read", card.MarkReadLabel);

        vm.ToggleReadCommand.Execute(null);

        Assert.Same(cards, vm.Cards);
        Assert.Equal(EventMapReadState.Read, card.ReadState);
        Assert.Equal("Mark unread", card.MarkReadLabel);

        vm.SetFilterCommand.Execute(EventMapFilter.HideOptional);   // a relayout keeps the new read state

        Assert.Equal(EventMapReadState.Read, vm.Cards[7].ReadState);
    }

    [Fact]
    public async Task OpenReader_PassesTheStoryEventId()
    {
        var harness = SpineHarness();
        var vm = harness.Create();
        await vm.LoadAsync(EventId);
        vm.Select(7);

        vm.OpenReaderCommand.Execute(null);

        Assert.Equal((vm.Cards[7].IssueId, (int?)EventId), harness.OpenedReader.Single());
    }

    [Fact]
    public async Task ReloadingTheSameEvent_WithOnlyReadStateChanges_RefreshesInPlace_AndReselects()
    {
        var harness = SpineHarness();
        var vm = harness.Create();
        await vm.LoadAsync(EventId);
        var cards = vm.Cards;
        int readerIssue = cards[8].IssueId;
        harness.Source = harness.Source with
        {
            Rows = harness.Source.Rows.Select(r => r.IssueId == cards[5].IssueId ? r with { ReadState = EventMapReadState.Read } : r).ToList(),
        };

        await vm.LoadAsync(EventId, reselectIssueId: readerIssue);

        Assert.Same(cards, vm.Cards);
        Assert.Equal(EventMapReadState.Read, cards[5].ReadState);
        Assert.Equal(8, vm.SelectedIndex);
    }

    [Theory]
    [InlineData(Key.Right, 7)]      // along lane A (A #2 -> A #3), not down the next column in reading order
    [InlineData(Key.Left, 2)]       // along lane A (A #2 -> A #1)
    [InlineData(Key.Up, 5)]         // lane A #2 (col 3) up → trunk: Crisis #1 (col 1) and Crisis #2 (col 4) → col 4 is closer
    [InlineData(Key.Down, 3)]       // A #2 (col 3) down → B #1 (col 2) vs B #2 (col 5) → col 2
    [InlineData(Key.Home, 0)]
    [InlineData(Key.End, 9)]
    public async Task Keyboard_MovesTheSelection(Key key, int expected)
    {
        var vm = SpineHarness().Create();
        await vm.LoadAsync(EventId);
        vm.Select(4);                                               // A #2: column 3, track 1

        Assert.True(EventMapView.HandleKey(vm, key, KeyModifiers.None));

        Assert.Equal(expected, vm.SelectedIndex);
    }

    [Fact]
    public async Task Keyboard_EnterOpensTheInspector_CtrlEnterOpensTheReader_EscClosesThenClears()
    {
        var harness = SpineHarness();
        var vm = harness.Create();
        await vm.LoadAsync(EventId);

        EventMapView.HandleKey(vm, Key.Enter, KeyModifiers.None);
        Assert.True(vm.IsInspectorOpen);

        EventMapView.HandleKey(vm, Key.Enter, KeyModifiers.Control);
        Assert.Single(harness.OpenedReader);

        Assert.True(EventMapView.HandleKey(vm, Key.Escape, KeyModifiers.None));
        Assert.False(vm.IsInspectorOpen);
        Assert.NotNull(vm.SelectedIndex);

        Assert.True(EventMapView.HandleKey(vm, Key.Escape, KeyModifiers.None));
        Assert.Null(vm.SelectedIndex);
        Assert.All(vm.Cards, c => Assert.False(c.IsDimmed));

        Assert.False(EventMapView.HandleKey(vm, Key.Escape, KeyModifiers.None));   // nothing left: let Esc bubble
    }

    [Fact]
    public void CardText_BadgeCoverDateAndAutomationName()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));
        var card = new EventMapCardViewModel(layout.Cells[2], layout.Tracks[1]);

        Assert.Equal("AD #1", card.Badge);
        Assert.Equal("CIE", EventMapCardViewModel.Initials("Crisis on Infinite Earths"));
        Assert.Equal("AB", EventMapCardViewModel.Initials("Absolute Batman"));
        Assert.Equal("Adventure #1, Core, unread", card.AutomationName);
        Assert.Equal("2020", EventMapCardViewModel.FormatCoverDate(2020, null));
    }
}
