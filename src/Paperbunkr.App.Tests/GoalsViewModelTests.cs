using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Tests;

/// <summary>
/// Exercises <see cref="GoalsViewModel"/> - live milestone alerts and delete
/// (docs/superpowers/specs/2026-09-23-insights-reading-goals-design.md). Progress computation itself is
/// covered by <c>GoalResolverTests</c> (Paperbunkr.Data.Tests). Same fixture shape as
/// <see cref="RecapViewModelTests"/>.
/// </summary>
public class GoalsViewModelTests : IDisposable
{
    private readonly string? _originalOverride;
    private readonly string _dbPath;
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.ToLocalTime().Date);

    public GoalsViewModelTests()
    {
        _originalOverride = PaperbunkrDbContext.DatabasePathOverride;
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_goals_vm_test_{Guid.NewGuid():N}.db");
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

    private static GoalsViewModel NewVm(IDialogService? dialogs = null) => new(dialogs ?? new FakeDialogService(true), nowUtc: () => Now);

    private static int SeedGoal(GoalMetric metric, long target)
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var g = new ReadingGoal
        {
            Title = "Test goal",
            Metric = metric,
            Target = target,
            PeriodKind = GoalPeriodKind.ThisYear,
            PeriodStart = Today.AddDays(-30),
            PeriodEnd = Today.AddDays(30),
            ScopeKind = GoalScopeKind.Library,
            CreatedUtc = Now,
        };
        ctx.ReadingGoals.Add(g);
        ctx.SaveChanges();
        return g.Id;
    }

    private static void SeedFinishedEvents(int count)
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var series = new Series { Name = "Saga" };
        ctx.Series.Add(series);
        ctx.SaveChanges();
        for (int i = 0; i < count; i++)
        {
            var issue = new Issue { SeriesId = series.Id };
            ctx.Issues.Add(issue);
            ctx.SaveChanges();
            ctx.ReadingEvents.Add(new ReadingEvent { ItemType = ReadingItemType.Comic, ItemId = issue.Id, Kind = ReadingEventKind.Finished, TimestampUtc = Now });
        }

        ctx.SaveChanges();
    }

    [Fact]
    public void CrossingFiftyPercent_RaisesExactlyOneAlert()
    {
        SeedGoal(GoalMetric.Items, target: 10);
        SeedFinishedEvents(5); // exactly 50%
        var activity = new FakeActivityService();
        var vm = NewVm();
        vm.Activity = activity;

        vm.Refresh();
        Assert.Single(activity.Alerts.Where(a => a.DedupeKey.StartsWith("goal-50:")));

        vm.Refresh(); // still at 50%, shouldn't re-raise
        Assert.Single(activity.Alerts.Where(a => a.DedupeKey.StartsWith("goal-50:")));
    }

    [Fact]
    public void ReachingOneHundredPercent_RaisesCompleteAlert()
    {
        SeedGoal(GoalMetric.Items, target: 5);
        SeedFinishedEvents(5);
        var activity = new FakeActivityService();
        var vm = NewVm();
        vm.Activity = activity;

        vm.Refresh();

        Assert.Single(activity.Alerts.Where(a => a.DedupeKey.StartsWith("goal-complete:")));
        Assert.True(vm.Cards.Single().IsComplete);
    }

    [Fact]
    public async Task DeleteGoal_WhenCancelled_KeepsTheCard()
    {
        SeedGoal(GoalMetric.Items, target: 10);
        var vm = NewVm(new FakeDialogService(confirm: false));
        vm.Refresh();
        var card = vm.Cards.Single();

        await vm.DeleteGoalCommand.ExecuteAsync(card);

        vm.Refresh();
        Assert.Single(vm.Cards);
    }

    [Fact]
    public async Task DeleteGoal_WhenConfirmed_RemovesIt()
    {
        SeedGoal(GoalMetric.Items, target: 10);
        var vm = NewVm(new FakeDialogService(confirm: true));
        vm.Refresh();
        var card = vm.Cards.Single();

        await vm.DeleteGoalCommand.ExecuteAsync(card);

        Assert.Empty(vm.Cards);
        vm.Refresh();
        Assert.Empty(vm.Cards);
    }

    private static int SeedPeriodGoal(long target, DateOnly start, DateOnly end, string title = "Period goal",
        GoalPeriodKind kind = GoalPeriodKind.Custom, GoalMetric metric = GoalMetric.Items,
        GoalScopeKind scopeKind = GoalScopeKind.Library, string? scopeValue = null)
    {
        using var ctx = PaperbunkrDb.CreateContext();
        var g = new ReadingGoal
        {
            Title = title, Metric = metric, Target = target, PeriodKind = kind, PeriodStart = start, PeriodEnd = end,
            ScopeKind = scopeKind, ScopeValue = scopeValue, CreatedUtc = Now,
        };
        ctx.ReadingGoals.Add(g);
        ctx.SaveChanges();
        return g.Id;
    }

    [Fact]
    public void MissedGoal_RaisesOneWarningAlert_AndNeverTheHalfwayOne()
    {
        int id = SeedPeriodGoal(10, Today.AddDays(-60), Today.AddDays(-1));
        SeedFinishedEvents(0);
        var activity = new FakeActivityService();
        var vm = NewVm();
        vm.Activity = activity;

        vm.Refresh();
        vm.Refresh(); // a second look must not repeat it

        var alert = Assert.Single(activity.Alerts);
        Assert.Equal($"goal-missed:{id}", alert.DedupeKey);
        Assert.Equal(ActivityAlertSeverity.Warning, alert.Severity);
        Assert.Equal(Paperbunkr.Data.Metadata.GoalOutcome.Missed, vm.Cards.Single().Outcome);
        Assert.StartsWith("Missed · ", vm.Cards.Single().StatusText);
    }

    [Fact]
    public void HeroGoal_PrefersAnActiveGoal_AndPastGoalsGoToTheirOwnGroup()
    {
        int missed = SeedPeriodGoal(10, Today.AddDays(-60), Today.AddDays(-1), "Old miss");
        int active = SeedPeriodGoal(10, Today.AddDays(-5), Today.AddDays(60), "Current");
        var vm = NewVm();

        vm.Refresh();

        Assert.Equal(active, vm.HeroGoal!.GoalId);
        Assert.Empty(vm.SecondaryCards);
        Assert.Equal(missed, vm.PastCards.Single().GoalId);
        Assert.True(vm.HasPastGoals);
        Assert.False(vm.PastGoalsOpen, "collapsed while something is active");
    }

    [Fact]
    public void HeroGoal_FallsBackToTheMostRecentPastGoal_WhenNothingIsActive_AndPastGroupOpens()
    {
        int older = SeedPeriodGoal(10, Today.AddDays(-120), Today.AddDays(-90), "Older");
        int newer = SeedPeriodGoal(10, Today.AddDays(-60), Today.AddDays(-1), "Newer");
        var vm = NewVm();

        vm.Refresh();

        Assert.Equal(newer, vm.HeroGoal!.GoalId);
        Assert.Equal(older, vm.PastCards.Single().GoalId); // the hero is not repeated in the group
        Assert.True(vm.PastGoalsOpen, "opens by itself when there is nothing active");
    }

    [Fact]
    public void TogglingPastGoals_StaysPut_AcrossRefreshes()
    {
        SeedPeriodGoal(10, Today.AddDays(-60), Today.AddDays(-1));
        SeedPeriodGoal(10, Today.AddDays(-5), Today.AddDays(60));
        var vm = NewVm();
        vm.Refresh();
        Assert.False(vm.PastGoalsOpen);

        vm.TogglePastGoalsCommand.Execute(null);
        vm.Refresh();

        Assert.True(vm.PastGoalsOpen);
    }

    [Fact]
    public void CompletedGoal_StatusNamesTheDayItWasCompleted()
    {
        SeedGoal(GoalMetric.Items, target: 1);
        SeedFinishedEvents(1);
        var vm = NewVm();

        vm.Refresh();

        var card = vm.Cards.Single();
        Assert.Equal(Paperbunkr.Data.Metadata.GoalOutcome.Completed, card.Outcome);
        Assert.Equal($"Completed {Today:MMM d} · 1 of 1", card.StatusText);
        Assert.True(card.IsComplete);
    }

    [Fact]
    public void BehindPaceActiveGoal_IsFlaggedBehind()
    {
        SeedPeriodGoal(100, Today.AddDays(-50), Today.AddDays(50), kind: GoalPeriodKind.ThisYear);
        var vm = NewVm();

        vm.Refresh();

        Assert.True(vm.Cards.Single().IsBehind);
    }

    [Fact]
    public void RenewGoal_AsksTheShellToOpenTheEditorForThatGoal()
    {
        int id = SeedPeriodGoal(10, Today.AddDays(-60), Today.AddDays(-1));
        var vm = NewVm();
        int? requested = null;
        vm.RenewRequested = goalId => requested = goalId;
        vm.Refresh();

        vm.RenewGoalCommand.Execute(vm.Cards.Single());

        Assert.Equal(id, requested);
    }

    [Fact]
    public void GoalEditor_LoadFrom_CopiesMetricTargetScopeAndPeriodKind_ButResolvesANewPeriod()
    {
        int id = SeedPeriodGoal(3000, Today.AddDays(-40), Today.AddDays(-10), kind: GoalPeriodKind.Custom,
            metric: GoalMetric.Pages, scopeKind: GoalScopeKind.Publisher, scopeValue: "Image");
        var editor = new GoalEditorViewModel(() => { }, () => { });

        Assert.True(editor.LoadFrom(id));

        Assert.Equal(GoalMetric.Pages, editor.Metric);
        Assert.Equal("3000", editor.TargetText);
        Assert.Equal(GoalPeriodKind.Custom, editor.PeriodKind);
        var row = Assert.Single(editor.ScopeRows); // the goal's legacy single scope loads as one row
        Assert.Equal(GoalScopeKind.Publisher, row.Kind);
        Assert.Equal("Image", row.ValueText);
        // The custom range keeps its 30-day length but starts today, not in the past.
        Assert.Equal(DateTime.Today, editor.CustomStart);
        Assert.Equal(DateTime.Today.AddDays(30), editor.CustomEnd);
        Assert.True(editor.CanCreate);
    }

    [Fact]
    public void GoalEditor_LoadFrom_ReturnsFalse_ForAGoalThatNoLongerExists()
    {
        var editor = new GoalEditorViewModel(() => { }, () => { });
        Assert.False(editor.LoadFrom(9999));
    }

    private sealed class FakeDialogService : IDialogService
    {
        private readonly bool _confirm;

        public FakeDialogService(bool confirm) => _confirm = confirm;

        public Task<int> ShowAsync(ConfirmDialogRequest request) => Task.FromResult(_confirm ? 0 : 1);

        public Task<bool> ConfirmAsync(string message, string? title = null, string confirmLabel = "Confirm",
            string cancelLabel = "Cancel", bool isDestructive = false) => Task.FromResult(_confirm);
    }

    private sealed class FakeActivityService : IActivityService
    {
        private readonly List<ActivityAlert> _alerts = new();
        public System.Collections.ObjectModel.ReadOnlyObservableCollection<ActivityJob> ActiveJobs { get; } = new(new());
        public System.Collections.ObjectModel.ReadOnlyObservableCollection<ActivityJob> RecentJobs { get; } = new(new());
        public System.Collections.ObjectModel.ReadOnlyObservableCollection<ActivityAlert> Alerts { get; }
        public bool PanelIsOpen { get; set; }
        public event EventHandler? Changed;
        public event Action<ToastRequest>? CompletionToastRequested;

        public FakeActivityService()
        {
            var backing = new System.Collections.ObjectModel.ObservableCollection<ActivityAlert>(_alerts);
            Alerts = new System.Collections.ObjectModel.ReadOnlyObservableCollection<ActivityAlert>(backing);
            _backing = backing;
        }

        private readonly System.Collections.ObjectModel.ObservableCollection<ActivityAlert> _backing;

        public IActivityJobHandle StartJob(ActivityJobKind kind, string title, bool cancellable = true,
            ActivityTrigger trigger = ActivityTrigger.Manual, ActivityToastPolicy toastPolicy = ActivityToastPolicy.Always,
            bool startQueued = false) => throw new NotSupportedException();

        public IActivityUpkeepHandle RegisterUpkeep(string title) => throw new NotSupportedException();

        public void CancelJob(Guid jobId) { }

        public void RaiseAlert(ActivityAlert alert)
        {
            var existing = _backing.FirstOrDefault(a => a.DedupeKey == alert.DedupeKey);
            if (existing is not null)
            {
                return;
            }

            _backing.Insert(0, alert);
        }

        public void DismissAlert(Guid alertId)
        {
            var existing = _backing.FirstOrDefault(a => a.Id == alertId);
            if (existing is not null)
            {
                _backing.Remove(existing);
            }
        }

        public void DismissAllAlerts() => _backing.Clear();

        public void ClearFinished() { }

        public void StopAll() { }
    }
}
