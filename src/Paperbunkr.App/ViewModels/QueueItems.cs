using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Paperbunkr.App.ViewModels;

/// <summary>Where an issue is in the acquisition pipeline; what the Queue's stage chips filter on.</summary>
public enum QueueStage
{
    Wanted,
    Upcoming,
    Downloading,
    Failed,
}

public enum QueueFilter
{
    All,
    Wanted,
    Upcoming,
    HasCandidates,
    Downloading,
    Failed,
    NeedsDetails,
}

public enum QueueSort
{
    Attention,
    Alphabetical,
    RecentlyAdded,
}

/// <summary>
/// A row of the Queue's flat, virtualized list (docs/superpowers/specs/2026-09-21-wanted-screen-redesign-design.md 2). The key is stable across refreshes so the list
/// can be reconciled in place instead of reloaded: an instance is reused for as long as its key exists, which is what keeps scroll position and expansion steady while
/// downloads tick along.
/// </summary>
public abstract class QueueItemViewModel : ObservableObject
{
    public abstract string Key { get; }
}

/// <summary>One release found for a wanted issue.</summary>
public sealed class CandidateRowViewModel : QueueItemViewModel
{
    public required int Id { get; init; }
    public required int WantedIssueId { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required string ScoreText { get; init; }
    public bool IsPack { get; init; }
    public required string DownloadUrl { get; init; }
    public override string Key => "c" + Id;
}

/// <summary>An imported issue whose ComicVine details could not be added (a failed scrape-on-import); the row stays until the user retries or dismisses it.</summary>
public sealed class ScrapeReviewRowViewModel : QueueItemViewModel
{
    public required int Id { get; init; }
    public required string Title { get; init; }
    public required string Reason { get; init; }

    /// <summary>Retrying without the user changing something is pointless (no such issue upstream, no key).</summary>
    public bool NeedsYourAction { get; init; }
    public string Hint => NeedsYourAction ? "Needs your attention" : "Paperbunkr will retry on its own";
    public RemoteCoverSource Cover { get; init; } = new(null);
    public override string Key => "s" + Id;
}

/// <summary>A series header in the Queue: its cover, how many issues are at each stage, and the bulk actions.</summary>
public sealed partial class QueueGroupViewModel : QueueItemViewModel
{
    public required int WatchedSeriesId { get; init; }
    public override string Key => "g" + WatchedSeriesId;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _subtitle;
    [ObservableProperty] private RemoteCoverSource _cover = new(null);
    [ObservableProperty] private bool _isMetron;

    /// <summary>"7 wanted · 2 candidates · 1 downloading".</summary>
    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>Something here needs the user: a failure, candidates to review, or a download in flight.</summary>
    [ObservableProperty] private bool _needsAttention;

    [ObservableProperty] private bool _isExpanded;

    /// <summary>The issues the current filter lists under this header that a bulk action would touch (not downloading ones).</summary>
    [ObservableProperty] private bool _canBulk;

    private string? _coverUrl;

    /// <summary>Every issue of the series in the queue, whatever the filter says.</summary>
    internal List<QueueIssueViewModel> Issues { get; set; } = new();

    internal DateTime NewestCreatedAt => Issues.Count == 0 ? DateTime.MinValue : Issues.Max(i => i.CreatedAt);

