using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using static Paperbunkr.App.Tests.EventMap.EventMapFixtures;

namespace Paperbunkr.App.Tests.EventMap;

/// <summary>Continuity map M4 (docs/superpowers/specs/2026-11-11-continuity-map-completeness-design.md) on the app side.</summary>
[Collection(nameof(AvaloniaTestCollection))]
public sealed class EventMissingTests
{
    private const int EventId = 1;

    private static EventArcEntry Entry(string series, string number, string? issueId = "500") =>
        new(series, number, 2015, "2015-06-01", null, null, null, null, null, issueId, issueId is null ? null : "77");

    private static EventGhost Ghost(string series, string number, int? after, int? lane, EventGhostKind kind = EventGhostKind.Missing, int? owned = null, string? issueId = "500") =>
        new(kind, Entry(series, number, issueId), EventArcSnapshots.Metron, DateTime.UtcNow.AddDays(-3), owned, after, lane);

    // ===================== Placement and gaps (pure) =====================

    [Fact]
    public void GhostsAreInsertedAfterTheirAnchorWithNegativeIdsAndLanes()
    {
        var rows = new[] { Row(SeriesA, "1", 1), Row(SeriesB, "1", 2) };
        var ghosts = new[]
        {
            Ghost("Weirdworld", "1", after: null, lane: null),
            Ghost("Adventure", "2", after: rows[0].IssueId, lane: SeriesA),
            Ghost("Weirdworld", "2", after: rows[1].IssueId, lane: null),
        };
        int next = 0;
        var placed = EventGhostPlacement.Inject(rows, ghosts, ref next, new Dictionary<string, int>());

        Assert.Equal(new[] { "Weirdworld 1", "Adventure 1", "Adventure 2", "Brave 1", "Weirdworld 2" },
            placed.Select(r => $"{r.SeriesName} {r.Number}"));
        Assert.Equal(Enumerable.Range(1, 5), placed.Select(r => r.Position));
        Assert.All(placed.Where(r => r.IsGhost), r => Assert.True(r.IssueId < 0 && r.MembershipId < 0));
        Assert.Equal(SeriesA, placed[2].SeriesId);
        Assert.True(placed[0].SeriesId < 0);
        Assert.Equal(placed[0].SeriesId, placed[4].SeriesId);    // one lane for the series with nothing on the map
        Assert.Equal(2015, placed[0].Year);
        Assert.Equal(6, placed[0].Month);
    }

    [Fact]
    public void GapCheckListsOnlyInteriorNumbersTheLibraryDoesNotOwn()
    {
        var rows = new[] { Row(SeriesA, "92", 1), Row(SeriesA, "96", 2), Row(SeriesA, "Annual 1", 3), Row(SeriesB, "5", 4) };
        var owned = new Dictionary<int, IReadOnlyList<string>> { [SeriesA] = new[] { "92", "94", "96" } };
        var gaps = EventGapCheck.Find(rows, owned);
        Assert.Equal(new[] { "93", "95" }, gaps.Select(g => g.Number));
        Assert.All(gaps, g => Assert.Equal(SeriesA, g.SeriesId));
    }

    // ===================== View model =====================

    private sealed class Harness
    {
        public List<string> Confirmations { get; } = new();
        public bool ConfirmAnswer = true;
        public List<IReadOnlyList<EventGhost>> Requested { get; } = new();
        public List<WantedRequestResult> Undone { get; } = new();
        public List<(int Event, int Issue)> AddedToEvent { get; } = new();
        public IReadOnlyList<string> ToTrack = new[] { "Weirdworld" };
        public List<IReadOnlyList<int>> Checked { get; } = new();
        public ActivityService Activity { get; } = new(dispatch: a => a(), recordRun: _ => { });

        public EventMapViewModel Create(EventMapSource source)
        {
            var vm = new EventMapViewModel(load: _ => source, saveSpine: (_, _) => { },
                setRead: (_, read) => read ? EventMapReadState.Read : EventMapReadState.Unread, openReader: (_, _) => { },
                activity: Activity, runInBackground: work => Task.FromResult(work()))
            {
                Dialogs = new FakeDialogs(this),
                SeriesToTrackRunner = _ => ToTrack,
                RequestWantedRunner = ghosts =>
                {
                    Requested.Add(ghosts);
                    return new WantedRequestResult(ghosts.Count, Array.Empty<string>(), new[] { 1 }, new[] { 2 });
                },
                UndoWantedRunner = Undone.Add,
                AddToEventRunner = (e, i) => { AddedToEvent.Add((e, i)); return true; },
                CheckMissingRunner = ids => { Checked.Add(ids); return Task.FromResult(new MissingCheckResult(ids.Count, ids.Count, 0, Array.Empty<string>())); },
            };
            return vm;
        }

        private sealed class FakeDialogs(Harness harness) : IDialogService
        {
            public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(harness.ConfirmAnswer ? 0 : -1);

            public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm", string cancelLabel = "Cancel", bool isDestructive = false)
            {
                harness.Confirmations.Add(message);
                return Task.FromResult(harness.ConfirmAnswer);
            }
        }
    }

    private static EventMapSource WithGhosts(bool hasIdentity = true)
    {
        var rows = new[] { Row(SeriesA, "1", 1, read: EventMapReadState.Read), Row(SeriesB, "1", 2) };
        var ghosts = new[]
        {
            Ghost("Adventure", "2", after: rows[0].IssueId, lane: SeriesA),
            Ghost("Brave", "2", after: rows[1].IssueId, lane: SeriesB, EventGhostKind.OwnedNotMember, owned: 9001),
        };
        return Source("No match", rows) with
        {
            Missing = new EventMissingData(ghosts, DateTime.UtcNow.AddDays(-3), hasIdentity, new Dictionary<int, IReadOnlyList<string>>()),
        };
    }

