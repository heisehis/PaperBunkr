using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Timeline (age-progression) mode of the Story Events screen (docs/superpowers/specs/2026-08-27-
/// metadata-model-phase4g-age-progression-design.md, plus its deferred per-Continuity / whole-
/// library scopes, character-aware family scoping, and the bulk "review inferred ages" surface).
/// Read-only for reading-order/browsing; the one write path is Accepting an inferred age, which
/// writes <c>Issue.BookAge</c> via <see cref="BookAgeReviewResolver"/>.
/// </summary>
public partial class EventsScreenViewModel
{
    private int? _timelineSeedSeriesId;
    private int? _timelineContinuityId;
    private List<int> _timelineSeriesIds = new();

    /// <summary>One labeled section per <see cref="ComicAge"/> present in the scoped set, ages with zero issues skipped.</summary>
    public ObservableCollection<TimelineSectionViewModel> TimelineSections { get; }

    public ObservableCollection<SeriesSearchResult> TimelineSeriesSearchResults { get; }

    /// <summary>Issues in the current scope whose age is only inferred - the "review inferred ages" queue.</summary>
    public ObservableCollection<InferredAgeRowViewModel> InferredAges { get; }

    /// <summary>Continuity names for the Continuity-scope picker.</summary>
    public ObservableCollection<ContinuitySummary> TimelineContinuityChoices { get; } = new();

    [ObservableProperty]
    private TimelineScope _timelineScope = TimelineScope.SeriesFamily;

    [ObservableProperty]
    private string _timelineSeriesSearchQuery = string.Empty;

    [ObservableProperty]
    private string _timelineTitle = string.Empty;

    /// <summary>Character-aware family expansion (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-age-progression-design.md) - only meaningful in <see cref="TimelineScope.SeriesFamily"/>.</summary>
    [ObservableProperty]
    private bool _timelineCharacterAware;

    [ObservableProperty]
    private bool _inferredAgesExpanded;

    [ObservableProperty]
    private ContinuitySummary? _selectedTimelineContinuity;

    partial void OnSelectedTimelineContinuityChanged(ContinuitySummary? value)
    {
        if (value is not null)
        {
            LoadContinuityTimeline(value.Id);
        }
    }

    public bool IsSeriesFamilyScope => TimelineScope == TimelineScope.SeriesFamily;
    public bool IsContinuityScope => TimelineScope == TimelineScope.Continuity;
    public bool IsLibraryScope => TimelineScope == TimelineScope.Library;

    public bool HasTimelineSeed => _timelineSeriesIds.Count > 0 || TimelineSections.Count > 0;

    public bool HasNoTimelineSections => HasTimelineSeed && TimelineSections.Count == 0;

    public bool HasNoInferredAges => InferredAges.Count == 0;

    public void RefreshTimelineSeriesSidebar()
    {
        TimelineSeriesSearchQuery = string.Empty;
        TimelineSeriesSearchResults.Clear();

        using var context = PaperbunkrDb.CreateContext();
        TimelineContinuityChoices.Clear();
        foreach (var c in context.Continuities.Include(c => c.Memberships).OrderBy(c => c.Name).ToList())
        {
            TimelineContinuityChoices.Add(new ContinuitySummary(c.Id, c.Name, c.Publisher, c.Memberships.Count));
        }
    }

    partial void OnTimelineScopeChanged(TimelineScope value)
    {
        OnPropertyChanged(nameof(IsSeriesFamilyScope));
        OnPropertyChanged(nameof(IsContinuityScope));
        OnPropertyChanged(nameof(IsLibraryScope));

        // Library scope needs no picking - lay it out immediately.
        if (value == TimelineScope.Library)
        {
            LoadLibraryTimeline();
        }
    }

    partial void OnTimelineCharacterAwareChanged(bool value)
    {
        if (_timelineSeedSeriesId is int seed && TimelineScope == TimelineScope.SeriesFamily)
        {
            LoadTimeline(seed);
        }
    }

    [RelayCommand]
    private void SetTimelineScope(TimelineScope scope) => TimelineScope = scope;

    partial void OnTimelineSeriesSearchQueryChanged(string value) => SearchTimelineSeries();

    [RelayCommand]
    private void SearchTimelineSeries()
    {
        TimelineSeriesSearchResults.Clear();
        string query = TimelineSeriesSearchQuery.Trim();
        if (query.Length == 0)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var matches = context.Series
            .AsEnumerable()
            .Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(20);

        foreach (var series in matches)
        {
            TimelineSeriesSearchResults.Add(new SeriesSearchResult { SeriesId = series.Id, Name = series.Name });
        }
    }

    [RelayCommand]
    private void SelectTimelineSeries(SeriesSearchResult? result)
    {
        if (result is not null)
        {
            LoadTimeline(result.SeriesId);
        }
    }

    [RelayCommand]
    private void SelectTimelineContinuity(ContinuitySummary? summary)
    {
        if (summary is not null)
        {
            LoadContinuityTimeline(summary.Id);
        }
    }

    public void LoadTimeline(int seedSeriesId)
    {
        _timelineSeedSeriesId = seedSeriesId;
        _timelineContinuityId = null;
        TimelineScope = TimelineScope.SeriesFamily;

        using var context = PaperbunkrDb.CreateContext();
        var seed = context.Series.FirstOrDefault(s => s.Id == seedSeriesId);
        var family = SeriesFamilyResolver.GetFamily(context, seedSeriesId, TimelineCharacterAware);
        BuildTimeline(context, family, $"Timeline · {seed?.Name ?? "series"} family");
    }

    public void LoadContinuityTimeline(int continuityId)
    {
        _timelineContinuityId = continuityId;
        _timelineSeedSeriesId = null;
        TimelineScope = TimelineScope.Continuity;

        using var context = PaperbunkrDb.CreateContext();
        var continuity = context.Continuities.FirstOrDefault(c => c.Id == continuityId);
        var series = ContinuityResolver.GetSeriesInContinuity(context, continuityId);
        var withIssues = context.Series.Include(s => s.Issues).Where(s => series.Select(x => x.Id).Contains(s.Id)).ToList();
        BuildTimeline(context, withIssues, $"Timeline · {continuity?.Name ?? "continuity"}");
    }

    public void LoadLibraryTimeline()
    {
        _timelineSeedSeriesId = null;
        _timelineContinuityId = null;

        using var context = PaperbunkrDb.CreateContext();
        var all = context.Series.Include(s => s.Issues).ToList();
        BuildTimeline(context, all, "Timeline · whole library");
    }

    /// <summary>Redesign (docs/superpowers/specs/2026-08-28-events-continuity-screen-redesign-design.md):
    /// Timeline scoped to a single story event's member issues, reached via the detail-pane toggle.</summary>
    public void LoadEventTimeline(int storyEventId)
    {
        _timelineSeedSeriesId = null;
        _timelineContinuityId = null;

        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = context.StoryEvents.FirstOrDefault(e => e.Id == storyEventId);
        var issues = EventMembershipResolver.GetOrderedMembers(context, storyEventId)
            .Where(m => m.Issue is not null)
            .Select(m => m.Issue!)
            .ToList();

        TimelineTitle = $"Timeline · {storyEvent?.Name ?? "event"}";
        _timelineSeriesIds = issues.Select(i => i.SeriesId).Distinct().ToList();

        PopulateTimelineSections(issues.Select(i => (i, i.Series?.Name ?? "Unknown")));

        InferredAges.Clear();
        foreach (var row in BookAgeReviewResolver.GetInferred(context, _timelineSeriesIds))
        {
            InferredAges.Add(new InferredAgeRowViewModel(row, AcceptInferredAge));
        }

        OnPropertyChanged(nameof(HasTimelineSeed));
        OnPropertyChanged(nameof(HasNoTimelineSections));
        OnPropertyChanged(nameof(HasNoInferredAges));
    }

    /// <summary>Shared bucketing: (issue, series-name) pairs → age-bucketed <see cref="TimelineSections"/>.</summary>
    private void PopulateTimelineSections(IEnumerable<(Issue Issue, string SeriesName)> entries)
    {
        var buckets = new Dictionary<ComicAge, List<(Issue Issue, string SeriesName, decimal Confidence, string? Reason)>>();
        foreach (var (issue, seriesName) in entries)
        {
            var (age, confidence, reason) = BookAgeResolver.Resolve(issue);
            if (age is not ComicAge resolvedAge)
            {
                continue;
            }

            if (!buckets.TryGetValue(resolvedAge, out var list))
            {
                buckets[resolvedAge] = list = new();
            }

            list.Add((issue, seriesName, confidence, reason));
        }

        TimelineSections.Clear();
        foreach (ComicAge age in Enum.GetValues<ComicAge>())
        {
            if (!buckets.TryGetValue(age, out var list))
            {
                continue;
            }

            var info = ComicAgeCatalog.All[age];
            var section = new TimelineSectionViewModel { Label = info.DisplayName, CommonlyCitedRange = info.CommonlyCitedRange };

            foreach (var entry in list
                .OrderBy(e => e.Issue.Year ?? int.MaxValue)
                .ThenBy(e => e.Issue.Month ?? 0)
                .ThenBy(e => e.Issue.Day ?? 0)
                .ThenBy(e => e.SeriesName))
            {
                section.Issues.Add(new TimelineIssueCard
                {
                    IssueId = entry.Issue.Id,
                    Title = string.IsNullOrWhiteSpace(entry.Issue.EffectiveNumber()) ? "#?" : $"#{entry.Issue.EffectiveNumber()}",
                    SeriesName = entry.SeriesName,
                    IsUnread = entry.Issue.OpenCount == 0,
                    IsReducedConfidence = entry.Confidence is > 0m and < 1.0m,
                    ConfidenceReason = entry.Reason,
                    CoverBrush = SeriesCardSample.CoverBrushFor(entry.SeriesName),
                    CoverImage = CoverImageCache.Get(entry.Issue.Id, entry.Issue.FilePath, entry.Issue.FileSize),
                    YearLabel = entry.Issue.Year?.ToString(),
                });
            }

            TimelineSections.Add(section);
        }
    }

    private void BuildTimeline(PaperbunkrDbContext context, IReadOnlyList<Series> series, string title)
    {
        TimelineTitle = title;
        _timelineSeriesIds = series.Select(s => s.Id).ToList();

        var buckets = new Dictionary<ComicAge, List<(Issue Issue, string SeriesName, decimal Confidence, string? Reason)>>();
        foreach (var s in series)
        {
            foreach (var issue in s.Issues)
            {
                var (age, confidence, reason) = BookAgeResolver.Resolve(issue);
                if (age is not ComicAge resolvedAge)
                {
                    continue;
                }

                if (!buckets.TryGetValue(resolvedAge, out var list))
                {
                    buckets[resolvedAge] = list = new();
                }

                list.Add((issue, s.Name, confidence, reason));
            }
        }

        TimelineSections.Clear();
        foreach (ComicAge age in Enum.GetValues<ComicAge>())
        {
            if (!buckets.TryGetValue(age, out var list))
            {
                continue;
            }

            var info = ComicAgeCatalog.All[age];
            var section = new TimelineSectionViewModel { Label = info.DisplayName, CommonlyCitedRange = info.CommonlyCitedRange };

            foreach (var entry in list
                .OrderBy(e => e.Issue.Year ?? int.MaxValue)
                .ThenBy(e => e.Issue.Month ?? 0)
                .ThenBy(e => e.Issue.Day ?? 0)
                .ThenBy(e => e.SeriesName))
            {
                section.Issues.Add(new TimelineIssueCard
                {
                    IssueId = entry.Issue.Id,
                    Title = string.IsNullOrWhiteSpace(entry.Issue.EffectiveNumber()) ? "#?" : $"#{entry.Issue.EffectiveNumber()}",
                    SeriesName = entry.SeriesName,
                    IsUnread = entry.Issue.OpenCount == 0,
                    IsReducedConfidence = entry.Confidence is > 0m and < 1.0m,
                    ConfidenceReason = entry.Reason,
                    CoverBrush = SeriesCardSample.CoverBrushFor(entry.SeriesName),
                    CoverImage = CoverImageCache.Get(entry.Issue.Id, entry.Issue.FilePath, entry.Issue.FileSize),
                    YearLabel = entry.Issue.Year?.ToString(),
                });
            }

            TimelineSections.Add(section);
        }

        InferredAges.Clear();
        foreach (var row in BookAgeReviewResolver.GetInferred(context, _timelineSeriesIds))
        {
            InferredAges.Add(new InferredAgeRowViewModel(row, AcceptInferredAge));
        }

        OnPropertyChanged(nameof(HasTimelineSeed));
        OnPropertyChanged(nameof(HasNoTimelineSections));
        OnPropertyChanged(nameof(HasNoInferredAges));
    }

    private void ReloadCurrentTimeline()
    {
        if (_timelineSeedSeriesId is int seed)
        {
            LoadTimeline(seed);
        }
        else if (_timelineContinuityId is int continuityId)
        {
            LoadContinuityTimeline(continuityId);
        }
        else if (TimelineScope == TimelineScope.Library)
        {
            LoadLibraryTimeline();
        }
    }

    private void AcceptInferredAge(InferredAgeRowViewModel row)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            BookAgeReviewResolver.Accept(context, row.IssueId, row.Age);
        }

        ReloadCurrentTimeline();
    }

    [RelayCommand]
    private void AcceptAllInferredAges()
    {
        var rows = InferredAges.ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var row in rows)
            {
                BookAgeReviewResolver.Accept(context, row.IssueId, row.Age);
            }
        }

        ReloadCurrentTimeline();
    }

    [RelayCommand]
    private void ToggleInferredAges() => InferredAgesExpanded = !InferredAgesExpanded;

    /// <summary>Clicking any issue opens it in the reader, same as every other issue-grid instance in this app.</summary>
    [RelayCommand]
    private void OpenTimelineIssue(TimelineIssueCard? card)
    {
        if (card is not null)
        {
            _goToReader(card.IssueId);
        }
    }
}