    internal void Apply(string name, string? subtitle, string? coverUrl, bool isMetron)
    {
        Name = name;
        Subtitle = subtitle;
        IsMetron = isMetron;
        if (!string.Equals(_coverUrl, coverUrl, StringComparison.Ordinal))
        {
            _coverUrl = coverUrl;
            Cover = new RemoteCoverSource(coverUrl);
        }
    }
}

/// <summary>One wanted, upcoming, downloading or failed issue.</summary>
public sealed partial class QueueIssueViewModel : QueueItemViewModel
{
    public required int Id { get; init; }
    public required int WatchedSeriesId { get; init; }
    public override string Key => "i" + Id;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title), nameof(IssueLabel))]
    private string _seriesName = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Title), nameof(IssueLabel))]
    private string _issueNumber = string.Empty;

    [ObservableProperty] private string? _subtitle;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsFailed), nameof(IsDownloading), nameof(ShowProgress), nameof(CanEdit), nameof(HasCandidateChip))]
    private QueueStage _stage;

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isMetron;
    [ObservableProperty] private RemoteCoverSource _cover = new(null);

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CandidateText), nameof(HasCandidateChip))]
    private int _candidateCount;

    /// <summary>Failure reason, or the release being downloaded.</summary>
    [ObservableProperty] private string? _detail;

    /// <summary>0..100 for the progress bar.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProgressText))]
    private double _progressPercent;

    /// <summary>The candidates are shown under this row.</summary>
    [ObservableProperty] private bool _isExpanded;

    private string? _coverUrl;
    private long _bytesPerSecond;
    private TimeSpan? _eta;

    public DateTime CreatedAt { get; private set; }
    public DateTime? StoreDate { get; private set; }

    /// <summary>This issue's own cover, or its series' when it has none; a group borrows it when its series has no cover of its own.</summary>
    internal string? CoverUrl => _coverUrl;

    /// <summary>Best first.</summary>
    public IReadOnlyList<CandidateRowViewModel> Candidates { get; private set; } = Array.Empty<CandidateRowViewModel>();

    public string Title => $"{SeriesName} #{IssueNumber}";
    public string IssueLabel => $"#{IssueNumber}";
    public bool IsFailed => Stage == QueueStage.Failed;
    public bool IsDownloading => Stage == QueueStage.Downloading;
    public bool ShowProgress => IsDownloading;

    /// <summary>Only issues that are still just wanted can be marked owned or dropped; a grab in flight is cancelled instead.</summary>
    public bool CanEdit => Stage is QueueStage.Wanted or QueueStage.Upcoming;

    public bool HasCandidateChip => Stage == QueueStage.Wanted && CandidateCount > 0;
    public string CandidateText => CandidateCount == 1 ? "1 candidate" : $"{CandidateCount} candidates";

    /// <summary>"38% · 2.1 MB/s · 4m": the percent is stored; speed and time left only exist while the client is reporting them.</summary>
    public string ProgressText
    {
        get
        {
            var parts = new List<string> { $"{ProgressPercent:0}%" };
            if (_bytesPerSecond > 0)
            {
                parts.Add(FormatSpeed(_bytesPerSecond));
            }

            if (_eta is TimeSpan eta)
            {
                parts.Add(FormatEta(eta));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Copies a freshly built snapshot onto this long-lived row, keeping what only this instance knows (expansion, live speed) and reusing candidate rows.</summary>
    internal void Apply(QueueIssueViewModel fresh)
    {
        SeriesName = fresh.SeriesName;
        IssueNumber = fresh.IssueNumber;
        Subtitle = fresh.Subtitle;
        StatusText = fresh.StatusText;
        IsMetron = fresh.IsMetron;
        Detail = fresh.Detail;
        CreatedAt = fresh.CreatedAt;
        StoreDate = fresh.StoreDate;
        CandidateCount = fresh.CandidateCount;

        var known = Candidates.ToDictionary(c => c.Id);
        Candidates = fresh.Candidates.Select(c => known.TryGetValue(c.Id, out var same) ? same : c).ToList();

        if (!string.Equals(_coverUrl, fresh._coverUrl, StringComparison.Ordinal))
        {
            _coverUrl = fresh._coverUrl;
            Cover = fresh.Cover;
        }

        if (fresh.Stage != Stage)
        {
            _bytesPerSecond = 0;
            _eta = null;
        }

        Stage = fresh.Stage;
        ProgressPercent = fresh.ProgressPercent;
        OnPropertyChanged(nameof(ProgressText));
    }

    internal void SetInitial(string? coverUrl, IReadOnlyList<CandidateRowViewModel> candidates, DateTime createdAt, DateTime? storeDate)
    {
        _coverUrl = coverUrl;
        Cover = new RemoteCoverSource(coverUrl);
        Candidates = candidates;
        CreatedAt = createdAt;
        StoreDate = storeDate;
    }

    /// <summary>The client's live numbers, which the database never stores.</summary>
    internal void ApplyLiveProgress(double percent, long bytesPerSecond, TimeSpan? eta)
    {
        _bytesPerSecond = bytesPerSecond;
        _eta = eta;
        ProgressPercent = percent;
        OnPropertyChanged(nameof(ProgressText));
    }

    internal static string FormatSpeed(long bytesPerSecond) => bytesPerSecond switch
    {
        < 1024L * 1024 => $"{bytesPerSecond / 1024.0:0} KB/s",
        _ => $"{bytesPerSecond / (1024.0 * 1024):0.0} MB/s",
    };

    internal static string FormatEta(TimeSpan eta) => eta.TotalHours >= 1
        ? $"{(int)eta.TotalHours}h {eta.Minutes}m"
        : eta.TotalMinutes >= 1 ? $"{(int)Math.Ceiling(eta.TotalMinutes)}m" : $"{Math.Max(1, (int)eta.TotalSeconds)}s";

    /// <summary>Series order: by issue number where it is one (16 before 105), text otherwise.</summary>
    internal static (double Number, string Text) SortKey(string issueNumber) =>
        double.TryParse(issueNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? (n, issueNumber) : (double.MaxValue, issueNumber);
}

/// <summary>A stage filter chip with its live count.</summary>
public sealed partial class QueueChipViewModel : ObservableObject
{
    public QueueChipViewModel(QueueFilter filter, string label)
    {
        Filter = filter;
        Label = label;
    }

    public QueueFilter Filter { get; }
    public string Label { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsVisible), nameof(Text))]
    private int _count;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsVisible))]
    private bool _isActive;

    /// <summary>Empty stages stay out of the way; All and the chip in use are always there.</summary>
    public bool IsVisible => Filter == QueueFilter.All || IsActive || Count > 0;

    public string Text => $"{Label} {Count}";
}
