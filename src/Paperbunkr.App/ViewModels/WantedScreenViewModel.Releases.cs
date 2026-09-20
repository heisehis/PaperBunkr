using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.ComicVine;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>One release on the Releases tab (docs/superpowers/specs/2026-09-20-weekly-pull-list-design.md).</summary>
public sealed class ReleaseRowViewModel
{
    public required int ReleaseId { get; init; }
    public required int SeriesId { get; init; }
    public required string Title { get; init; }
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
    public string StatusText => IsHidden ? "Hidden" : IsTaken ? "Wanted" : IsFollowed ? "Following" : string.Empty;
    public bool HasStatus => IsHidden || IsTaken || IsFollowed;
}

/// <summary>A week of releases with its heading ("This week", "Next week", "Week of Oct 7").</summary>
public sealed class ReleaseWeekViewModel
{
    public required string Title { get; init; }
    public required IReadOnlyList<ReleaseRowViewModel> Rows { get; init; }
}

public sealed partial class WantedScreenViewModel
{
    public const string AllPublishers = "All publishers";

    public ObservableCollection<ReleaseWeekViewModel> ReleaseWeeks { get; } = new();

    public bool HasNoReleases => ReleaseWeeks.Count == 0;

    /// <summary>The number of releases shown on the tab (after the filters).</summary>
    [ObservableProperty] private int _releaseCount;

    /// <summary>"All publishers" or one publisher's name; a text value for the suggest box.</summary>
    [ObservableProperty] private string _releasePublisherText = AllPublishers;

    [ObservableProperty] private bool _releasesFollowedOnly;

    /// <summary>Also list the releases the user hid, each with a Restore button.</summary>
    [ObservableProperty] private bool _releasesShowHidden;

    /// <summary>Every publisher seen in the cached list, plus "All publishers" first.</summary>
    [ObservableProperty] private IReadOnlyList<string> _releasePublisherNames = new[] { AllPublishers };

    /// <summary>Why the tab is empty, when it is: no Metron login, nothing fetched yet, or a filter hides everything.</summary>
    [ObservableProperty] private string _releasesEmptyText = string.Empty;

    partial void OnReleasePublisherTextChanged(string value) => ReloadReleases();

    partial void OnReleasesFollowedOnlyChanged(bool value) => ReloadReleases();

    partial void OnReleasesShowHiddenChanged(bool value) => ReloadReleases();

    private void ReloadReleases()
    {
        using var context = _createContext();
        LoadReleases(context, _today());
        OnPropertyChanged(nameof(HasNoReleases));
    }

