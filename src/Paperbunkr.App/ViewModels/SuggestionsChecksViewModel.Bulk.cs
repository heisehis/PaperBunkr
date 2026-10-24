using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

public partial class SuggestionsChecksViewModel
{
    /// <summary>Test seam for deterministic verification failures, without live provider requests.</summary>
    internal Func<PaperbunkrDbContext, StoryEventCandidate, Task<StoryEventCandidate>>? VerifyStoryEventCandidate { get; set; }

    [ObservableProperty]
    private bool _isStoryEventBatchRunning;

    [ObservableProperty]
    private int _storyEventBatchCompleted;

    [ObservableProperty]
    private int _storyEventBatchTotal;

    [ObservableProperty]
    private string? _storyEventBatchStatus;

    public int SelectedStoryEventCount => NewStoryEventCandidates.Count(row => row.IsSelected);
    public string StoryEventSelectionLabel => $"{SelectedStoryEventCount} selected";
    public bool CanChangeStoryEvents => !IsStoryEventBatchRunning && !IsFindingDuplicates
        && !NewStoryEventCandidates.Any(row => row.IsAccepting);
    public bool CanProcessSelectedStoryEvents => CanChangeStoryEvents && SelectedStoryEventCount > 0;

    partial void OnIsStoryEventBatchRunningChanged(bool value) => RaiseStoryEventSelectionState();
    partial void OnIsFindingDuplicatesChanged(bool value) => RaiseStoryEventSelectionState();

    private void RaiseStoryEventSelectionState()
    {
        OnPropertyChanged(nameof(SelectedStoryEventCount));
        OnPropertyChanged(nameof(StoryEventSelectionLabel));
        OnPropertyChanged(nameof(CanChangeStoryEvents));
        OnPropertyChanged(nameof(CanProcessSelectedStoryEvents));
        SelectAllStoryEventsCommand.NotifyCanExecuteChanged();
        ClearStoryEventSelectionCommand.NotifyCanExecuteChanged();
        AcceptSelectedStoryEventsCommand.NotifyCanExecuteChanged();
        DismissSelectedStoryEventsCommand.NotifyCanExecuteChanged();
        foreach (var row in NewStoryEventCandidates) row.IsQueueBusy = !CanChangeStoryEvents;
    }

    [RelayCommand(CanExecute = nameof(CanChangeStoryEvents))]
    private void SelectAllStoryEvents()
    {
        foreach (var row in NewStoryEventCandidates) row.IsSelected = true;
    }

    [RelayCommand(CanExecute = nameof(CanChangeStoryEvents))]
    private void ClearStoryEventSelection()
    {
        foreach (var row in NewStoryEventCandidates) row.IsSelected = false;
    }

    [RelayCommand(CanExecute = nameof(CanProcessSelectedStoryEvents))]
    private Task AcceptSelectedStoryEventsAsync() => ProcessSelectedStoryEventsAsync(accept: true);

    [RelayCommand(CanExecute = nameof(CanProcessSelectedStoryEvents))]
    private Task DismissSelectedStoryEventsAsync() => ProcessSelectedStoryEventsAsync(accept: false);

    private async Task ProcessSelectedStoryEventsAsync(bool accept)
    {
        if (!CanProcessSelectedStoryEvents) return;
        var rows = NewStoryEventCandidates.Where(row => row.IsSelected).ToList();
        // Invalidate an earlier asynchronous refresh before it can replace the snapshot's rows.
        _candidatesGeneration++;
        IsStoryEventBatchRunning = true;
        StoryEventBatchCompleted = 0;
        StoryEventBatchTotal = rows.Count;
        int succeeded = 0;
        int failed = 0;
        var created = new HashSet<int>();
        string? batchError = null;
        try
        {
            foreach (var row in rows)
            {
                row.BatchError = null;
                row.IsAccepting = true;
                StoryEventBatchStatus = $"{(accept ? "Accepting" : "Dismissing")} {StoryEventBatchCompleted + 1} of {rows.Count}: {row.ArcName}";
                try
                {
                    if (accept)
                    {
                        var result = await Task.Run(() => CreateStoryEventFromCandidateAsync(row.Candidate));
                        created.Add(result.Id);
                    }
                    else
                    {
                        await Task.Run(() =>
                        {
                            using var context = PaperbunkrDb.CreateContext();
                            StoryArcGroupingResolver.Dismiss(context, row.Candidate.ArcName, row.Candidate.Publisher);
                        });
                    }
                    succeeded++;
                    // Even cached verification can complete synchronously: detach only after the
                    // routed button event finishes. Await removal before releasing the batch lock.
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        NewStoryEventCandidates.Remove(row);
                        RaiseCounts();
                    }, DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    failed++;
                    row.BatchError = ex.Message;
                    row.IsSelected = true;
                }
                finally
                {
                    row.IsAccepting = false;
                    StoryEventBatchCompleted++;
                }
            }

            if (accept && created.Count > 0)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RefreshPossibleDuplicates();
                    _storyEventsChanged();
                }, DispatcherPriority.Background);
                _ = CheckEventIdentitiesInBackground(created);
            }
        }
        catch (Exception ex)
        {
            batchError = $" Could not finish refreshing results: {ex.Message}";
        }
        finally
        {
            StoryEventBatchStatus = $"{succeeded} {(accept ? "accepted" : "dismissed")}, {failed} failed."
                + (failed > 0 ? " Failed items remain selected for retry." : string.Empty) + batchError;
            IsStoryEventBatchRunning = false;
            RaiseCounts();
        }
    }
}
