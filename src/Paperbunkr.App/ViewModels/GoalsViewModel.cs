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
            // A finish goal on an empty / deleted scope has nothing to measure against: show 0, never a full ring.
            double percent = p.EffectiveTarget <= 0 ? (p.ScopeMissing || p.Goal.Kind == GoalKind.Finish ? 0 : 100)
                : Math.Clamp(p.CurrentValue / (double)p.EffectiveTarget * 100, 0, 100);
            nextPercents[p.Goal.Id] = percent;
            if (p.Outcome == GoalOutcome.Missed)
            {
                RaiseMissed(p);
            }
            else
            {
                CheckMilestone(p.Goal.Id, percent);
            }

            Cards.Add(new GoalCardViewModel(p.Goal.Id, p.Goal.Title, StatusText(p), percent, p.IsComplete,
                p.CurrentValue, p.EffectiveTarget, p.Goal.PeriodEnd, p.Outcome, p.PaceState == GoalPaceState.Behind, p.CompletedOn));
        }

        _previousPercents = nextPercents;
        OnPropertyChanged(nameof(HasGoals));
        OnPropertyChanged(nameof(HeroGoal));
        OnPropertyChanged(nameof(ActiveCards));
        OnPropertyChanged(nameof(SecondaryCards));
        OnPropertyChanged(nameof(HasSecondaryGoals));
        OnPropertyChanged(nameof(PastCards));
        OnPropertyChanged(nameof(HasPastGoals));
        OnPropertyChanged(nameof(PastGoalsExpandedByDefault));
        if (!_pastToggledByUser)
        {
            PastGoalsOpen = PastGoalsExpandedByDefault;
        }
    }

    private bool _pastToggledByUser;

    /// <summary>Whether the Past goals group is showing its cards. Follows <see cref="PastGoalsExpandedByDefault"/> until the user toggles it once.</summary>
    [ObservableProperty]
    private bool _pastGoalsOpen;

    [RelayCommand]
    private void TogglePastGoals()
    {
        _pastToggledByUser = true;
        PastGoalsOpen = !PastGoalsOpen;
    }

    /// <summary>Set by the shell: opens the goal editor pre-filled from the goal with this id (the "Renew" button on a past goal).</summary>
    public Action<int>? RenewRequested { get; set; }

    /// <summary>Goals still in play, nearest deadline first.</summary>
    public IReadOnlyList<GoalCardViewModel> ActiveCards
        => Cards.Where(c => c.Outcome == GoalOutcome.Active).OrderBy(c => c.PeriodEnd).ToList();

    /// <summary>The Today tab's hero-row goal pick (docs/superpowers/specs/2026-09-23-insights-redesign-
    /// design.md's Today tab section) - the active goal nearest its own deadline, since that's the one most
    /// worth surfacing at a glance. With nothing active it falls back to the most recently ended goal, so the
    /// tile still shows how the last one went (docs/superpowers/specs/2026-10-04-insights-goal-outcomes-and-chart-colour-design.md).</summary>
    public GoalCardViewModel? HeroGoal
        => ActiveCards.FirstOrDefault() ?? Cards.OrderByDescending(c => c.PeriodEnd).FirstOrDefault();

    /// <summary>Every active goal except <see cref="HeroGoal"/> - kept reachable (with the same delete
    /// affordance) below the hero row rather than only through the hero tile, so having more than one
    /// active goal never hides the rest.</summary>
    public IReadOnlyList<GoalCardViewModel> SecondaryCards => ActiveCards.Where(c => c.GoalId != HeroGoal?.GoalId).ToList();

    public bool HasSecondaryGoals => SecondaryCards.Count > 0;

    /// <summary>Completed and Missed goals, newest first, other than a goal the hero tile is already showing.</summary>
    public IReadOnlyList<GoalCardViewModel> PastCards
        => Cards.Where(c => c.Outcome != GoalOutcome.Active && c.GoalId != HeroGoal?.GoalId)
            .OrderByDescending(c => c.PeriodEnd).ToList();

    public bool HasPastGoals => PastCards.Count > 0;

    /// <summary>The Past goals group opens by itself when there is nothing active to look at instead.</summary>
    public bool PastGoalsExpandedByDefault => ActiveCards.Count == 0;

    private static string StatusText(GoalProgress p)
    {
        string counts = $"{p.CurrentValue:N0} of {p.EffectiveTarget:N0}";
        if (p.ScopeMissing)
        {
            return $"{p.MissingScopeLabel} was deleted · {counts}";
        }

        return p.Outcome switch
        {
            GoalOutcome.Completed => p.CompletedOn is { } on ? $"Completed {on:MMM d} · {counts}" : $"Completed · {counts}",
            GoalOutcome.Missed => $"Missed · {counts}",
            _ when p.Goal.Kind == GoalKind.Finish => p.EffectiveTarget <= 0
                ? "Nothing in this scope yet"
                : $"{counts} · {p.EffectiveTarget - p.CurrentValue:N0} to go",
            _ => p.PaceState == GoalPaceState.Behind
                ? $"{counts} · {p.BehindAmount:N0} behind pace"
                : $"{counts} · on track",
        };
    }

    /// <summary>One "Goal missed" alert per goal, once its period has ended below target (deduped, so reopening Insights never repeats it).</summary>
    private void RaiseMissed(GoalProgress p)
        => Activity?.RaiseAlert(new ActivityAlert
        {
            Severity = ActivityAlertSeverity.Warning,
            Title = "Goal missed",
            Detail = $"{p.Goal.Title} — {p.CurrentValue:N0} of {p.EffectiveTarget:N0}",
            DedupeKey = $"goal-missed:{p.Goal.Id}",
        });

    [RelayCommand]
    private void RenewGoal(GoalCardViewModel? card)
    {
        if (card is not null)
        {
            RenewRequested?.Invoke(card.GoalId);
        }
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
    long CurrentValue, long Target, DateOnly PeriodEnd, GoalOutcome Outcome = GoalOutcome.Active, bool IsBehind = false,
    DateOnly? CompletedOn = null);
