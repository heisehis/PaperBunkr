using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Continuity screen's Suggestions &amp; checks panel (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md): the
/// three review queues that used to sit in the sidebar - new story-event suggestions (docs/superpowers/specs/
/// 2026-09-17-storyevent-continuity-autopopulate-design.md Phase 1), possible duplicate events (docs/superpowers/specs/
/// 2026-09-27-story-event-resolver-design.md §4) and shared-universe suggestions (the same design, Phase 2) - plus the Check story events
/// button (the Check Wikidata button was removed 2026-10-12). Behaviour is ported unchanged; every row removal now waits one dispatcher tick, because the clicked
/// button lives in the row being removed (CLAUDE.md, routed-event detach rule).
/// </summary>
public partial class SuggestionsChecksViewModel : ViewModelBase
{
    private readonly Action<string, string> _notify;
    private readonly IActivityService _activity;
    private readonly Action<int> _openEvent;
    private readonly Action<int> _openContinuity;
    private readonly Action<StoryEventIdentitySweepSummary> _afterIdentityWork;
    private readonly Action _changed;
    private readonly Action _storyEventsChanged;
    private CancellationTokenSource? _metronScanCancellation;

    /// <param name="openEvent">Opens a story event (after accepting a suggestion).</param>
    /// <param name="openContinuity">Opens a continuity (after accepting a shared-universe suggestion).</param>
    /// <param name="afterIdentityWork">The shell's reaction to a sweep or merge: follow a merged-away open event, refresh the sidebar.</param>
    /// <param name="changed">Any count here changed (the sidebar badge).</param>
    public SuggestionsChecksViewModel(
        Action<string, string> notify,
        IActivityService activity,
        Action<int> openEvent,
        Action<int> openContinuity,
        Action<StoryEventIdentitySweepSummary> afterIdentityWork,
        Action changed,
        Action? storyEventsChanged = null)
    {
        _notify = notify;
        _activity = activity;
        _openEvent = openEvent;
        _openContinuity = openContinuity;
        _afterIdentityWork = afterIdentityWork;
        _changed = changed;
        _storyEventsChanged = storyEventsChanged ?? (() => { });
    }

    /// <summary>Test seam: how identity work leaves the UI thread. Null = a thread-pool task (tests pass a synchronous runner).</summary>
    internal Func<Func<Task>, Task>? IdentityRunner { get; set; }

    /// <summary>Test seam: the reading-order sources the smart connector asks (CBRO, Metron lists). Null = the live ones.</summary>
    internal Func<StoryEventChecks.EventOrderInputs>? OrderInputs { get; set; }

    /// <summary>Test seam: the provider sources the sweep uses. Null = whatever credentials are saved.</summary>
    internal Func<IReadOnlyDictionary<ComicProvider, IArcIdentitySource>>? IdentitySources { get; set; }

    public ObservableCollection<StoryEventCandidateRowViewModel> NewStoryEventCandidates { get; } = new();

    public ObservableCollection<StoryEventDuplicateRowViewModel> PossibleDuplicates { get; } = new();

    public ObservableCollection<ContinuitySuggestionRowViewModel> NewContinuitySuggestions { get; } = new();

    public bool HasNoNewStoryEventCandidates => NewStoryEventCandidates.Count == 0;

    public bool HasPossibleDuplicates => PossibleDuplicates.Count > 0;

    public bool HasNoNewContinuitySuggestions => NewContinuitySuggestions.Count == 0;

    public string PossibleDuplicatesSummary => PossibleDuplicates.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public int SelectedContinuitySuggestionCount => NewContinuitySuggestions.Count(row => row.IsSelected);

    public bool HasSelectedContinuitySuggestions => SelectedContinuitySuggestionCount > 0;

    /// <summary>Everything waiting here - the sidebar row's badge.</summary>
    public int TotalCount => NewStoryEventCandidates.Count + PossibleDuplicates.Count + NewContinuitySuggestions.Count;

