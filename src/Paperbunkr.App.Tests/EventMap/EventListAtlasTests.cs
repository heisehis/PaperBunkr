using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Paperbunkr.App.Controls.EventMap;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>M6a (docs/superpowers/specs/2026-11-11-continuity-list-atlas-views-design.md): the List sections and the continuity Atlas.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventListAtlasTests
{
    private const int EventId = 1;

    // ===================== List: pure =====================

    [Fact]
    public void SpineSectionsAreNamedChaptersAndRowsTieIntoTheirTrunkIssue()
    {
        var layout = Layout(Source("Crisis", SpineSampleRows()));
        var list = EventListBuilder.Build(layout);

        Assert.Equal("Before", list.Sections[0].Label);
        Assert.StartsWith("Chapter 1 · Crisis #1", list.Sections[1].Label);
        Assert.StartsWith("Chapter 2 · Crisis #2", list.Sections[2].Label);
        Assert.Equal(layout.Cells.Count, list.Sections.Sum(s => s.Indices.Count));
        Assert.All(list.Sections, s => Assert.Equal(s.Indices.OrderBy(i => i), s.Indices));

        int tieIn = list.Sections[1].Indices.First(i => !layout.Cells[i].IsTrunk);
        Assert.Equal("ties into Crisis #1", EventListBuilder.TiesInto(layout, tieIn));
        Assert.Null(EventListBuilder.TiesInto(layout, list.Sections[1].Indices.First(i => layout.Cells[i].IsTrunk)));
    }

    [Fact]
    public void RelayEventsAreGroupedInTens()
    {
        var rows = Enumerable.Range(0, 23).Select(i => Row(10 + (i % 3), (i + 1).ToString(), i + 1)).ToArray();
        var list = EventListBuilder.Build(Layout(Source("Unmatched", rows)));
        Assert.Equal(new[] { "1–10", "11–20", "21–23" }, list.Sections.Select(s => s.Label));
        Assert.Equal(new[] { 10, 10, 3 }, list.Sections.Select(s => s.Indices.Count));
    }

    [Fact]
    public void ContinuitySectionsFollowTheBlocks()
    {
        var map = ContinuityMapBuilder.Build(ContinuityMapTests.Sample(), ContinuityMapOptions.Default);
        var layout = EventMapLayout.ComputeBlocks(map, EventMapDensity.Standard);
        var list = EventListBuilder.Build(layout);
        Assert.Contains(list.Sections, s => s.Label == "Planet Hulk");
        Assert.Contains(list.Sections, s => s.Label == "World War Hulk");
        Assert.Equal(layout.Cells.Count, list.Sections.Sum(s => s.Indices.Count));
    }

    // ===================== List: view model =====================

    [Fact]
    public async Task ListShowsHeadersAndRowsAndCollapsingIsDeferred()
    {
        var vm = Create(Source("Crisis", SpineSampleRows()));
        await vm.LoadAsync(EventId);
        vm.ShowListModeCommand.Execute(null);
        Assert.True(vm.IsListMode);

        var headers = vm.ListItems.OfType<EventListHeaderItem>().ToList();
        Assert.Equal(vm.JumpItems.Count, headers.Count);
        Assert.Equal(vm.Cards.Count, vm.ListItems.OfType<EventListIssueItem>().Count());

        var chapter1 = headers[1];
        vm.ToggleListSectionCommand.Execute(chapter1);
        // Deferred a tick (the header's own button raised it): nothing changes until the dispatcher runs.
        Assert.Contains(vm.ListItems.OfType<EventListIssueItem>(), r => r.SectionKey == chapter1.SectionKey);
        TestDispatcher.Drain();
        Assert.DoesNotContain(vm.ListItems.OfType<EventListIssueItem>(), r => r.SectionKey == chapter1.SectionKey);
        Assert.False(vm.HeaderFor(chapter1.SectionKey)!.IsExpanded);
    }

    [Fact]
    public async Task MarkReadUpdatesCountsAndNextUpInPlace()
    {
        var vm = Create(Source("No match", Row(SeriesA, "1", 1), Row(SeriesB, "1", 2), Row(SeriesA, "2", 3)));
        await vm.LoadAsync(EventId);
        var rowsBefore = vm.ListItems.ToList();
        var first = vm.ListItems.OfType<EventListIssueItem>().First();
        Assert.True(first.IsNextUp);

        vm.Select(first.Card.Index);
        vm.ToggleReadCommand.Execute(null);

        // Same items (no rebuild under the user), new counts and a new "Next up".
        Assert.Equal(rowsBefore, vm.ListItems.ToList());
        Assert.False(first.IsNextUp);
        Assert.True(vm.ListItems.OfType<EventListIssueItem>().Skip(1).First().IsNextUp);
        Assert.Equal(1, vm.ListItems.OfType<EventListHeaderItem>().First().Read);
        Assert.Equal("1/3", vm.JumpItems.First().CountLabel);
    }

    [Fact]
    public async Task ReadThisSectionOpensTheFirstUnreadIssue()
    {
        var opened = new List<int>();
        var vm = Create(Source("No match", Row(SeriesA, "1", 1, read: EventMapReadState.Read), Row(SeriesB, "1", 2)), opened);
        await vm.LoadAsync(EventId);
        var header = vm.ListItems.OfType<EventListHeaderItem>().First();
        vm.ReadListSectionCommand.Execute(header);
        Assert.Equal(vm.Cards[1].IssueId, opened.Single());
    }

    [Fact]
    public async Task AtlasIsOnlyAvailableOnContinuities()
    {
        var vm = Create(Source("No match", Row(SeriesA, "1", 1)));
        await vm.LoadAsync(EventId);
        Assert.False(vm.IsAtlasAvailable);
        vm.ShowAtlasModeCommand.Execute(null);
        Assert.True(vm.IsMapMode);
    }

    // ===================== Atlas: pure =====================

    [Fact]
    public void MonthsCoverYearOnlyAndOneSidedDates()
    {
        Assert.Equal((2006 * 12, 2006 * 12 + 11), ContinuityAtlasBuilder.Months(200600, null));
        Assert.Equal((2006 * 12 + 3, 2006 * 12 + 4), ContinuityAtlasBuilder.Months(200604, 200605));
        Assert.Equal((2007 * 12 + 5, 2007 * 12 + 5), ContinuityAtlasBuilder.Months(null, 200706));
        Assert.Equal((2007 * 12, 2007 * 12 + 11), ContinuityAtlasBuilder.Months(200700, 200700));
        Assert.Null(ContinuityAtlasBuilder.Months(null, null));
    }

    [Fact]
    public void AtlasPlacesEventsChronologicallyWithConnectorsAndTheLooseBand()
    {
        var data = ContinuityMapTests.Sample();
        var atlas = ContinuityAtlasBuilder.Build(data, null, id => id == 92);

        Assert.Equal(new[] { "Planet Hulk", "World War Hulk" }, atlas.Bars.Select(b => b.Name));
        Assert.All(atlas.Bars, b => Assert.Equal(0, b.Row));                       // a year apart: one row
        var planet = atlas.Bars[0];
        Assert.Equal(0.5, planet.ReadFraction);
        Assert.Equal("2006", planet.YearsLabel);
        Assert.Single(atlas.Connectors);
        Assert.True(atlas.Connectors[0].IsYours);
        Assert.Equal(planet.EventId, atlas.Connectors[0].EarlierEventId);
        Assert.Equal(1, atlas.LooseByYear[1990]);
        Assert.Equal(2, atlas.LooseByYear[2006]);
        Assert.False(atlas.LooseByYear.ContainsKey(0));                            // the undated loose issue is left out
    }

    [Fact]
    public void OverlappingEventsStackAndUndatedOnesAreListed()
    {
        var row = ContinuityMapTests.Row(1, 1, "1", 2006);
        var events = new[]
        {
            new ContinuityEventData(1, "A", 200601, 200612, new[] { row }),
            new ContinuityEventData(2, "B", 200606, 200703, new[] { row }),
            new ContinuityEventData(3, "C", 200801, 200802, new[] { row }),
            new ContinuityEventData(4, "Undated", null, null, new[] { row }),
        };
        var data = ContinuityMapTests.Sample() with { Events = events, Relations = Array.Empty<ContinuityRelationData>() };
        var atlas = ContinuityAtlasBuilder.Build(data, null, _ => false);

        Assert.Equal(2, atlas.RowCount);
        Assert.Equal(0, atlas.Bars.Single(b => b.Name == "A").Row);
        Assert.Equal(1, atlas.Bars.Single(b => b.Name == "B").Row);
        Assert.Equal(0, atlas.Bars.Single(b => b.Name == "C").Row);               // fits back in row 0 after the two-month gap
        Assert.Equal("Undated", Assert.Single(atlas.Undated).Name);

        var hidden = ContinuityAtlasBuilder.Build(data, new HashSet<int> { 2 }, _ => false);
        Assert.DoesNotContain(hidden.Bars, b => b.Name == "B");
    }

    [Fact]
    public void ThicknessFollowsIssueCountTerciles()
    {
        EventMapRow[] Members(int n) => Enumerable.Range(0, n).Select(i => ContinuityMapTests.Row(100 + i, 1, $"{i}", 2000)).ToArray();
        var events = new[] { 2, 5, 9, 20, 40, 80 }.Select((n, i) => new ContinuityEventData(i + 1, $"E{i}", 199001 + i * 1000, 199002 + i * 1000, Members(n))).ToArray();
        var data = ContinuityMapTests.Sample() with { Events = events, Relations = Array.Empty<ContinuityRelationData>() };
        var atlas = ContinuityAtlasBuilder.Build(data, null, _ => false);
        Assert.Equal(new[] { 6.0, 6, 10, 10, 14, 14 }, atlas.Bars.OrderBy(b => b.IssueCount).Select(b => b.Thickness));
    }

    // ===================== Atlas: control =====================

    [Fact]
    public void AtlasKeyboardStepsChronologicallyAndEnterOpensTheEvent()
    {
        Theming.FoundationStyleTests.WithFoundation("default", () =>
        {
            var atlas = ContinuityAtlasBuilder.Build(ContinuityMapTests.Sample(), null, _ => false);
            var canvas = new ContinuityAtlasCanvas { Atlas = atlas };
            var window = new Window { Width = 800, Height = 300, Content = canvas };
            window.Show();
            window.UpdateLayout();
            TestDispatcher.Drain();
            using (var bitmap = new RenderTargetBitmap(new PixelSize(800, 300)))
            {
                bitmap.Render(window);
            }

            var opened = new List<int>();
            canvas.OpenEvent += opened.Add;
            canvas.Focus();
            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            Assert.Equal(1, canvas.FocusedBar);
            canvas.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(atlas.Bars[1].EventId, opened.Single());

            var peer = ControlAutomationPeer.CreatePeerForElement(canvas);
            var bars = peer.GetChildren();
            Assert.Equal(2, bars.Count);
            Assert.StartsWith("Planet Hulk · 2006", bars[0].GetName());
            bars[0].GetProvider<IInvokeProvider>()!.Invoke();
            Assert.Equal(atlas.Bars[0].EventId, opened.Last());
            window.Close();
        });
    }

    [Fact]
    public void AtlasYearLabelsThinOutAsTheAxisCompresses()
    {
        Assert.Equal(1, ContinuityAtlasCanvas.YearStep(6));
        Assert.Equal(2, ContinuityAtlasCanvas.YearStep(3));
        Assert.Equal(5, ContinuityAtlasCanvas.YearStep(1));
    }

    private static EventMapViewModel Create(EventMapSource source, List<int>? opened = null) => new(
        load: _ => source,
        saveSpine: (_, _) => { },
        setRead: (_, read) => read ? EventMapReadState.Read : EventMapReadState.Unread,
        openReader: (issueId, _) => opened?.Add(issueId),
        activity: new Paperbunkr.App.Services.ActivityService(dispatch: a => a(), recordRun: _ => { }),
        runInBackground: work => Task.FromResult(work()));
}