    [Fact]
    public async Task ShowMissingPlacesGhostsThatNeverCountAsReadOrNext()
    {
        var vm = new Harness().Create(WithGhosts());
        await vm.LoadAsync(EventId);
        Assert.True(vm.HasMissingSnapshot);
        Assert.DoesNotContain(vm.Cards, c => c.IsGhost);
        Assert.Equal(1, vm.MissingCount);                         // known before turning it on

        vm.ShowMissing = true;
        Assert.Equal(4, vm.Cards.Count);
        Assert.Equal(1, vm.Cards.Count(c => c.IsMissingGhost));
        Assert.Equal(1, vm.Cards.Count(c => c.IsOwnedGhost));
        Assert.Equal("1 / 2 read · 1 missing", vm.ProgressText);
        Assert.False(vm.Cards[vm.Layout.FirstUnread()!.Value].IsGhost);
        Assert.Equal(1, vm.Matrix.Rows.Single(r => r.SeriesId == SeriesA).Cells[^1].Missing);
        Assert.Equal(1, vm.Matrix.Rows.Single(r => r.SeriesId == SeriesA).Cells[^1].Count);
        Assert.Equal("1 missing", vm.RollCallRows.Single(r => r.SeriesId == SeriesA).MissingLabel);
        Assert.Contains(", not in your library", vm.Cards.First(c => c.IsMissingGhost).AutomationName);
    }

    [Fact]
    public async Task GhostsCannotBeReadOrMarkedRead()
    {
        var opened = new List<int>();
        var source = WithGhosts();
        var vm = new EventMapViewModel(load: _ => source, setRead: (_, _) => throw new InvalidOperationException("must not write"),
            openReader: (issue, _) => opened.Add(issue), activity: new ActivityService(dispatch: a => a(), recordRun: _ => { }),
            runInBackground: work => Task.FromResult(work()));
        await vm.LoadAsync(EventId);
        vm.ShowMissing = true;
        vm.Select(vm.Cards.First(c => c.IsMissingGhost).Index);
        vm.OpenReaderCommand.Execute(null);
        vm.ToggleReadCommand.Execute(null);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task AddToWantedConfirmsNewSeriesRequestsAndCanBeUndone()
    {
        var harness = new Harness();
        var vm = harness.Create(WithGhosts());
        await vm.LoadAsync(EventId);
        vm.ShowMissing = true;
        var ghost = vm.Cards.First(c => c.IsMissingGhost);
        vm.Select(ghost.Index);

        await vm.AddSelectedToWantedCommand.ExecuteAsync(null);
        Assert.Contains("Weirdworld", Assert.Single(harness.Confirmations));
        Assert.Single(Assert.Single(harness.Requested));
        Assert.True(ghost.IsWanted);
        Assert.True(vm.HasUndoWanted);

        await vm.UndoWantedCommand.ExecuteAsync(null);
        Assert.Single(harness.Undone);
        Assert.False(ghost.IsWanted);
        Assert.False(vm.HasUndoWanted);
    }

    [Fact]
    public async Task DecliningTheConfirmationRequestsNothing()
    {
        var harness = new Harness { ConfirmAnswer = false };
        var vm = harness.Create(WithGhosts());
        await vm.LoadAsync(EventId);
        vm.ShowMissing = true;
        await vm.AddAllMissingToWantedCommand.ExecuteAsync(null);
        Assert.Single(harness.Confirmations);
        Assert.Empty(harness.Requested);
    }

    [Fact]
    public async Task AddToEventWritesOneMembershipOnlyWhenAsked()
    {
        var harness = new Harness();
        var vm = harness.Create(WithGhosts());
        await vm.LoadAsync(EventId);
        vm.ShowMissing = true;
        Assert.Empty(harness.AddedToEvent);
        vm.Select(vm.Cards.First(c => c.IsOwnedGhost).Index);
        await vm.AddSelectedToEventCommand.ExecuteAsync(null);
        Assert.Equal((EventId, 9001), Assert.Single(harness.AddedToEvent));
    }

    [Fact]
    public async Task CheckForMissingIssuesNeedsAnArcIdentity()
    {
        var harness = new Harness();
        var vm = harness.Create(WithGhosts(hasIdentity: false));
        await vm.LoadAsync(EventId);
        Assert.False(vm.CanCheckMissing);
        await vm.CheckForMissingIssuesCommand.ExecuteAsync(null);
        Assert.Empty(harness.Checked);
        Assert.Contains(harness.Activity.Alerts, a => a.Title.Contains("Metron or ComicVine arc", StringComparison.Ordinal));

        var linked = harness.Create(WithGhosts(hasIdentity: true));
        await linked.LoadAsync(EventId);
        await linked.CheckForMissingIssuesCommand.ExecuteAsync(null);
        Assert.Equal(new[] { EventId }, Assert.Single(harness.Checked));
        Assert.True(linked.ShowMissing);
    }

    [Fact]
    public async Task WithoutAProviderListTheGapCheckRuns()
    {
        var rows = new[] { Row(SeriesA, "92", 1), Row(SeriesA, "95", 2) };
        var source = Source("No match", rows) with
        {
            Missing = new EventMissingData(Array.Empty<EventGhost>(), null, false,
                new Dictionary<int, IReadOnlyList<string>> { [SeriesA] = new[] { "92", "93", "95" } }),
        };
        var vm = new Harness().Create(source);
        await vm.LoadAsync(EventId);
        Assert.True(vm.HasGaps);
        Assert.Equal(new[] { "Adventure #94" }, vm.Gaps.Select(g => g.Label));
        Assert.False(vm.HasMissingSnapshot);
    }
}
