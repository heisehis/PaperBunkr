using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Reading-goal cards on the Insights screen's Overview tab (docs/superpowers/specs/2026-09-23-insights-
/// reading-goals-design.md). Unlike <see cref="StatsScreenViewModel"/>/<see cref="RecapViewModel"/>, there's
/// no per-key session cache here - <see cref="GoalResolver.Build"/> is cheap (a handful of goals against the
/// existing <c>ReadingEvents</c> table) and needs recomputing on every refresh regardless, since it's also
/// where the live 50%/100% milestone check happens. Also unlike those two, this has no independent
/// <c>ReadingEventRecorded</c> subscription of its own or tab-gated <c>IsActive</c> - the goal cards live on
/// the Overview tab, which <see cref="InsightsScreenViewModel.Refresh"/> already refreshes unconditionally
/// (and only while the Insights screen itself is the visible screen), so a second subscription here would
/// just recompute the same thing twice per event.
/// </summary>
public partial class GoalsViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly Func<DateTime> _nowUtc;
    private Dictionary<int, double> _previousPercents = new();

    public GoalsViewModel(IDialogService dialogs, IReadingEventRecorder? readingEventRecorder = null, Func<DateTime>? nowUtc = null)
    {
        _dialogs = dialogs;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    /// <summary>Used to raise the live 50%/100% milestone alerts - null in tests that don't exercise them.</summary>
    public IActivityService? Activity { get; set; }

    public ObservableCollection<GoalCardViewModel> Cards { get; } = new();

    public bool HasGoals => Cards.Count > 0;

    public void Refresh()
    {
        using var context = PaperbunkrDb.CreateContext();
        var progress = GoalResolver.Build(context, _nowUtc());

        var nextPercents = new Dictionary<int, double>();
        Cards.Clear();
        foreach (var p in progress)
        {
            double percent = p.Goal.Target <= 0 ? 100 : Math.Clamp(p.CurrentValue / (double)p.Goal.Target * 100, 0, 100);
            nextPercents[p.Goal.Id] = percent;
            CheckMilestone(p.Goal.Id, percent);
            Cards.Add(new GoalCardViewModel(p.Goal.Id, p.Goal.Title, StatusText(p), percent, p.IsComplete,
                p.CurrentValue, p.Goal.Target, p.Goal.PeriodEnd));
        }

        _previousPercents = nextPercents;
        OnPropertyChanged(nameof(HasGoals));
        OnPropertyChanged(nameof(HeroGoal));
        OnPropertyChanged(nameof(SecondaryCards));
        OnPropertyChanged(nameof(HasSecondaryGoals));
    }

    /// <summary>The Today tab's hero-row goal pick (docs/superpowers/specs/2026-09-23-insights-redesign-
    /// design.md's Today tab section) - the goal nearest its own deadline, since that's the one most
    /// worth surfacing at a glance.</summary>
    public GoalCardViewModel? HeroGoal => Cards.OrderBy(c => c.PeriodEnd).FirstOrDefault();

    /// <summary>Every goal except <see cref="HeroGoal"/> - kept reachable (with the same delete
    /// affordance) below the hero row rather than only through the hero tile, so having more than one
    /// active goal never hides the rest.</summary>
    public IReadOnlyList<GoalCardViewModel> SecondaryCards => Cards.Where(c => c.GoalId != HeroGoal?.GoalId).ToList();

    public bool HasSecondaryGoals => SecondaryCards.Count > 0;

    private static string StatusText(GoalProgress p)
    {
        if (p.IsComplete)
        {
            return "Complete!";
        }

        bool ended = p.Goal.PeriodKind == GoalPeriodKind.Custom
            && p.Goal.PeriodEnd < DateOnly.FromDateTime(DateTime.Now);
        if (ended)
        {
            return $"Goal ended · {p.CurrentValue:N0} of {p.Goal.Target:N0}";
        }

        return p.PaceState == GoalPaceState.Behind
            ? $"{p.CurrentValue:N0} of {p.Goal.Target:N0} · {p.BehindAmount:N0} behind pace"
            : $"{p.CurrentValue:N0} of {p.Goal.Target:N0} · on track";
    }

    private void CheckMilestone(int goalId, double percent)
    {
        double previous = _previousPercents.GetValueOrDefault(goalId, 0);
        if (previous < 100 && percent >= 100)
        {
            Activity?.RaiseAlert(new ActivityAlert
            {
                Severity = ActivityAlertSeverity.Info,
                Title = "Goal reached!",
                DedupeKey = $"goal-complete:{goalId}",
            });
        }
        else if (previous < 50 && percent >= 50)
        {
            Activity?.RaiseAlert(new ActivityAlert
            {
                Severity = ActivityAlertSeverity.Info,
                Title = "Halfway to your reading goal",
                DedupeKey = $"goal-50:{goalId}",
            });
        }
    }

    [RelayCommand]
    private async Task DeleteGoal(GoalCardViewModel? card)
    {
        if (card is null)
        {
            return;
        }

        bool confirmed = await _dialogs.ConfirmAsync("Delete this goal? This can't be undone.", "Delete goal", isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var goal = context.ReadingGoals.Find(card.GoalId);
        if (goal is not null)
        {
            context.ReadingGoals.Remove(goal);
            context.SaveChanges();
        }

        Refresh();
    }
}

/// <summary>One reading-goal card's display state (docs/superpowers/specs/2026-09-23-insights-reading-
/// goals-design.md). <see cref="CurrentValue"/>/<see cref="Target"/> back the Today hero tile's mono
/// readout (docs/superpowers/specs/2026-09-23-insights-redesign-design.md); <see cref="PeriodEnd"/> is
/// what <see cref="GoalsViewModel.HeroGoal"/> sorts by.</summary>
public sealed record GoalCardViewModel(int GoalId, string Title, string StatusText, double Percent, bool IsComplete,
    long CurrentValue, long Target, DateOnly PeriodEnd);