    private void LoadReleases(PaperbunkrDbContext context, DateTime today)
    {
        var releases = context.PullListReleases.AsNoTracking().OrderBy(r => r.StoreDate).ThenBy(r => r.SeriesName).ThenBy(r => r.IssueNumber).ToList();
        var seriesInfo = context.MetronSeries.AsNoTracking().ToDictionary(m => m.SeriesId);
        var publishers = seriesInfo.Values.Select(m => m.Publisher).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        ReleasePublisherNames = new[] { AllPublishers }.Concat(publishers).ToList();

        // Which Metron series the user follows (a ComicVine series counts through its cross-reference), and which numbers of them are already wanted or owned.
        var followed = new Dictionary<int, WatchedSeries>();
        foreach (var watched in context.WatchedSeries.AsNoTracking().Where(w => w.WatchFutureReleases && !w.IsPaused).ToList())
        {
            if (PullListService.MetronSeriesIdFor(context, watched) is int metronId)
            {
                followed[metronId] = watched;
            }
        }

        var tracked = new Dictionary<int, WatchedSeries>();
        foreach (var watched in context.WatchedSeries.AsNoTracking().ToList())
        {
            if (PullListService.MetronSeriesIdFor(context, watched) is int metronId)
            {
                tracked[metronId] = watched;
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

            rows.Add((release.StoreDate, new ReleaseRowViewModel
            {
                ReleaseId = release.Id,
                SeriesId = release.SeriesId,
                Title = $"{release.SeriesName} #{release.IssueNumber}",
                Subtitle = string.Join(" · ", new[] { info?.Publisher, release.StoreDate.ToString("ddd MMM d", CultureInfo.CurrentCulture) }.Where(s => !string.IsNullOrEmpty(s))),
                Cover = new RemoteCoverSource(release.CoverImageUrl),
                IsFollowed = isFollowed,
                IsTaken = isTaken,
                IsHidden = release.IsHidden,
            }));
        }

        var weeks = rows
            .GroupBy(r => WeekStart(r.Date))
            .OrderBy(g => g.Key)
            .Select(g => new ReleaseWeekViewModel { Title = WeekTitle(g.Key, today), Rows = g.Select(r => r.Row).ToList() })
            .ToList();

        Fill(ReleaseWeeks, weeks);
        ReleaseCount = rows.Count;

        var hasMetron = ComicProviderFactory.IsAvailable(context, ComicProvider.Metron);
        ReleasesEmptyText = releases.Count > 0
            ? "Nothing matches these filters."
            : !hasMetron
                ? "The weekly pull list comes from Metron. Save your Metron login under Preferences → Connections and it fills in on the next check."
                : "Nothing fetched yet. Press Search now, or wait for the next automatic check.";
    }

    private static DateTime WeekStart(DateTime date)
    {
        int offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return date.Date.AddDays(-offset);
    }

    private static string WeekTitle(DateTime weekStart, DateTime today)
    {
        int weeks = (int)((weekStart - WeekStart(today)).TotalDays / 7);
        return weeks switch
        {
            0 => "This week",
            1 => "Next week",
            -1 => "Last week",
            _ => "Week of " + weekStart.ToString("MMM d", CultureInfo.CurrentCulture),
        };
    }

    /// <summary>The watched series a release belongs to, tracking its Metron series first when the user hasn't yet (its info was cached with the list).</summary>
    private static WatchedSeries TrackForRelease(PaperbunkrDbContext context, PullListRelease release, bool follow)
    {
        var existing = context.WatchedSeries.AsEnumerable().FirstOrDefault(w => PullListService.MetronSeriesIdFor(context, w) == release.SeriesId);
        if (existing is not null)
        {
            if (follow && !existing.WatchFutureReleases)
            {
                WantedService.SetWatchFutureReleases(context, existing.Id, true);
            }

            return existing;
        }

        var info = context.MetronSeries.Find(release.SeriesId);
        var volume = new ComicVineVolume(release.SeriesId, info?.Name ?? release.SeriesName, info?.Publisher, info?.YearBegan, 0, null);
        int? localSeriesId = context.Series.AsEnumerable().FirstOrDefault(s => SeriesNames.Same(s.Name, volume.Name))?.Id;
        return WantedService.TrackVolume(context, volume, localSeriesId, follow, ComicProvider.Metron);
    }

    [RelayCommand]
    private void RequestRelease(ReleaseRowViewModel row) => _post(() =>
    {
        using (var context = _createContext())
        {
            var release = context.PullListReleases.Find(row.ReleaseId);
            if (release is null)
            {
                return;
            }

            var watched = TrackForRelease(context, release, follow: false);
            var made = WantedService.RequestFromRelease(context, watched, release);
            SetStatus(made is null ? $"{row.Title} is already wanted or in your library." : $"Requested {row.Title}.", isError: false);
        }

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

        SetStatus(requested > 0 ? $"Following {name}: requested {requested} upcoming issue{(requested == 1 ? string.Empty : "s")}." : $"Following {name}: new issues will be requested automatically.", isError: false);
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

        SetStatus(hidden ? $"Hid {row.Title}. Turn on Show hidden to bring it back." : $"Restored {row.Title}.", isError: false);
        Refresh();
    }

    [RelayCommand] private void GoReleases() => ActiveTab = WantedTab.Releases;
}
