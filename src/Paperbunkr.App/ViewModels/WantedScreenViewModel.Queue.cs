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
using Paperbunkr.Daemon.Events;
using Paperbunkr.Data;
using Paperbunkr.Data.Acquisition;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>The Queue tab (docs/superpowers/specs/2026-09-21-wanted-screen-redesign-design.md 2).</summary>
public sealed partial class WantedScreenViewModel
{
    /// <summary>How many active downloads the strip lists before it says "and n more".</summary>
    private const int DownloadStripSize = 4;

    public static IReadOnlyList<string> QueueSortNames { get; } = new[] { "Attention", "A–Z", "Recently added" };

    private readonly Dictionary<int, QueueIssueViewModel> _issueById = new();
    private readonly Dictionary<int, QueueGroupViewModel> _groupById = new();

    /// <summary>What the user chose for a series group (true = open); a group they never touched follows the default.</summary>
    private readonly Dictionary<int, bool> _groupExpansion = new();

    private readonly Dictionary<int, bool> _issueExpansion = new();

    /// <summary>The flat list the Queue shows: group headers, their issues, and the candidates of expanded issues (or the failed scrapes under "Needs details").</summary>
    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = new();

    /// <summary>The downloads in flight, for the strip above the list (the first few).</summary>
    public ObservableCollection<QueueIssueViewModel> DownloadStrip { get; } = new();

    /// <summary>Imported issues still missing their ComicVine details (docs/superpowers/specs/2026-09-20-cluster-library-manager-into-core-design.md 6.2).</summary>
    public ObservableCollection<ScrapeReviewRowViewModel> ScrapeReviewRows { get; } = new();