    public bool IsEmpty => TotalCount == 0;

    [ObservableProperty]
    private bool _isFindingDuplicates;

    [ObservableProperty]
    private bool _isCheckingMetron;

    [ObservableProperty]
    private string? _metronCheckResult;

    [ObservableProperty]
    private string? _storyEventCheckResult;

    /// <summary>Set when the panel was opened at one pair ("Possible duplicate: X" on an event page), so the view can scroll to it.</summary>
    [ObservableProperty]
    private int? _focusedDuplicateEventId;

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(HasNoNewStoryEventCandidates));
        OnPropertyChanged(nameof(HasPossibleDuplicates));
        OnPropertyChanged(nameof(HasNoNewContinuitySuggestions));
        OnPropertyChanged(nameof(PossibleDuplicatesSummary));
        OnPropertyChanged(nameof(SelectedContinuitySuggestionCount));
        OnPropertyChanged(nameof(HasSelectedContinuitySuggestions));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(IsEmpty));
        RaiseStoryEventSelectionState();
        _changed();
    }

    // --- New story-event suggestions: local-only on load, verified once on Accept. ---

    // Bumped by every refresh, so a background read that was overtaken is dropped.
    private int _candidatesGeneration;

    /// <summary>
    /// Same as <see cref="RefreshStoryEventCandidates"/>, with both resolvers on a thread-pool thread (0.25-0.5 s of every
    /// visit to the Continuity screen that had to reload, 2026-10-11 measurement). The lists keep what they showed until the
    /// new ones arrive. Logs and falls back to the synchronous refresh on failure.
    /// </summary>
    public async Task RefreshStoryEventCandidatesAsync()
    {
        if (!CanChangeStoryEvents) return;
        int generation = ++_candidatesGeneration;
        try
        {
            var candidates = await Task.Run(() =>
            {
                using var context = PaperbunkrDb.CreateContext();
                return StoryArcGroupingResolver.GetCandidates(context);
            }).ConfigureAwait(true);

            if (generation != _candidatesGeneration || !CanChangeStoryEvents)
            {
                return;
            }

            NewStoryEventCandidates.Clear();
            foreach (var candidate in candidates)
            {
                NewStoryEventCandidates.Add(new StoryEventCandidateRowViewModel(candidate, AcceptStoryEventCandidateAsync, DismissStoryEventCandidate, RaiseCounts));
            }

            RaiseCounts();
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogMilestone($"Continuity: async suggestions refresh failed ({ex.GetType().Name}: {ex.Message}) - refreshing synchronously.");
            if (generation == _candidatesGeneration)
            {
                RefreshStoryEventCandidates();
            }
        }
    }

    public void RefreshStoryEventCandidates()
    {
        if (!CanChangeStoryEvents) return;
        _candidatesGeneration++;
        using var context = PaperbunkrDb.CreateContext();
        NewStoryEventCandidates.Clear();
        foreach (var candidate in StoryArcGroupingResolver.GetCandidates(context))
        {
            NewStoryEventCandidates.Add(new StoryEventCandidateRowViewModel(candidate, AcceptStoryEventCandidateAsync, DismissStoryEventCandidate, RaiseCounts));
        }

        RaiseCounts();
    }

    private async Task AcceptStoryEventCandidateAsync(StoryEventCandidateRowViewModel row)
    {
        var (storyEventId, message) = await CreateStoryEventFromCandidateAsync(row.Candidate);

        _notify("Story event created", message);
        Dispatcher.UIThread.Post(() =>
        {
            NewStoryEventCandidates.Remove(row);
            RefreshPossibleDuplicates();
            _openEvent(storyEventId);
        });

        // Fill the event's other provider id and merge it if it duplicates one already here - in the background, silent unless it merges.
        _ = CheckEventIdentityInBackground(storyEventId);
    }

    private async Task<(int Id, string Message)> CreateStoryEventFromCandidateAsync(StoryEventCandidate candidate)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            // One last verification against ComicVine/Metron (if configured) - an explicit action, so a short wait is fine.
            var verified = VerifyStoryEventCandidate is { } verify
                ? await verify(context, candidate)
                : (await ArcExternalVerificationService.VerifyAsync(context, new[] { candidate }, CancellationToken.None)).Single();

            using var transaction = context.Database.BeginTransaction();
            var storyEvent = StoryEventResolver.GetOrCreateForIssues(context, verified.ArcName, verified.Members.Select(m => m.Issue.Id));
            storyEvent.ComicVineArcId ??= verified.ComicVineArcId;
            storyEvent.MetronArcId ??= verified.MetronArcId;
            context.SaveChanges();

            // Position order lets AddMember's auto-incrementing Position reproduce the verified order; each member takes the role the
            // detector is sure of, and weaker guesses are held as suggestions.
            var detected = MemberRoleDetection.AddDetectedMembers(
                context, storyEvent.Id,
                verified.Members.OrderBy(m => m.Position ?? int.MaxValue).ThenBy(m => m.Issue.Year ?? int.MaxValue).Select(m => m.Issue.Id).ToList());
            transaction.Commit();
            return (storyEvent.Id, $"\"{verified.ArcName}\" added with {verified.Members.Count} issue(s). {detected}");
        }
    }

    private void DismissStoryEventCandidate(StoryEventCandidateRowViewModel row)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            StoryArcGroupingResolver.Dismiss(context, row.Candidate.ArcName, row.Candidate.Publisher);
        }

        Dispatcher.UIThread.Post(() =>
        {
            NewStoryEventCandidates.Remove(row);
            RaiseCounts();
        });
    }

    // --- Possible duplicates (the Story Event resolver's review list). ---

    /// <summary>Local only - rebuilds the review list from cached ids, aliases and member overlap.</summary>
    public void RefreshPossibleDuplicates()
    {
        using var context = PaperbunkrDb.CreateContext();
        PossibleDuplicates.Clear();
        foreach (var item in StoryEventIdentityResolver.FindReviewItems(context))
        {
            PossibleDuplicates.Add(new StoryEventDuplicateRowViewModel(item, MergeDuplicate, DismissDuplicate, CheckDuplicateAgain));
        }

        RaiseCounts();
    }

    private async Task<T> RunIdentityWork<T>(Func<Task<T>> work)
    {
        T result = default!;
        await (IdentityRunner ?? (w => Task.Run(w)))(async () => result = await work());
        return result;
    }

    private IReadOnlyDictionary<ComicProvider, IArcIdentitySource> CreateIdentitySources()
    {
        if (IdentitySources is not null)
        {
            return IdentitySources();
        }

        using var context = PaperbunkrDb.CreateContext();
        return ProviderArcIdentitySource.CreateAvailable(context);
    }

    /// <summary>"Check story events": the weekly task's two steps (resolver, then smart connector), now, as one Activity job.</summary>
    [RelayCommand]
    private async Task CheckStoryEventsAsync()
    {
        if (!CanChangeStoryEvents)
        {
            return;
        }

        IsFindingDuplicates = true;
        StoryEventCheckResult = null;
        using var job = _activity.StartJob(ActivityJobKind.SyncMetadata, "Checking story events");
        try
        {
            var sources = CreateIdentitySources();
            var progress = new Progress<(int Done, int Total)>(p => job.Report(p.Done, p.Total, "Checking story events"));
            var orderInputs = OrderInputs?.Invoke();
            var result = await RunIdentityWork(() => StoryEventChecks.RunDetailedAsync(sources, orderInputs, progress, CancellationToken.None));
            job.Succeed(result.Text, itemsProcessed: result.Identity.Checked, itemsFailed: result.Identity.Failed);
            StoryEventCheckResult = result.Text;
            _afterIdentityWork(result.Identity);
            RefreshPossibleDuplicates();
        }
        catch (Exception ex)
        {
            job.Fail($"Checking story events failed: {ex.Message}", ex: ex);
            StoryEventCheckResult = $"Checking failed: {ex.Message}";
        }
        finally
        {
            IsFindingDuplicates = false;
        }
    }

    [RelayCommand]
    private async Task CheckMetronAsync()
    {
        if (IsCheckingMetron) return;
        _metronScanCancellation?.Dispose();
        _metronScanCancellation = new CancellationTokenSource();
        var token = _metronScanCancellation.Token;
        IsCheckingMetron = true;
        MetronCheckResult = null;
        using var job = _activity.StartJob(ActivityJobKind.SyncMetadata, "Checking Metron universes");
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, job.CancellationToken);
        token = linkedCancellation.Token;
        try
        {
            var report = new Progress<(int Done, int Total, string Series)>(p =>
                job.Report(p.Done, p.Total, string.IsNullOrWhiteSpace(p.Series) ? "Checking Metron universes" : p.Series));
            var found = await Task.Run(async () =>
            {
                using var context = PaperbunkrDb.CreateContext();
                var provider = ComicProviderFactory.Create(context, ComicProvider.Metron, ComicVineRequestPriority.Low);
                if (provider is null)
                {
                    throw new InvalidOperationException(ComicProviderFactory.MissingCredentialsMessage(ComicProvider.Metron));
                }
                try
                {
                    return await ContinuityMetronSuggestionResolver.GetSuggestionsAsync(context, provider, token, report).ConfigureAwait(false);
                }
                finally { (provider as IDisposable)?.Dispose(); }
            }, token);

            StageContinuitySuggestions(found);
            MetronCheckResult = $"Checked the library; found {found.Count} Metron universe suggestion{(found.Count == 1 ? "" : "s")}.";
            job.Succeed(MetronCheckResult, itemsProcessed: found.Count);
            RaiseCounts();
        }
        catch (OperationCanceledException)
        {
            MetronCheckResult = "Metron check cancelled. Any findings already in the review queue were kept.";
            // Disposing the activity handle marks this run cancelled.
        }
        catch (Exception ex)
        {
            MetronCheckResult = $"Metron check failed: {ex.Message}";
            job.Fail(MetronCheckResult, ex: ex);
        }
        finally
        {
            IsCheckingMetron = false;
        }
    }

    internal void StageContinuitySuggestions(IEnumerable<ContinuitySuggestion> suggestions)
    {
        foreach (var suggestion in suggestions)
        {
            if (NewContinuitySuggestions.Any(row => row.Suggestion.Series.Id == suggestion.Series.Id && row.Suggestion.MetronUniverseId == suggestion.MetronUniverseId)) continue;
            NewContinuitySuggestions.Add(new ContinuitySuggestionRowViewModel(suggestion, AcceptContinuitySuggestion, DismissContinuitySuggestion, RaiseCounts));
        }
        RaiseCounts();
    }

    [RelayCommand]
    private void CancelMetronCheck() => _metronScanCancellation?.Cancel();

    [RelayCommand]
    private void SelectAllContinuitySuggestions()
    {
        foreach (var row in NewContinuitySuggestions) row.IsSelected = true;
        RaiseCounts();
    }

    [RelayCommand]
    private void ClearContinuitySuggestionSelection()
    {
        foreach (var row in NewContinuitySuggestions) row.IsSelected = false;
        RaiseCounts();
    }

    [RelayCommand]
    private async Task AcceptSelectedContinuitySuggestionsAsync()
    {
        var rows = NewContinuitySuggestions.Where(row => row.IsSelected).ToList();
        if (rows.Count == 0) return;
        var opened = new HashSet<int>();
        try
        {
            await Task.Run(() =>
            {
                using var context = PaperbunkrDb.CreateContext();
                using var transaction = context.Database.BeginTransaction();
                foreach (var row in rows) opened.Add(AcceptContinuitySuggestionInContext(context, row.Suggestion));
                transaction.Commit();
            });
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var row in rows) NewContinuitySuggestions.Remove(row);
                RaiseCounts();
                if (opened.Count == 1) _openContinuity(opened.Single());
                else _notify("Continuity suggestions applied", $"Added {rows.Count} series across {opened.Count} continuities.");
            });
        }
        catch (Exception ex) { _notify("Couldn't apply continuity suggestions", ex.Message); }
    }

    [RelayCommand]
    private void DismissSelectedContinuitySuggestions()
    {
        var rows = NewContinuitySuggestions.Where(row => row.IsSelected).ToList();
        if (rows.Count == 0) return;
        using (var context = PaperbunkrDb.CreateContext())
        using (var transaction = context.Database.BeginTransaction())
        {
            foreach (var row in rows) DismissContinuitySuggestionInContext(context, row.Suggestion);
            transaction.Commit();
        }
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var row in rows) NewContinuitySuggestions.Remove(row);
            RaiseCounts();
        });
    }

    /// <summary>After accepting a suggestion: id completion and matching for that one event, in the background. Silent unless it merges.</summary>
    internal Task CheckEventIdentityInBackground(int storyEventId) => CheckEventIdentitiesInBackground(new[] { storyEventId });

    private Task CheckEventIdentitiesInBackground(IReadOnlyCollection<int> storyEventIds)
    {
        var sources = CreateIdentitySources();
        return RunIdentityWork(() => StoryEventIdentitySweep.RunAsync(
                () => PaperbunkrDb.CreateContext(), sources, storyEventIds, storyEventIds.Count, null, CancellationToken.None))
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result.Merges.Count > 0)
                {
                    var summary = t.Result;
                    Dispatcher.UIThread.Post(() =>
                    {
                        _notify("Duplicate story event merged", summary.MergeDetails);
                        _afterIdentityWork(summary);
                        RefreshPossibleDuplicates();
                    });
                }
            }, TaskScheduler.Default);
    }

    /// <summary>Merge. Deferred one tick: the clicked row belongs to the list this rebuilds.</summary>
    private void MergeDuplicate(StoryEventDuplicateRowViewModel row)
    {
        if (row.Item.EventBId is not int other)
        {
            return;
        }

        int first = row.Item.EventAId;
        Dispatcher.UIThread.Post(() =>
        {
            StoryEventMergeResult result;
            try
            {
                using var context = PaperbunkrDb.CreateContext();
                result = StoryEventMerger.Merge(context, first, other);
            }
            catch (Exception ex)
            {
                _notify("Merge failed", ex.Message);
                RefreshPossibleDuplicates();
                return;
            }

            _notify("Story events merged", $"{result.RemovedName} → {result.SurvivorName}");
            _afterIdentityWork(new StoryEventIdentitySweepSummary(0, 0, 0, new[] { result }, 0, Array.Empty<ComicProvider>()));
            RefreshPossibleDuplicates();
        });
    }

    /// <summary>"Not the same" - remembered, never proposed again. Deferred like <see cref="MergeDuplicate"/>.</summary>
    private void DismissDuplicate(StoryEventDuplicateRowViewModel row)
    {
        if (row.Item.EventBId is not int other)
        {
            return;
        }

        int first = row.Item.EventAId;
        Dispatcher.UIThread.Post(() =>
        {
            using (var context = PaperbunkrDb.CreateContext())
            {
                StoryEventIdentityResolver.Dismiss(context, first, other);
            }

            RefreshPossibleDuplicates();
        });
    }

    /// <summary>"Check again" on a sources-disagree row: clear the note and re-run id completion for that event.</summary>
    private void CheckDuplicateAgain(StoryEventDuplicateRowViewModel row)
    {
        int eventId = row.Item.EventAId;
        Dispatcher.UIThread.Post(async () =>
        {
            using (var context = PaperbunkrDb.CreateContext())
            {
                if (context.StoryEvents.Find(eventId) is { } storyEvent)
                {
                    storyEvent.IdentityConflict = null;
                    storyEvent.IdentityCheckedAt = null;
                    context.SaveChanges();
                }
            }

            RefreshPossibleDuplicates();
            await CheckEventIdentityInBackground(eventId);
            Dispatcher.UIThread.Post(RefreshPossibleDuplicates);
        });
    }

    private void AcceptContinuitySuggestion(ContinuitySuggestionRowViewModel row)
    {
        int continuityId;
        string continuityName;
        using (var context = PaperbunkrDb.CreateContext())
        {
            continuityId = AcceptContinuitySuggestionInContext(context, row.Suggestion);
            var continuity = context.Continuities.Single(c => c.Id == continuityId);
            continuityId = continuity.Id;
            continuityName = continuity.Name;
        }

        _notify("Series added to continuity", $"\"{row.Suggestion.Series.Name}\" added to \"{continuityName}\".");
        Dispatcher.UIThread.Post(() =>
        {
            NewContinuitySuggestions.Remove(row);
            RaiseCounts();
            _openContinuity(continuityId);
        });
    }

    private void DismissContinuitySuggestion(ContinuitySuggestionRowViewModel row)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            DismissContinuitySuggestionInContext(context, row.Suggestion);
        }

        Dispatcher.UIThread.Post(() =>
        {
            NewContinuitySuggestions.Remove(row);
            RaiseCounts();
        });
    }

    private static int AcceptContinuitySuggestionInContext(PaperbunkrDbContext context, ContinuitySuggestion suggestion)
    {
        Continuity? continuity = suggestion.MetronUniverseId is int metronId
            ? context.Continuities.FirstOrDefault(c => c.MetronId == metronId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : null;
        continuity ??= context.Continuities.AsEnumerable()
            .FirstOrDefault(c => string.Equals(c.Name, suggestion.UniverseLabel, StringComparison.OrdinalIgnoreCase));
        if (continuity is null) continuity = ContinuityResolver.GetOrCreate(context, suggestion.UniverseLabel);
        if (suggestion.MetronUniverseId is int id)
        {
            string providerId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(continuity.MetronId) && continuity.MetronId != providerId)
                throw new InvalidOperationException($"A continuity named '{continuity.Name}' is already linked to a different Metron universe.");
            continuity.MetronId ??= providerId;
            continuity.Designation ??= suggestion.MetronDesignation;
            continuity.Publisher ??= suggestion.MetronPublisher;
        }
        else
        {
            continuity.WikidataId ??= suggestion.WikidataQid;
            continuity.FandomKey ??= suggestion.FandomKey;
            continuity.Description ??= suggestion.UniverseDescription;
        }
        context.SaveChanges();
        ContinuityResolver.AddSeriesToContinuity(context, suggestion.Series.Id, continuity.Id);
        if (suggestion.MetronUniverseId is null) continuity.Publisher ??= ContinuityResolver.InferPublisher(context, continuity.Id);
        context.SaveChanges();
        return continuity.Id;
    }

    private static void DismissContinuitySuggestionInContext(PaperbunkrDbContext context, ContinuitySuggestion suggestion)
    {
        if (suggestion.MetronUniverseId is int metronId) ContinuityMetronSuggestionResolver.Dismiss(context, suggestion.Series.Id, metronId);
        else if (suggestion.WikidataQid is string qid) ContinuityWikidataMatchResolver.Dismiss(context, suggestion.Series.Id, qid);
        else if (suggestion.FandomKey is string key) ContinuityWikidataMatchResolver.DismissFandom(context, suggestion.Series.Id, key);
    }
}
