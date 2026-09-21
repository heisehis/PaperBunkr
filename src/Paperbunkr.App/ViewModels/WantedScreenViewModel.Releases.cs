using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>One release tile on the Releases shelf (docs/superpowers/specs/2026-09-20-weekly-pull-list-design.md, redesigned in 2026-09-21-wanted-screen-redesign-design.md).</summary>
public sealed class ReleaseRowViewModel
{
    public required int ReleaseId { get; init; }
    public required int SeriesId { get; init; }
    public required string Title { get; init; }

    /// <summary>The publisher, when it is known (the day header already says when).</summary>
    public string? Subtitle { get; init; }
    public RemoteCoverSource Cover { get; init; } = new(null);

    /// <summary>The series is followed: this release becomes an Upcoming want on its own.</summary>
    public bool IsFollowed { get; init; }

    /// <summary>This issue is already wanted, downloading or owned, so there is nothing to request.</summary>
    public bool IsTaken { get; init; }

    /// <summary>The user hid this release; only shown while "Show hidden" is on.</summary>
    public bool IsHidden { get; init; }
    public bool CanHide => !IsHidden;

    public bool CanFollow => !IsFollowed && !IsHidden;
    public bool CanRequest => !IsTaken && !IsHidden;

    /// <summary>The tile's one visible button: Request while there is something to request, otherwise Follow while the series isn't followed.</summary>
    public bool PrimaryIsRequest => CanRequest;
    public bool PrimaryIsFollow => !CanRequest && CanFollow;

    /// <summary>Follow also lives in the overflow menu when Request holds the button.</summary>
    public bool MenuHasFollow => CanRequest && CanFollow;

    public string StatusText => IsHidden ? "Hidden" : IsTaken ? "Wanted" : IsFollowed ? "Following" : string.Empty;
    public bool HasStatus => IsHidden || IsTaken || IsFollowed;
}

/// <summary>One store date of the viewed week with its releases.</summary>
public sealed class ReleaseDayViewModel
{
    public required DateTime Date { get; init; }
    public required IReadOnlyList<ReleaseRowViewModel> Rows { get; init; }
    public string Title => Date.ToString("dddd, MMM d", CultureInfo.CurrentCulture);
    public string CountText => Rows.Count == 1 ? "1 release" : $"{Rows.Count} releases";
}

/// <summary>A cell of the month calendar: Avalonia's own Calendar can't decorate days, so this is a small custom grid.</summary>
public sealed class CalendarDayViewModel
{
    public required DateTime Date { get; init; }
    public bool InMonth { get; init; }
    public bool IsToday { get; init; }

    /// <summary>The day is inside the week the shelf is showing.</summary>
    public bool IsInWeek { get; init; }
    public bool HasReleases { get; init; }
    public bool HasFollowed { get; init; }
    public string DayText => Date.Day.ToString(CultureInfo.CurrentCulture);
    public string AutomationName => Date.ToString("dddd, MMMM d", CultureInfo.CurrentCulture) + (HasReleases ? ", has releases" : string.Empty);
}

public sealed partial class WantedScreenViewModel
{
    public const string AllPublishers = "All publishers";

