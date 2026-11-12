using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.Services.EventMap;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.ViewModels;

/// <summary>A "Possible gaps" row (M4 §5).</summary>
public sealed record EventGapItem(int SeriesId, string Label);

/// <summary>
/// Continuity map M4 (docs/superpowers/specs/2026-11-11-continuity-map-completeness-design.md): ghost cards for the issues an event's
/// saved provider list has that aren't members, Check for missing issues, Add to Wanted (with one confirmation and Undo), Add to event,
/// and the gap check when there's no provider list. Ghosts are display-only; the one membership write is an explicit Add to event.
/// </summary>
public sealed partial class EventMapViewModel
{
    /// <summary>Show the provider list's missing issues; persisted as <c>AppSettings.EventMapShowMissing</c> (default off).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMissingLabel))]
    private bool _showMissing;

    [ObservableProperty]
    private bool _isCheckingMissing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUndoWanted))]
    private WantedRequestResult? _lastWanted;

    private bool _showMissingChangedBeforeLoad;

    public ObservableCollection<EventGapItem> Gaps { get; } = new();

    /// <summary>Confirms tracking new series before Add to Wanted; null in tests means "yes".</summary>
    public IDialogService? Dialogs { get; set; }

    /// <summary>Test seams: the database work behind the M4 actions.</summary>
    internal Func<IReadOnlyList<int>, Task<MissingCheckResult>> CheckMissingRunner { get; set; } = ids => Task.Run(() =>
        EventMissingChecker.CheckAsync(ids, PaperbunkrDb.CreateContext, ReadingListSourceRegistry.Get, null, System.Threading.CancellationToken.None));

    internal Func<IReadOnlyList<EventGhost>, IReadOnlyList<string>> SeriesToTrackRunner { get; set; } = ghosts =>
    {
        using var context = PaperbunkrDb.CreateContext();
        return EventWantedRequester.SeriesToTrack(context, ghosts);
    };

    internal Func<IReadOnlyList<EventGhost>, WantedRequestResult> RequestWantedRunner { get; set; } = ghosts =>
    {
        using var context = PaperbunkrDb.CreateContext();
        return EventWantedRequester.Request(context, ghosts);
    };

    internal Action<WantedRequestResult> UndoWantedRunner { get; set; } = result =>
    {
        using var context = PaperbunkrDb.CreateContext();
        EventWantedRequester.Undo(context, result);
    };

    internal Func<int, int, bool> AddToEventRunner { get; set; } = (eventId, issueId) =>
    {
        using var context = PaperbunkrDb.CreateContext();
        return EventMembershipResolver.AddMember(context, eventId, issueId, EventMembershipRole.Core);
    };

    internal Action<string> OpenUrl { get; set; } = url => ExternalLinks.TryOpenWeb(url);

    public string ShowMissingLabel => ShowMissing ? "Hide missing" : "Show missing";

    /// <summary>Every saved provider list on this map, per event.</summary>
    private IEnumerable<EventMissingData> MissingData => _continuityData is { } data
        ? data.Events.Where(e => !_hiddenEvents.Contains(e.Id)).Select(e => e.Missing).OfType<EventMissingData>()
        : _source?.Missing is { } m ? new[] { m } : Array.Empty<EventMissingData>();

    /// <summary>The "Show missing" toggle shows once the map has a saved provider list (§3).</summary>
    public bool HasMissingSnapshot => MissingData.Any(m => m.HasSnapshot);

    /// <summary>"Check for missing issues" needs a verified arc id somewhere on the map.</summary>
    public bool CanCheckMissing => MissingData.Any(m => m.HasArcIdentity) && !IsCheckingMissing;

    public int MissingCount => ShowMissing ? Cards.Count(c => c.IsMissingGhost) : MissingData.Sum(m => m.Ghosts.Count(g => g.Kind == EventGhostKind.Missing));

    public bool HasMissingIssues => MissingCount > 0;

    /// <summary>"missing issues checked 41 days ago" once a list is over 30 days old (§3); empty otherwise.</summary>
    public string MissingStaleText
    {
        get
        {
            var oldest = MissingData.Select(m => m.CheckedAt).OfType<DateTime>().DefaultIfEmpty().Min();
            int days = oldest == default ? 0 : (int)(DateTime.UtcNow - oldest).TotalDays;
            return ShowMissing && days > 30 ? $"missing issues checked {days} days ago" : string.Empty;
        }
    }

    public bool HasGaps => Gaps.Count > 0;

    public string GapsLabel => $"Possible gaps · {Gaps.Count}";

    public bool HasUndoWanted => LastWanted is { CreatedWantedIds.Count: > 0 } or { CreatedWatchedIds.Count: > 0 };

    /// <summary>An issue the map has no file for can't be read or marked; a ghost can't either.</summary>
    public bool IsSelectedMember => SelectedCard is { IsMember: true };

    public bool IsSelectedGhost => SelectedCard is { IsGhost: true };

    [RelayCommand]
    private void ToggleShowMissing() => ShowMissing = !ShowMissing;

    partial void OnShowMissingChanged(bool value)
    {
        if (!_applyingRoomSettings)
        {
            if (!_roomSettingsLoaded) _showMissingChangedBeforeLoad = true;
            else _ = SaveRoomSettingsAsync();
        }

        if (!_suppressRelayout && (_source is not null || _continuityData is not null))
        {
            Relayout(SelectedCard is { IsGhost: false } card ? card.IssueId : null);
        }
    }

    partial void OnIsCheckingMissingChanged(bool value) => OnPropertyChanged(nameof(CanCheckMissing));

    [RelayCommand]
    private async Task CheckForMissingIssues()
    {
        var ids = _continuityData is { } data
            ? data.Events.Where(e => !_hiddenEvents.Contains(e.Id) && e.Missing is { HasArcIdentity: true }).Select(e => e.Id).ToList()
            : StoryEventId is int id && _source?.Missing is { HasArcIdentity: true } ? new List<int> { id } : new List<int>();
        if (ids.Count == 0)
        {
            _activity.RaiseAlert(new ActivityAlert
            {
                Severity = ActivityAlertSeverity.Info,
                Title = "Link this event to a Metron or ComicVine arc first",
                Detail = "Smart fill › Compare sources links an event to its arc; then the map can check for missing issues.",
                DedupeKey = "event-map-missing-identity",
            });
            return;
        }

        IsCheckingMissing = true;
        using var job = _activity.StartJob(ActivityJobKind.SyncMetadata, "Checking for missing issues");
        try
        {
            var result = await CheckMissingRunner(ids);
            string summary = result.Saved == 0 ? "No provider lists could be fetched" : $"Checked {result.Saved} provider list{(result.Saved == 1 ? "" : "s")}";
            if (result.Failures.Count > 0) summary += $" · {result.Failures.Count} failed";
            job.Succeed(summary, itemsProcessed: result.Saved, itemsFailed: result.Failures.Count);
            ShowMissing = true;
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            job.Fail("Couldn't check for missing issues", ex: ex);
        }
        finally
        {
            IsCheckingMissing = false;
        }
    }

    [RelayCommand]
    private Task AddSelectedToWanted() => SelectedCard is { IsMissingGhost: true, Ghost: { } ghost } card
        ? AddToWantedAsync(new[] { ghost }, new[] { card })
        : Task.CompletedTask;

    [RelayCommand]
    private Task AddAllMissingToWanted()
    {
        var cards = Cards.Where(c => c.IsMissingGhost && !c.IsWanted).ToList();
        return AddToWantedAsync(cards.Select(c => c.Ghost!).ToList(), cards);
    }

    private async Task AddToWantedAsync(IReadOnlyList<EventGhost> ghosts, IReadOnlyList<EventMapCardViewModel> cards)
    {
        if (ghosts.Count == 0)
        {
            return;
        }

        try
        {
            var toTrack = await Task.Run(() => SeriesToTrackRunner(ghosts));
            if (toTrack.Count > 0 && Dialogs is { } dialogs)
            {
                string names = string.Join(", ", toTrack.Take(6)) + (toTrack.Count > 6 ? $" and {toTrack.Count - 6} more" : string.Empty);
                bool go = await dialogs.ConfirmAsync(
                    $"This starts tracking {toTrack.Count} series: {names}. Their missing issues go to Wanted.",
                    title: "Track and add to Wanted", confirmLabel: "Track and add");
                if (!go)
                {
                    return;
                }
            }

            var result = await Task.Run(() => RequestWantedRunner(ghosts));
            LastWanted = result;
            foreach (var card in cards)
            {
                card.IsWanted = true;
            }

            string detail = $"{result.Requested} issue{(result.Requested == 1 ? "" : "s")} added to Wanted"
                + (result.CreatedWatchedIds.Count > 0 ? $"; now tracking {result.CreatedWatchedIds.Count} series" : string.Empty)
                + (result.Unmatched.Count > 0 ? $". {result.Unmatched.Count} couldn't be matched - open them in Wanted to search." : ".");
            _activity.RaiseAlert(new ActivityAlert
            {
                Severity = result.Unmatched.Count > 0 ? ActivityAlertSeverity.Warning : ActivityAlertSeverity.Info,
                Title = "Added to Wanted",
                Detail = detail,
                DedupeKey = "event-map-wanted",
            });
        }
        catch (Exception ex)
        {
            _activity.RaiseAlert(new ActivityAlert
            {
                Severity = ActivityAlertSeverity.Error,
                Title = "Couldn't add to Wanted",
                Detail = ex.Message,
                DedupeKey = "event-map-wanted",
            });
        }
    }

    [RelayCommand]
    private async Task UndoWanted()
    {
        if (LastWanted is not { } last)
        {
            return;
        }

        await Task.Run(() => UndoWantedRunner(last));
        LastWanted = null;
        foreach (var card in Cards.Where(c => c.IsWanted))
        {
            card.IsWanted = false;
        }
    }

    [RelayCommand]
    private async Task AddSelectedToEvent()
    {
        if (SelectedCard is not { IsOwnedGhost: true, Ghost.OwnedIssueId: int issueId } card)
        {
            return;
        }

        int? eventId = card.OwnerEventId ?? StoryEventId;
        if (eventId is not int target)
        {
            return;
        }

        await Task.Run(() => AddToEventRunner(target, issueId));
        await ReloadAsync(issueId);
    }

    [RelayCommand]
    private void OpenGhostOnProvider()
    {
        if (SelectedCard?.GhostProviderUrl is { } url)
        {
            OpenUrl(url);
        }
    }

    [RelayCommand]
    private void OpenGapSeries(EventGapItem? gap)
    {
        if (gap is not null)
        {
            Dispatcher.UIThread.Post(() => _openSeriesDetail(gap.SeriesId));
        }
    }

    private Task ReloadAsync(int? reselectIssueId = null) =>
        _continuityData is { } data ? LoadContinuityAsync(data.ContinuityId, reselectIssueId)
        : StoryEventId is int id ? LoadAsync(id, reselectIssueId) : Task.CompletedTask;

    /// <summary>The event map's rows with ghosts placed in, when Show missing is on.</summary>
    private EventMapSource SourceForLayout(EventMapSource source)
    {
        if (!ShowMissing || source.Missing is not { Ghosts.Count: > 0 } missing)
        {
            return source with { Rows = FilterFeatured(source.Rows) };
        }

        int nextId = 0;
        return source with { Rows = FilterFeatured(EventGhostPlacement.Inject(source.Rows, missing.Ghosts, ref nextId, new Dictionary<string, int>())) };
    }

    /// <summary>The continuity data with each event's ghosts placed in its members, when Show missing is on.</summary>
    private ContinuityMapData DataForLayout(ContinuityMapData data)
    {
        if (!ShowMissing || !data.Events.Any(e => e.Missing is { Ghosts.Count: > 0 }))
        {
            return FeaturingHideOthers && Featuring is not null
                ? data with { Events = data.Events.Select(e => e with { Members = FilterFeatured(e.Members) }).ToList(), LooseIssues = FilterFeatured(data.LooseIssues) }
                : data;
        }

        int nextId = 0;
        var pseudo = new Dictionary<string, int>();
        var events = new List<ContinuityEventData>();
        foreach (var e in data.Events)
        {
            events.Add(e.Missing is { Ghosts.Count: > 0 } m ? e with { Members = FilterFeatured(EventGhostPlacement.Inject(e.Members, m.Ghosts, ref nextId, pseudo)) }
                : e with { Members = FilterFeatured(e.Members) });
        }

        return data with { Events = events, LooseIssues = FilterFeatured(data.LooseIssues) };
    }

    /// <summary>Recomputes the missing counts and, with no provider list, the gap check (§5).</summary>
    private void RefreshMissing()
    {
        Gaps.Clear();
        var data = MissingData.ToList();
        if (data.Count > 0 && !data.Any(m => m.HasSnapshot))
        {
            var owned = new Dictionary<int, IReadOnlyList<string>>();
            foreach (var m in data)
            {
                foreach (var (series, numbers) in m.OwnedNumbers)
                {
                    owned[series] = numbers;
                }
            }

            foreach (var gap in EventGapCheck.Find(Layout.Cells.Select(c => c.Row), owned))
            {
                Gaps.Add(new EventGapItem(gap.SeriesId, $"{gap.SeriesName} #{gap.Number}"));
            }
        }

        OnPropertyChanged(nameof(HasGaps));
        OnPropertyChanged(nameof(GapsLabel));
        OnPropertyChanged(nameof(HasMissingSnapshot));
        OnPropertyChanged(nameof(CanCheckMissing));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(HasMissingIssues));
        OnPropertyChanged(nameof(MissingStaleText));
    }
}
