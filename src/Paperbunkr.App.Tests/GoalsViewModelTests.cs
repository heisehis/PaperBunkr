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