    public IReadOnlyList<QueueChipViewModel> QueueChips { get; } = new[]
    {
        new QueueChipViewModel(QueueFilter.All, "All"),
        new QueueChipViewModel(QueueFilter.Wanted, "Wanted"),
        new QueueChipViewModel(QueueFilter.Upcoming, "Upcoming"),
        new QueueChipViewModel(QueueFilter.HasCandidates, "Has candidates"),
        new QueueChipViewModel(QueueFilter.Downloading, "Downloading"),
        new QueueChipViewModel(QueueFilter.Failed, "Failed"),
        new QueueChipViewModel(QueueFilter.NeedsDetails, "Needs details"),
    };

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsNeedsDetailsFilter))]
    private QueueFilter _queueFilter = QueueFilter.All;

    [ObservableProperty] private string _queueSortText = QueueSortNames[0];

    /// <summary>The Queue tab's count: every issue still to find, on its way, or failed.</summary>
    [ObservableProperty] private int _queueCount;

    [ObservableProperty] private string _downloadStripHeading = string.Empty;
    [ObservableProperty] private string _downloadStripMoreText = string.Empty;

    public bool IsNeedsDetailsFilter => QueueFilter == QueueFilter.NeedsDetails;
    public bool HasDownloadStrip => DownloadStrip.Count > 0;
    public bool HasDownloadStripMore => !string.IsNullOrEmpty(DownloadStripMoreText);
    public bool HasScrapeReview => ScrapeReviewRows.Count > 0;

    public string ScrapeReviewHeading => ScrapeReviewRows.Count == 1 ? "1 issue is missing its details" : $"{ScrapeReviewRows.Count} issues are missing their details";

    /// <summary>Nothing at all is queued: the screen's own empty state.</summary>
    public bool HasNoQueue => QueueCount == 0 && ScrapeReviewRows.Count == 0;

    /// <summary>Things are queued but the chip in use matches none of them.</summary>
    public bool HasNoQueueMatches => !HasNoQueue && QueueItems.Count == 0;

    partial void OnQueueFilterChanged(QueueFilter value)
    {
        foreach (var chip in QueueChips)
        {
            chip.IsActive = chip.Filter == value;
        }

        RebuildQueue();
    }

    partial void OnQueueSortTextChanged(string value) => RebuildQueue();

    private QueueSort CurrentSort => QueueSortText switch
    {
        "A–Z" => QueueSort.Alphabetical,
        "Recently added" => QueueSort.RecentlyAdded,
        _ => QueueSort.Attention,
    };

    [RelayCommand]
    private void SetQueueFilter(QueueChipViewModel chip) => QueueFilter = chip.Filter;

    /// <summary>Opens or closes a series group. The header itself stays in the list, so this is safe to run inside its own click.</summary>
    [RelayCommand]
    private void ToggleGroup(QueueGroupViewModel group)
    {
        _groupExpansion[group.WatchedSeriesId] = !group.IsExpanded;
        RebuildQueue();
    }

    /// <summary>Shows or hides an issue's candidates under it.</summary>
    [RelayCommand]
    private void ToggleCandidates(QueueIssueViewModel issue)
    {
        _issueExpansion[issue.Id] = !issue.IsExpanded;
        RebuildQueue();
    }

    /// <summary>Applies the client's live speed and time left to a download row; the percent and stage follow on the next reload.</summary>
    public void ApplyDownloadProgress(DownloadProgressEvent progress)
    {
        if (_issueById.TryGetValue(progress.WantedIssueId, out var issue) && issue.IsDownloading)
        {
            issue.ApplyLiveProgress(progress.Progress * 100, progress.BytesPerSecond, progress.Eta);
        }
    }

    private void LoadQueue(PaperbunkrDbContext context, DateTime today)
    {
        var candidates = context.ReleaseCandidates.AsNoTracking().OrderByDescending(c => c.Score).ToList()
            .GroupBy(c => c.WantedIssueId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CandidateRowViewModel>)g.Select(ToCandidateRow).ToList());

        var wanted = new List<WantedIssue>();
        var stages = new Dictionary<int, QueueStage>();
        void Take(IEnumerable<WantedIssue> issues, Func<WantedIssue, QueueStage> stage)
        {
            foreach (var issue in issues)
            {
                wanted.Add(issue);
                stages[issue.Id] = stage(issue);
            }
        }

        Take(WantedService.Due(context, today).Include(w => w.WatchedSeries).ToList(), _ => QueueStage.Wanted);
        Take(WantedService.Upcoming(context, today).Include(w => w.WatchedSeries).ToList(), _ => QueueStage.Upcoming);
        Take(context.WantedIssues.Include(w => w.WatchedSeries)
                .Where(w => w.Status == WantedIssueStatus.Snatched || w.Status == WantedIssueStatus.Downloading || w.Status == WantedIssueStatus.Failed)
                .ToList(),
            w => w.Status == WantedIssueStatus.Failed ? QueueStage.Failed : QueueStage.Downloading);

        // Issues: a row already on screen takes the fresh values, a new one joins, one that has left is forgotten.
        var seen = new HashSet<int>();
        var series = new Dictionary<int, WatchedSeries>();
        foreach (var w in wanted)
        {
            var fresh = ToIssue(w, stages[w.Id], candidates.TryGetValue(w.Id, out var list) ? list : Array.Empty<CandidateRowViewModel>());
            seen.Add(w.Id);
            series.TryAdd(w.WatchedSeriesId, w.WatchedSeries!);
            if (_issueById.TryGetValue(w.Id, out var existing))
            {
                existing.Apply(fresh);
            }
            else
            {
                _issueById[w.Id] = fresh;
            }
        }

        foreach (var gone in _issueById.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            _issueById.Remove(gone);
            _issueExpansion.Remove(gone);
        }

        // Groups: one per series that has at least one issue queued.
        var byGroup = _issueById.Values.GroupBy(i => i.WatchedSeriesId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var gone in _groupById.Keys.Where(id => !byGroup.ContainsKey(id)).ToList())
        {
            _groupById.Remove(gone);
            _groupExpansion.Remove(gone);
        }

        // A Metron series has no cover of its own; the weekly list's cache may have one.
        var cachedCovers = PullListService.CachedCovers(context, series.Values.Where(w => w.Provider == ComicProvider.Metron).Select(w => w.ExternalVolumeId));

        foreach (var (seriesId, issues) in byGroup)
        {
            if (!_groupById.TryGetValue(seriesId, out var group))
            {
                group = new QueueGroupViewModel { WatchedSeriesId = seriesId };
                _groupById[seriesId] = group;
            }

            var watched = series[seriesId];
            group.Apply(
                watched.Name,
                string.Join(" · ", new[] { watched.Publisher, watched.StartYear?.ToString(CultureInfo.InvariantCulture) }.Where(s => !string.IsNullOrEmpty(s))),
                watched.CoverImageUrl
                    ?? issues.OrderBy(i => QueueIssueViewModel.SortKey(i.IssueNumber)).Select(i => i.CoverUrl).FirstOrDefault(u => !string.IsNullOrEmpty(u))
                    ?? (cachedCovers.TryGetValue(watched.ExternalVolumeId, out var cached) ? cached : null),
                watched.Provider == ComicProvider.Metron);
            group.Issues = issues;
            group.Summary = SummaryFor(issues);
            group.NeedsAttention = issues.Any(i => i.Stage is QueueStage.Failed or QueueStage.Downloading || i.HasCandidateChip);
        }

        LoadScrapeReview(context);
        RebuildQueue();
    }

    private void LoadScrapeReview(PaperbunkrDbContext context)
    {
        var known = ScrapeReviewRows.ToDictionary(r => r.Id);
        var rows = context.WantedIssues.Include(w => w.WatchedSeries)
            .Where(w => w.ScrapeStatus == ScrapeStatus.Failed)
            .OrderBy(w => w.ScrapeLastAttemptAt)
            .ToList()
            .Select(w =>
            {
                var row = new ScrapeReviewRowViewModel
                {
                    Id = w.Id,
                    Title = $"{w.WatchedSeries?.Name} #{w.IssueNumber}",
                    Reason = w.ScrapeError ?? "Couldn't add details.",
                    NeedsYourAction = w.ScrapeFailureIsTerminal,
                    Cover = CoverFor(w),
                };

                // Unchanged rows keep their instance (and so their loaded cover).
                return known.TryGetValue(row.Id, out var same) && same.Reason == row.Reason && same.NeedsYourAction == row.NeedsYourAction ? same : row;
            })
            .ToList();
        SyncList(ScrapeReviewRows, rows);
    }

    /// <summary>Recomputes what the Queue shows from what is loaded: chip counts, the strip, and the flat list (filtered, sorted, groups opened or closed).</summary>
    private void RebuildQueue()
    {
        var issues = _issueById.Values.ToList();
        QueueCount = issues.Count;

        foreach (var chip in QueueChips)
        {
            chip.Count = chip.Filter switch
            {
                QueueFilter.All => issues.Count,
                QueueFilter.NeedsDetails => ScrapeReviewRows.Count,
                var filter => issues.Count(i => Matches(filter, i)),
            };
            chip.IsActive = chip.Filter == QueueFilter;
        }

        var active = issues.Where(i => i.IsDownloading).OrderBy(i => i.SeriesName, StringComparer.OrdinalIgnoreCase).ThenBy(i => QueueIssueViewModel.SortKey(i.IssueNumber)).ToList();
        SyncList(DownloadStrip, active.Take(DownloadStripSize).ToList());
        DownloadStripHeading = active.Count == 1 ? "Downloading 1 issue" : $"Downloading {active.Count} issues";
        DownloadStripMoreText = active.Count > DownloadStripSize ? $"and {active.Count - DownloadStripSize} more" : string.Empty;

        var desired = new List<QueueItemViewModel>();
        if (QueueFilter == QueueFilter.NeedsDetails)
        {
            desired.AddRange(ScrapeReviewRows);
        }
        else
        {
            foreach (var group in OrderedGroups())
            {
                var listed = group.Issues.Where(i => Matches(QueueFilter, i))
                    .OrderBy(i => QueueIssueViewModel.SortKey(i.IssueNumber)).ThenBy(i => i.Id).ToList();
                group.CanBulk = listed.Any(i => i.CanEdit);
                if (listed.Count == 0)
                {
                    continue;
                }

                // A group the user never touched opens when it needs them, and while a chip is narrowing the list to what they came for.
                group.IsExpanded = _groupExpansion.TryGetValue(group.WatchedSeriesId, out bool chosen) ? chosen : group.NeedsAttention || QueueFilter != QueueFilter.All;
                desired.Add(group);
                if (!group.IsExpanded)
                {
                    continue;
                }

                foreach (var issue in listed)
                {
                    issue.IsExpanded = _issueExpansion.TryGetValue(issue.Id, out bool open) && open && issue.Candidates.Count > 0;
                    desired.Add(issue);
                    if (issue.IsExpanded)
                    {
                        desired.AddRange(issue.Candidates);
                    }
                }
            }
        }

        SyncList(QueueItems, desired);

        OnPropertyChanged(nameof(HasDownloadStrip));
        OnPropertyChanged(nameof(HasDownloadStripMore));
        OnPropertyChanged(nameof(HasScrapeReview));
        OnPropertyChanged(nameof(ScrapeReviewHeading));
        OnPropertyChanged(nameof(HasNoQueue));
        OnPropertyChanged(nameof(HasNoQueueMatches));
    }

    private IEnumerable<QueueGroupViewModel> OrderedGroups()
    {
        var groups = _groupById.Values;
        return CurrentSort switch
        {
            QueueSort.Alphabetical => groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            QueueSort.RecentlyAdded => groups.OrderByDescending(g => g.NewestCreatedAt).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
            _ => groups.OrderBy(AttentionRank).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
        };
    }

    /// <summary>Failures first, then candidates waiting for a decision, then downloads, then the rest.</summary>
    private static int AttentionRank(QueueGroupViewModel group) =>
        group.Issues.Any(i => i.IsFailed) ? 0
        : group.Issues.Any(i => i.HasCandidateChip) ? 1
        : group.Issues.Any(i => i.IsDownloading) ? 2
        : 3;

    private static bool Matches(QueueFilter filter, QueueIssueViewModel issue) => filter switch
    {
        QueueFilter.Wanted => issue.Stage == QueueStage.Wanted,
        QueueFilter.Upcoming => issue.Stage == QueueStage.Upcoming,
        QueueFilter.HasCandidates => issue.HasCandidateChip,
        QueueFilter.Downloading => issue.Stage == QueueStage.Downloading,
        QueueFilter.Failed => issue.Stage == QueueStage.Failed,
        QueueFilter.NeedsDetails => false,
        _ => true,
    };

    private static string SummaryFor(IReadOnlyCollection<QueueIssueViewModel> issues)
    {
        var parts = new List<string>();
        void Add(int count, string label)
        {
            if (count > 0)
            {
                parts.Add($"{count} {label}");
            }
        }

        Add(issues.Count(i => i.Stage == QueueStage.Wanted), "wanted");
        Add(issues.Count(i => i.HasCandidateChip), "with candidates");
        Add(issues.Count(i => i.Stage == QueueStage.Downloading), "downloading");
        Add(issues.Count(i => i.Stage == QueueStage.Failed), "failed");
        Add(issues.Count(i => i.Stage == QueueStage.Upcoming), "upcoming");
        return string.Join(" · ", parts);
    }

    // --- Rows an issue's own buttons act on. Each removes or restructures the list its button sits in, so it runs a tick after the click (project runtime gotcha). ---

    /// <summary>"I have this": stops wanting it without ever searching.</summary>
    [RelayCommand]
    private void Ignore(QueueIssueViewModel issue) => _post(() =>
    {
        using var context = _createContext();
        var wanted = context.WantedIssues.FirstOrDefault(w => w.Id == issue.Id);
        if (wanted is not null)
        {
            wanted.Status = WantedIssueStatus.Ignored;
            context.SaveChanges();
        }

        Refresh();
    });

    /// <summary>Stops wanting an issue; it goes back to the series' "Missing" list.</summary>
    [RelayCommand]
    private void Remove(QueueIssueViewModel issue) => _post(() =>
    {
        using var context = _createContext();
        var wanted = context.WantedIssues.FirstOrDefault(w => w.Id == issue.Id);
        if (wanted is not null)
        {
            context.WantedIssues.Remove(wanted);
            context.SaveChanges();
        }

        Refresh();
    });

    /// <summary>"I have all" for the issues a group lists.</summary>
    [RelayCommand]
    private Task IgnoreGroupAsync(QueueGroupViewModel group) => BulkAsync(group, ignore: true);

    /// <summary>"Remove all" for the issues a group lists.</summary>
    [RelayCommand]
    private Task RemoveGroupAsync(QueueGroupViewModel group) => BulkAsync(group, ignore: false);

    private async Task BulkAsync(QueueGroupViewModel group, bool ignore)
    {
        var ids = group.Issues.Where(i => i.CanEdit && Matches(QueueFilter, i)).Select(i => i.Id).ToList();
        if (ids.Count == 0)
        {
            return;
        }

        string noun = ids.Count == 1 ? "1 issue" : $"{ids.Count} issues";
        bool proceed = await _confirm(
            ignore ? $"Mark {noun} of {group.Name} as already in your library? Paperbunkr will stop looking for them." : $"Stop wanting {noun} of {group.Name}? They go back to the series' missing list.",
            ignore ? "I have all" : "Remove all");
        if (!proceed)
        {
            return;
        }

        _post(() =>
        {
            using (var context = _createContext())
            {
                var wanted = context.WantedIssues.Where(w => ids.Contains(w.Id)).ToList();
                if (ignore)
                {
                    foreach (var w in wanted)
                    {
                        w.Status = WantedIssueStatus.Ignored;
                    }
                }
                else
                {
                    context.WantedIssues.RemoveRange(wanted);
                }

                context.SaveChanges();
            }

            Refresh();
            Notify(ignore ? $"Marked {noun} of {group.Name} as already yours." : $"Stopped wanting {noun} of {group.Name}.", isError: false);
        });
    }

    /// <summary>Approves a candidate: sends it to qBittorrent. Progress and the import then follow on their own.</summary>
    [RelayCommand]
    private async Task GrabAsync(CandidateRowViewModel candidate, CancellationToken cancellationToken)
    {
        var result = await _grab.GrabAsync(candidate.Id, automatic: false, cancellationToken);
        Notify(result.Message, isError: !result.Success);
        Refresh();
    }

    /// <summary>Rejects a candidate: it is blocklisted and never offered again.</summary>
    [RelayCommand]
    private void RejectCandidate(CandidateRowViewModel candidate) => _post(() =>
    {
        _grab.Reject(candidate.Id);
        Refresh();
    });

    /// <summary>Puts a failed issue back to Wanted for another search.</summary>
    [RelayCommand]
    private void RetryDownload(QueueIssueViewModel issue) => _post(() =>
    {
        _grab.Retry(issue.Id);
        Refresh();
    });

    /// <summary>Stops a grab (removing its torrent, only ever one in the Paperbunkr category) and returns the issue to Wanted.</summary>
    [RelayCommand]
    private async Task CancelDownloadAsync(QueueIssueViewModel issue, CancellationToken cancellationToken)
    {
        var result = await _grab.CancelAsync(issue.Id, deleteFiles: true, cancellationToken);
        Notify(result.Message, isError: !result.Success);
        Refresh();
    }

    [RelayCommand]
    private async Task CopyLinkAsync(CandidateRowViewModel candidate)
    {
        await _copyToClipboard(candidate.DownloadUrl);
        Notify("Link copied. Paste it into your download client.", isError: false);
    }

    // --- Imported issues missing their ComicVine details ---

    /// <summary>Tries a failed scrape again now.</summary>
    [RelayCommand]
    private void RetryScrape(ScrapeReviewRowViewModel row) => _post(() =>
    {
        using (var context = _createContext())
        {
            Paperbunkr.Daemon.Services.ScrapeSweeper.Requeue(context, row.Id);
        }

        Refresh();
    });

    /// <summary>Gives up on one issue's ComicVine details; the issue stays in the library as it is.</summary>
    [RelayCommand]
    private void DismissScrape(ScrapeReviewRowViewModel row) => _post(() =>
    {
        using (var context = _createContext())
        {
            Paperbunkr.Daemon.Services.ScrapeSweeper.Dismiss(context, row.Id);
        }

        Refresh();
    });

    /// <summary>One click for a whole failed batch (a bad key fixed, ComicVine back up): every failed issue goes back in the queue.</summary>
    [RelayCommand]
    private void RetryAllScrapes() => _post(() =>
    {
        int count;
        using (var context = _createContext())
        {
            var ids = context.WantedIssues.Where(w => w.ScrapeStatus == ScrapeStatus.Failed).Select(w => w.Id).ToList();
            count = ids.Count(id => Paperbunkr.Daemon.Services.ScrapeSweeper.Requeue(context, id));
        }

        Notify(count == 0 ? string.Empty : $"Trying again for {count} issue{(count == 1 ? string.Empty : "s")}.", isError: false);
        Refresh();
    });

    [RelayCommand]
    private void DismissAllScrapes() => _post(() =>
    {
        using (var context = _createContext())
        {
            foreach (var id in context.WantedIssues.Where(w => w.ScrapeStatus == ScrapeStatus.Failed).Select(w => w.Id).ToList())
            {
                Paperbunkr.Daemon.Services.ScrapeSweeper.Dismiss(context, id);
            }
        }

        Refresh();
    });

    // --- Building rows ---

    private static QueueIssueViewModel ToIssue(WantedIssue wanted, QueueStage stage, IReadOnlyList<CandidateRowViewModel> candidates)
    {
        var issue = new QueueIssueViewModel
        {
            Id = wanted.Id,
            WatchedSeriesId = wanted.WatchedSeriesId,
            SeriesName = wanted.WatchedSeries?.Name ?? string.Empty,
            IssueNumber = wanted.IssueNumber,
            Stage = stage,
            IsMetron = wanted.Provider == ComicProvider.Metron,
            CandidateCount = stage == QueueStage.Wanted ? candidates.Count : 0,
            ProgressPercent = (wanted.DownloadProgress ?? 0) * 100,
        };
        issue.SetInitial(wanted.CoverImageUrl ?? wanted.WatchedSeries?.CoverImageUrl, stage == QueueStage.Wanted ? candidates : Array.Empty<CandidateRowViewModel>(), wanted.CreatedAt, wanted.StoreDate);

        switch (stage)
        {
            case QueueStage.Upcoming:
                issue.Subtitle = wanted.StoreDate is DateTime due ? $"Arrives {due.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}" : wanted.Name;
                issue.StatusText = "Upcoming";
                break;
            case QueueStage.Wanted:
                issue.Subtitle = wanted.Name;
                issue.StatusText = wanted.LastSearchedAt is null ? "Not searched yet" : "Wanted";
                break;
            case QueueStage.Failed:
                issue.Detail = wanted.FailureReason;
                issue.StatusText = "Failed";
                break;
            default:
                issue.Detail = wanted.GrabbedTitle;
                issue.StatusText = wanted.Status == WantedIssueStatus.Snatched ? "Sent to qBittorrent" : wanted.DownloadProgress is >= 0.9999 ? "Importing…" : "Downloading";
                break;
        }

        return issue;
    }

    private static RemoteCoverSource CoverFor(WantedIssue wanted) => new(wanted.CoverImageUrl ?? wanted.WatchedSeries?.CoverImageUrl);

    private static CandidateRowViewModel ToCandidateRow(ReleaseCandidate candidate) => new()
    {
        Id = candidate.Id,
        WantedIssueId = candidate.WantedIssueId,
        Title = candidate.Title,
        Detail = string.Join(" · ", new[]
        {
            FormatSize(candidate.SizeBytes),
            $"{candidate.Seeders} seeders",
            candidate.Indexer,
        }.Where(s => !string.IsNullOrEmpty(s))),
        ScoreText = candidate.Score.ToString("0", CultureInfo.InvariantCulture),
        IsPack = candidate.IsPack,
        DownloadUrl = candidate.DownloadUrl,
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        <= 0 => string.Empty,
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB",
    };
}
