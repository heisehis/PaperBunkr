using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Continuity screen's Suggestions &amp; checks panel (docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md): the
/// three review queues that used to sit in the sidebar - new story-event suggestions (docs/superpowers/specs/
/// 2026-09-17-storyevent-continuity-autopopulate-design.md Phase 1), possible duplicate events (docs/superpowers/specs/
/// 2026-09-27-story-event-resolver-design.md §4) and shared-universe suggestions (the same design, Phase 2) - plus the Check story events
/// and Check Wikidata buttons. Behaviour is ported unchanged; every row removal now waits one dispatcher tick, because the clicked
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
        Action changed)
    {
        _notify = notify;
        _activity = activity;
        _openEvent = openEvent;
        _openContinuity = openContinuity;
        _afterIdentityWork = afterIdentityWork;
        _changed = changed;
    }

    /// <summary>Test seam: how identity work leaves the UI thread. Null = a thread-pool task (tests pass a synchronous runner).</summary>
    internal Func<Func<Task>, Task>? IdentityRunner { get; set; }

    /// <summary>Test seam: the smart connector's Wikidata lookup. Null = the live Wikidata client.</summary>
    internal Func<IWikidataLookup>? IdentityWikidata { get; set; }

    /// <summary>Test seam: the provider sources the sweep uses. Null = whatever credentials are saved.</summary>
    internal Func<IReadOnlyDictionary<ComicProvider, IArcIdentitySource>>? IdentitySources { get; set; }

    public ObservableCollection<StoryEventCandidateRowViewModel> NewStoryEventCandidates { get; } = new();

    public ObservableCollection<StoryEventDuplicateRowViewModel> PossibleDuplicates { get; } = new();

    public ObservableCollection<ContinuitySuggestionRowViewModel> NewContinuitySuggestions { get; } = new();

    public bool HasNoNewStoryEventCandidates => NewStoryEventCandidates.Count == 0;

    public bool HasPossibleDuplicates => PossibleDuplicates.Count > 0;

    public bool HasNoNewContinuitySuggestions => NewContinuitySuggestions.Count == 0;

    public string PossibleDuplicatesSummary => PossibleDuplicates.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Everything waiting here - the sidebar row's badge.</summary>
    public int TotalCount => NewStoryEventCandidates.Count + PossibleDuplicates.Count + NewContinuitySuggestions.Count;

    public bool IsEmpty => TotalCount == 0;

    [ObservableProperty]
    private bool _isFindingDuplicates;

    [ObservableProperty]
    private string? _storyEventCheckResult;

    [ObservableProperty]
    private bool _isCheckingWikidataSuggestions;

    /// <summary>Distinguishes "haven't checked yet" from "checked, found nothing" - without it the button read as doing nothing.</summary>
    [ObservableProperty]
    private string? _wikidataCheckResult;

    /// <summary>Set when the panel was opened at one pair ("Possible duplicate: X" on an event page), so the view can scroll to it.</summary>
    [ObservableProperty]
    private int? _focusedDuplicateEventId;

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(HasNoNewStoryEventCandidates));
        OnPropertyChanged(nameof(HasPossibleDuplicates));
        OnPropertyChanged(nameof(HasNoNewContinuitySuggestions));
        OnPropertyChanged(nameof(PossibleDuplicatesSummary));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(IsEmpty));
        _changed();
    }

    // --- New story-event suggestions: local-only on load, verified once on Accept. ---

    public void RefreshStoryEventCandidates()
    {
        using var context = PaperbunkrDb.CreateContext();
        NewStoryEventCandidates.Clear();
        foreach (var candidate in StoryArcGroupingResolver.GetCandidates(context))
        {
            NewStoryEventCandidates.Add(new StoryEventCandidateRowViewModel(candidate, AcceptStoryEventCandidateAsync, DismissStoryEventCandidate));
        }

        RaiseCounts();
    }

    private async Task AcceptStoryEventCandidateAsync(StoryEventCandidateRowViewModel row)
    {
        int storyEventId;
        string message;
        using (var context = PaperbunkrDb.CreateContext())
        {
            // One last verification against ComicVine/Metron (if configured) - an explicit action, so a short wait is fine.
            var verifiedList = await ArcExternalVerificationService.VerifyAsync(context, new[] { row.Candidate }, CancellationToken.None);
            var verified = verifiedList.Single();

            var storyEvent = StoryEventResolver.GetOrCreateForIssues(context, verified.ArcName, verified.Members.Select(m => m.Issue.Id));
            storyEvent.ComicVineArcId ??= verified.ComicVineArcId;
            storyEvent.MetronArcId ??= verified.MetronArcId;
            context.SaveChanges();

            // Position order lets AddMember's auto-incrementing Position reproduce the verified order; each member takes the role the
            // detector is sure of, and weaker guesses are held as suggestions.
            var detected = MemberRoleDetection.AddDetectedMembers(
                context, storyEvent.Id,
                verified.Members.OrderBy(m => m.Position ?? int.MaxValue).ThenBy(m => m.Issue.Year ?? int.MaxValue).Select(m => m.Issue.Id).ToList());
            storyEventId = storyEvent.Id;
            message = $"\"{verified.ArcName}\" added with {verified.Members.Count} issue(s). {detected}";
        }

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
        if (IsFindingDuplicates)
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
            var wikidata = IdentityWikidata?.Invoke();
            var result = await RunIdentityWork(() => StoryEventChecks.RunDetailedAsync(sources, wikidata, progress, CancellationToken.None));
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

    /// <summary>After accepting a suggestion: id completion and matching for that one event, in the background. Silent unless it merges.</summary>
    internal Task CheckEventIdentityInBackground(int storyEventId)
    {
        var sources = CreateIdentitySources();
        return RunIdentityWork(() => StoryEventIdentitySweep.RunAsync(
                () => PaperbunkrDb.CreateContext(), sources, new[] { storyEventId }, 1, null, CancellationToken.None))
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

    // --- Shared-universe suggestions: only on an explicit click, never on navigation (each needs Wikidata lookups). ---

    [RelayCommand]
    private async Task CheckForWikidataSuggestions()
    {
        IsCheckingWikidataSuggestions = true;
        WikidataCheckResult = null;
        NewContinuitySuggestions.Clear();
        RaiseCounts();

        using var job = _activity.StartJob(ActivityJobKind.SyncMetadata, "Checking Wikidata for shared universes");
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            using var httpClient = WikidataClient.CreateClient();
            var client = new WikidataClient(httpClient);
            var progress = new Progress<(int Done, int Total)>(p => job.Report(p.Done, p.Total, $"{p.Done} / {p.Total} series"));

            // Each suggestion shows the moment it's found; the resolver runs with ConfigureAwait(false), so post to the UI thread.
            void OnFound(ContinuitySuggestion suggestion) => Dispatcher.UIThread.Post(() =>
            {
                NewContinuitySuggestions.Add(new ContinuitySuggestionRowViewModel(suggestion, AcceptContinuitySuggestion, DismissContinuitySuggestion));
                RaiseCounts();
            });

            // Uncapped: an explicit, watched click with Activity Center progress, not the unattended weekly scan.
            var suggestions = await ContinuityWikidataMatchResolver.GetSuggestionsAsync(
                context, client, CancellationToken.None, progress: progress, capToBoundedRun: false, onSuggestionFound: OnFound);

            WikidataCheckResult = suggestions.Count == 0
                ? "Checked - no shared-universe matches found this time."
                : $"Checked - found {suggestions.Count} suggestion{(suggestions.Count == 1 ? "" : "s")}.";
            job.Succeed(WikidataCheckResult);
        }
        catch (Exception ex)
        {
            job.Fail($"Checking Wikidata failed: {ex.Message}", ex: ex);
            WikidataCheckResult = $"Checking failed: {ex.Message}";
        }
        finally
        {
            IsCheckingWikidataSuggestions = false;
        }
    }

    private void AcceptContinuitySuggestion(ContinuitySuggestionRowViewModel row)
    {
        int continuityId;
        string continuityName;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var continuity = ContinuityResolver.GetOrCreate(context, row.Suggestion.UniverseLabel);
            continuity.WikidataId ??= row.Suggestion.WikidataQid;
            continuity.FandomKey ??= row.Suggestion.FandomKey;
            continuity.Description ??= row.Suggestion.UniverseDescription;
            context.SaveChanges();

            ContinuityResolver.AddSeriesToContinuity(context, row.Suggestion.Series.Id, continuity.Id);

            // Wikidata has no publisher claim on shared-universe items, so the publisher comes from the member series' issues.
            continuity.Publisher ??= ContinuityResolver.InferPublisher(context, continuity.Id);
            context.SaveChanges();
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
            if (row.Suggestion.WikidataQid is string wikidataQid)
            {
                ContinuityWikidataMatchResolver.Dismiss(context, row.Suggestion.Series.Id, wikidataQid);
            }
            else if (row.Suggestion.FandomKey is string fandomKey)
            {
                ContinuityWikidataMatchResolver.DismissFandom(context, row.Suggestion.Series.Id, fandomKey);
            }
        }

        Dispatcher.UIThread.Post(() =>
        {
            NewContinuitySuggestions.Remove(row);
            RaiseCounts();
        });
    }
}