    public ObservableCollection<ReleaseDayViewModel> ReleaseDays { get; } = new();
    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; } = new();

    public bool HasNoReleases => ReleaseDays.Count == 0;

    /// <summary>The Monday of the week the shelf shows.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ReleaseWeekLabel), nameof(ReleaseWeekCaption), nameof(IsViewingThisWeek))]
    private DateTime _releaseWeekStart;

    /// <summary>The first day of the month the calendar popup shows.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CalendarTitle))]
    private DateTime _calendarMonth;

    [ObservableProperty] private bool _isCalendarOpen;

    /// <summary>A week outside the cached window is being fetched.</summary>
    [ObservableProperty] private bool _isReleasesLoading;

    /// <summary>The last on-demand fetch failed; the week says so instead of looking empty.</summary>
    [ObservableProperty] private bool _releaseLoadFailed;

    /// <summary>The number of releases in the viewed week (after the filters).</summary>
    [ObservableProperty] private int _releaseCount;

    /// <summary>"All publishers" or one publisher's name; a text value for the suggest box.</summary>
    [ObservableProperty] private string _releasePublisherText = AllPublishers;

    [ObservableProperty] private bool _releasesFollowedOnly;

    /// <summary>Also list the releases the user hid, each with a Restore button.</summary>
    [ObservableProperty] private bool _releasesShowHidden;

    /// <summary>Every publisher seen in the cached list, plus "All publishers" first.</summary>
    [ObservableProperty] private IReadOnlyList<string> _releasePublisherNames = new[] { AllPublishers };

    /// <summary>Why the week is empty, when it is.</summary>
    [ObservableProperty] private string _releasesEmptyText = string.Empty;

    /// <summary>"Metron · updated 2h ago".</summary>
    [ObservableProperty] private string _releaseFreshnessText = string.Empty;

    public string ReleaseWeekLabel => WeekLabel(ReleaseWeekStart);
    public string ReleaseWeekCaption => WeekTitle(ReleaseWeekStart, _today());
    public bool IsViewingThisWeek => ReleaseWeekStart == WeekStart(_today());
    public string CalendarTitle => CalendarMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    private bool _releasesLoaded;
    private DateTime? _releasesRefreshedAt;
    private string _releaseSignature = string.Empty;
    private readonly HashSet<DateTime> _fetchedWeeks = new();
    private CancellationTokenSource? _weekFetch;
    private Dictionary<DateTime, (int Count, bool Followed)> _releaseDayInfo = new();

    partial void OnReleasePublisherTextChanged(string value) => ReloadReleases();

    partial void OnReleasesFollowedOnlyChanged(bool value) => ReloadReleases();

    partial void OnReleasesShowHiddenChanged(bool value) => ReloadReleases();

    partial void OnActiveTabChanged(WantedTab value)
    {
        if (value == WantedTab.Releases)
        {
            ReloadReleases();
        }
    }

    private void ReloadReleases()
    {
        using var context = _createContext();
        LoadReleases(context, _today());
        OnPropertyChanged(nameof(HasNoReleases));
    }

    private void LoadReleases(PaperbunkrDbContext context, DateTime today)
    {
        _releasesLoaded = true;
        var releases = context.PullListReleases.AsNoTracking().OrderBy(r => r.StoreDate).ThenBy(r => r.SeriesName).ThenBy(r => r.IssueNumber).ToList();
        var listProvider = releases.FirstOrDefault()?.Provider ?? ComicProvider.Metron;
        var seriesInfo = context.ReleaseSeries.AsNoTracking().Where(m => m.Provider == listProvider).ToDictionary(m => m.SeriesId);
        var publishers = seriesInfo.Values.Select(m => m.Publisher).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var names = new[] { AllPublishers }.Concat(publishers).ToList();
        if (!names.SequenceEqual(ReleasePublisherNames))
        {
            ReleasePublisherNames = names;
        }

        // Which Metron series the user follows (a ComicVine series counts through its cross-reference), and which numbers of them are already wanted or owned.
        var followed = new Dictionary<int, WatchedSeries>();
        var tracked = new Dictionary<int, WatchedSeries>();
        foreach (var watched in context.WatchedSeries.AsNoTracking().ToList())
        {
            if (PullListService.ListSeriesIdFor(context, watched, listProvider) is not int listId)
            {
                continue;
            }

            tracked[listId] = watched;
            if (watched.WatchFutureReleases && !watched.IsPaused)
            {
                followed[listId] = watched;
            }
        }

        var wantedNumbers = context.WantedIssues.AsNoTracking()
            .Where(w => w.Status != WantedIssueStatus.Ignored && w.Status != WantedIssueStatus.Failed)
            .Select(w => new { w.WatchedSeriesId, w.IssueNumber }).ToList();
        var ownedByLocalSeries = context.Issues.AsNoTracking().Where(i => !i.IsPlaceholder && !i.FileIsMissing)
            .Select(i => new { i.SeriesId, i.Number }).ToList();

        bool publisherFilter = !string.Equals(ReleasePublisherText, AllPublishers, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ReleasePublisherText);
        var rows = new List<(DateTime Date, ReleaseRowViewModel Row)>();
        foreach (var release in releases)
        {
            seriesInfo.TryGetValue(release.SeriesId, out var info);
            if (publisherFilter && !string.Equals(info?.Publisher, ReleasePublisherText, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (release.IsHidden && !ReleasesShowHidden)
            {
                continue;
            }

            bool isFollowed = followed.ContainsKey(release.SeriesId);
            if (ReleasesFollowedOnly && !isFollowed)
            {
                continue;
            }

            bool isTaken = false;
            if (tracked.TryGetValue(release.SeriesId, out var watchedSeries))
            {
                isTaken = wantedNumbers.Any(w => w.WatchedSeriesId == watchedSeries.Id && IssueNumbers.Equal(w.IssueNumber, release.IssueNumber))
                    || (watchedSeries.SeriesId is int localId && ownedByLocalSeries.Any(o => o.SeriesId == localId && IssueNumbers.Equal(o.Number, release.IssueNumber)));
            }

            rows.Add((release.StoreDate.Date, new ReleaseRowViewModel
            {
                ReleaseId = release.Id,
                SeriesId = release.SeriesId,
                Title = $"{release.SeriesName} #{release.IssueNumber}",
                Subtitle = info?.Publisher,
                Cover = new RemoteCoverSource(release.CoverImageUrl),
                IsFollowed = isFollowed,
                IsTaken = isTaken,
                IsHidden = release.IsHidden,
            }));
        }

        // A scheduled refresh replaces the whole cache, so the weeks fetched on demand before it are gone and may be fetched again.
        var settings = context.GetOrCreateAcquisitionSettings();
        if (settings.PullListRefreshedAt != _releasesRefreshedAt)
        {
            _releasesRefreshedAt = settings.PullListRefreshedAt;
            _fetchedWeeks.Clear();
        }

        var weekStart = ReleaseWeekStart.Date;
        var weekEnd = weekStart.AddDays(6);
        var weekRows = rows.Where(r => r.Date >= weekStart && r.Date <= weekEnd).ToList();

        // Only touch the shelf when the week's content changed: a download ticking along elsewhere refreshes this screen every second.
        string signature = weekStart.Ticks + "|" + string.Join(",", weekRows.Select(r => $"{r.Row.ReleaseId}:{r.Row.IsTaken}:{r.Row.IsFollowed}:{r.Row.IsHidden}"));
        if (signature != _releaseSignature)
        {
            _releaseSignature = signature;
            Fill(ReleaseDays, weekRows.GroupBy(r => r.Date).OrderBy(g => g.Key)
                .Select(g => new ReleaseDayViewModel { Date = g.Key, Rows = g.Select(r => r.Row).ToList() }));
        }

        ReleaseCount = weekRows.Count;
        _releaseDayInfo = rows.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => (g.Count(), g.Any(r => r.Row.IsFollowed)));
        BuildCalendar(today);

        ReleaseFreshnessText = settings.PullListRefreshedAt is DateTime at
            ? $"{ComicProviderFactory.DisplayName(listProvider)} · updated {Ago(DateTime.UtcNow - at)}"
            : string.Empty;

        var hasSource = ComicProviderFactory.IsAvailable(context, ComicProvider.Metron) || ComicProviderFactory.IsAvailable(context, ComicProvider.ComicVine);
        bool weekHasAny = releases.Any(r => r.StoreDate.Date >= weekStart && r.StoreDate.Date <= weekEnd);
        ReleasesEmptyText = ReleaseLoadFailed
            ? "Couldn't load this week. Check your connection and the login under Preferences → Connections, then try again."
            : weekHasAny
                ? "Nothing matches these filters."
                : !hasSource
                    ? "The weekly pull list comes from Metron, or from ComicVine when there is no Metron login. Save either under Preferences → Connections and it fills in on the next check."
                    : releases.Count == 0 && settings.PullListRefreshedAt is null
                        ? "Nothing fetched yet. Press Search now, or wait for the next automatic check."
                        : "No releases this week.";
    }

    private void BuildCalendar(DateTime today)
    {
        var month = new DateTime(CalendarMonth == default ? WeekStart(today).Year : CalendarMonth.Year, CalendarMonth == default ? WeekStart(today).Month : CalendarMonth.Month, 1);
        var first = WeekStart(month);
        var weekStart = ReleaseWeekStart.Date;
        Fill(CalendarDays, Enumerable.Range(0, 42).Select(i =>
        {
            var date = first.AddDays(i);
            _releaseDayInfo.TryGetValue(date, out var info);
            return new CalendarDayViewModel
            {
                Date = date,
                InMonth = date.Month == month.Month,
                IsToday = date == today.Date,
                IsInWeek = date >= weekStart && date <= weekStart.AddDays(6),
                HasReleases = info.Count > 0,
                HasFollowed = info.Followed,
            };
        }));
    }

    private static string Ago(TimeSpan span) => span.TotalMinutes < 1 ? "just now"
        : span.TotalMinutes < 60 ? $"{(int)span.TotalMinutes}m ago"
        : span.TotalHours < 48 ? $"{(int)span.TotalHours}h ago"
        : $"{(int)span.TotalDays}d ago";

    private static DateTime WeekStart(DateTime date)
    {
        int offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return date.Date.AddDays(-offset);
    }

    private static string WeekLabel(DateTime weekStart)
    {
        var end = weekStart.AddDays(6);
        return weekStart.Month == end.Month
            ? $"{weekStart.ToString("MMM d", CultureInfo.CurrentCulture)} – {end.Day}"
            : $"{weekStart.ToString("MMM d", CultureInfo.CurrentCulture)} – {end.ToString("MMM d", CultureInfo.CurrentCulture)}";
    }

    private static string WeekTitle(DateTime weekStart, DateTime today)
    {
        int weeks = (int)((weekStart - WeekStart(today)).TotalDays / 7);
        return weeks switch
        {
            0 => "This week",
            1 => "Next week",
            -1 => "Last week",
            _ => string.Empty,
        };
    }

    /// <summary>Whether a week is worth fetching on demand: it reaches outside the window the background refresh keeps, and hasn't been fetched since that refresh.</summary>
    private bool NeedsWeekFetch(DateTime weekStart)
    {
        var today = _today().Date;
        bool covered = weekStart >= today.AddDays(-PullListService.DaysBack) && weekStart.AddDays(6) <= today.AddDays(PullListService.DaysAhead);
        return !covered && !_fetchedWeeks.Contains(weekStart);
    }

    /// <summary>Shows a week; when it lies outside the cached window its releases are fetched (once) and the shelf fills in when they arrive.</summary>
    private async Task GoToWeekAsync(DateTime weekStart)
    {
        _weekFetch?.Cancel();
        _weekFetch = null;
        IsReleasesLoading = false;
        ReleaseLoadFailed = false;
        ReleaseWeekStart = weekStart;
        CalendarMonth = new DateTime(weekStart.AddDays(3).Year, weekStart.AddDays(3).Month, 1);
        ReloadReleases();
        if (!NeedsWeekFetch(weekStart))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _weekFetch = cts;
        IsReleasesLoading = true;
        bool fetched = false;
        try
        {
            fetched = await _fetchReleases(weekStart, weekStart.AddDays(6), cts.Token);
            if (!cts.IsCancellationRequested)
            {
                _fetchedWeeks.Add(weekStart);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (ComicVineException ex)
        {
            ReleaseLoadFailed = true;
            Notify(ex.Message, isError: true);
        }
        finally
        {
            if (ReferenceEquals(_weekFetch, cts))
            {
                _weekFetch = null;
                IsReleasesLoading = false;
            }
        }

        if (!cts.IsCancellationRequested && ReleaseWeekStart == weekStart && (fetched || ReleaseLoadFailed))
        {
            ReloadReleases();
        }
    }

    [RelayCommand] private Task PreviousWeekAsync() => GoToWeekAsync(ReleaseWeekStart.AddDays(-7));

    [RelayCommand] private Task NextWeekAsync() => GoToWeekAsync(ReleaseWeekStart.AddDays(7));

    [RelayCommand] private Task ThisWeekAsync() => GoToWeekAsync(WeekStart(_today()));

    [RelayCommand] private void ToggleCalendar() => IsCalendarOpen = !IsCalendarOpen;

    [RelayCommand]
    private void PreviousMonth()
    {
        CalendarMonth = CalendarMonth.AddMonths(-1);
        BuildCalendar(_today());
    }

    [RelayCommand]
    private void NextMonth()
    {
        CalendarMonth = CalendarMonth.AddMonths(1);
        BuildCalendar(_today());
    }

    /// <summary>Jumps the shelf to the week of a calendar day. Deferred a tick: the day's button sits in the popup this closes and in the grid this rebuilds.</summary>
    [RelayCommand]
    private async Task PickDateAsync(CalendarDayViewModel day)
    {
        await Task.Yield();
        IsCalendarOpen = false;
        await GoToWeekAsync(WeekStart(day.Date));
    }

    /// <summary>The watched series a release belongs to, tracking its Metron series first when the user hasn't yet (its info was cached with the list).</summary>
    private static WatchedSeries TrackForRelease(PaperbunkrDbContext context, PullListRelease release, bool follow)
    {
        var existing = context.WatchedSeries.AsEnumerable().FirstOrDefault(w => PullListService.ListSeriesIdFor(context, w, release.Provider) == release.SeriesId);
        if (existing is not null)
        {
            if (follow && !existing.WatchFutureReleases)
            {
                WantedService.SetWatchFutureReleases(context, existing.Id, true);
            }

            return existing;
        }

        var info = context.ReleaseSeries.Find(release.Provider, release.SeriesId);
        var volume = new ComicVineVolume(release.SeriesId, info?.Name ?? release.SeriesName, info?.Publisher, info?.YearBegan, 0, null);
        int? localSeriesId = context.Series.AsEnumerable().FirstOrDefault(s => SeriesNames.Same(s.Name, volume.Name))?.Id;
        return WantedService.TrackVolume(context, volume, localSeriesId, follow, release.Provider);
    }

    [RelayCommand]
    private void RequestRelease(ReleaseRowViewModel row) => _post(() =>
    {
        string message;
        using (var context = _createContext())
        {
            var release = context.PullListReleases.Find(row.ReleaseId);
            if (release is null)
            {
                return;
            }

            var watched = TrackForRelease(context, release, follow: false);
            var made = WantedService.RequestFromRelease(context, watched, release);
            message = made is null ? $"{row.Title} is already wanted or in your library." : $"Requested {row.Title}.";
        }

        Notify(message, isError: false);
        Refresh();
    });

    [RelayCommand]
    private void FollowReleaseSeries(ReleaseRowViewModel row) => _post(() =>
    {
        string name;
        int requested;
        using (var context = _createContext())
        {
            var release = context.PullListReleases.Find(row.ReleaseId);
            if (release is null)
            {
                return;
            }

            var watched = TrackForRelease(context, release, follow: true);
            name = watched.Name;
            requested = PullListService.PromoteFollowedReleases(context, _today());
        }

        Notify(requested > 0 ? $"Following {name}: requested {requested} upcoming issue{(requested == 1 ? string.Empty : "s")}." : $"Following {name}: new issues will be requested automatically.", isError: false);
        Refresh();
    });

    [RelayCommand]
    private void HideRelease(ReleaseRowViewModel row) => _post(() => SetReleaseHidden(row, hidden: true));

    [RelayCommand]
    private void RestoreRelease(ReleaseRowViewModel row) => _post(() => SetReleaseHidden(row, hidden: false));

    private void SetReleaseHidden(ReleaseRowViewModel row, bool hidden)
    {
        using (var context = _createContext())
        {
            var release = context.PullListReleases.Find(row.ReleaseId);
            if (release is null)
            {
                return;
            }

            release.IsHidden = hidden;
            context.SaveChanges();
        }

        Notify(hidden ? $"Hid {row.Title}. Turn on Show hidden to bring it back." : $"Restored {row.Title}.", isError: false);
        Refresh();
    }
}
