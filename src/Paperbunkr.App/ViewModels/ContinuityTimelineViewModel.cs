using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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

/// <summary>One slot of the timeline's year histogram. Empty years keep a slot so the axis stays true to time.</summary>
public sealed class TimelineYearBar
{
    public required int Year { get; init; }

    public required int Count { get; init; }

    /// <summary>Bar height as a share of the busiest year.</summary>
    public required double Fraction { get; init; }

    public bool IsEmpty => Count == 0;

    public string Tooltip => $"{Year.ToString(CultureInfo.InvariantCulture)} · {(Count == 1 ? "1 issue" : $"{Count:N0} issues")}";

    /// <summary>Bar width: wider the fewer years the histogram covers, so a short run doesn't shrink to a few slivers.</summary>
    public double BarWidth { get; init; } = 7;

    /// <summary>The year under this bar, or null between labels (every year on a short axis, every 5th or 10th on longer ones).</summary>
    public string? AxisLabel { get; init; }

    public double BarHeight => IsEmpty ? 0 : Math.Max(2, Fraction * ContinuityTimelineViewModel.HistogramHeight);
}

/// <summary>
/// The Timeline tab of a continuity or an event (docs/superpowers/specs/2026-08-27-metadata-model-phase4g-age-progression-design.md, moved
/// out of the old screen by docs/superpowers/specs/2026-09-28-continuity-screen-redesign-design.md): issues bucketed into CE's five comic
/// ages, in date order, with the "Review inferred ages" queue - the one write path, which writes <c>Issue.BookAge</c> through
/// <see cref="BookAgeReviewResolver"/>. The redesign adds the year histogram (click a year to jump to it), each era's counts and read
/// progress, era colours and folding. The old series-family / whole-library scopes aren't carried over: no view has reached them since
/// the 2026-08-28 redesign.
/// </summary>
public partial class ContinuityTimelineViewModel : ViewModelBase
{
    public const double HistogramHeight = 40;

    private readonly Action<int> _goToReader;
    private int? _continuityId;
    private int? _eventId;
    private List<int> _seriesIds = new();
    private readonly HashSet<ComicAge> _folded = new();

    public ContinuityTimelineViewModel(Action<int> goToReader) => _goToReader = goToReader;

    public ObservableCollection<TimelineSectionViewModel> Sections { get; } = new();

    public ObservableCollection<TimelineYearBar> YearBars { get; } = new();

    public ObservableCollection<InferredAgeRowViewModel> InferredAges { get; } = new();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _inferredAgesExpanded;

    public bool HasScope => _continuityId is not null || _eventId is not null;

    public bool HasNoSections => HasScope && Sections.Count == 0;

    public bool HasYearBars => YearBars.Count > 1;

    public bool HasNoInferredAges => InferredAges.Count == 0;

    /// <summary>Raised when a histogram click wants a cover brought into view.</summary>
    public event Action<TimelineIssueCard>? JumpRequested;

    public void LoadForContinuity(int continuityId)
    {
        if (_continuityId != continuityId || _eventId is not null)
        {
            _folded.Clear();
        }

        _continuityId = continuityId;
        _eventId = null;

        using var context = PaperbunkrDb.CreateContext();
        var continuity = context.Continuities.AsNoTracking().FirstOrDefault(c => c.Id == continuityId);
        var seriesIds = ContinuityResolver.GetSeriesInContinuity(context, continuityId).Select(s => s.Id).ToList();
        var series = context.Series.AsNoTracking().Include(s => s.Issues).Where(s => seriesIds.Contains(s.Id)).ToList();

        Title = $"Timeline · {continuity?.Name ?? "continuity"}";
        _seriesIds = seriesIds;
        Populate(context, series.SelectMany(s => s.Issues.Where(i => !i.IsPlaceholder).Select(i => (i, s.Name))));
    }

    public void LoadForEvent(int storyEventId)
    {
        if (_eventId != storyEventId || _continuityId is not null)
        {
            _folded.Clear();
        }

        _eventId = storyEventId;
        _continuityId = null;

        using var context = PaperbunkrDb.CreateContext();
        var storyEvent = context.StoryEvents.AsNoTracking().FirstOrDefault(e => e.Id == storyEventId);
        var issues = EventMembershipResolver.GetOrderedMembers(context, storyEventId)
            .Where(m => m.Issue is not null)
            .Select(m => m.Issue!)
            .ToList();

        Title = $"Timeline · {storyEvent?.Name ?? "event"}";
        _seriesIds = issues.Select(i => i.SeriesId).Distinct().ToList();
        Populate(context, issues.Select(i => (i, i.Series?.Name ?? "Unknown")));
    }

    private void Reload()
    {
        if (_continuityId is int continuityId)
        {
            LoadForContinuity(continuityId);
        }
        else if (_eventId is int eventId)
        {
            LoadForEvent(eventId);
        }
    }

    /// <summary>(issue, series name) pairs → era sections in date order, the histogram, and the inferred-age queue.</summary>
    private void Populate(PaperbunkrDbContext context, IEnumerable<(Issue Issue, string SeriesName)> entries)
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

        Sections.Clear();
        foreach (ComicAge age in Enum.GetValues<ComicAge>())
        {
            if (!buckets.TryGetValue(age, out var list))
            {
                continue;
            }

            var info = ComicAgeCatalog.All[age];
            var section = new TimelineSectionViewModel { Label = info.DisplayName, CommonlyCitedRange = info.CommonlyCitedRange, Era = age };
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
                    YearLabel = entry.Issue.Year?.ToString(CultureInfo.InvariantCulture),
                    Year = entry.Issue.Year,
                });
            }

            section.IsFolded = _folded.Contains(age);
            section.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TimelineSectionViewModel.IsFolded))
                {
                    if (section.IsFolded) _folded.Add(section.Era); else _folded.Remove(section.Era);
                }
            };
            Sections.Add(section);
        }

        BuildHistogram();

        InferredAges.Clear();
        foreach (var row in BookAgeReviewResolver.GetInferred(context, _seriesIds))
        {
            InferredAges.Add(new InferredAgeRowViewModel(row, AcceptInferredAge));
        }

        OnPropertyChanged(nameof(HasScope));
        OnPropertyChanged(nameof(HasNoSections));
        OnPropertyChanged(nameof(HasNoInferredAges));
    }

    private void BuildHistogram()
    {
        YearBars.Clear();
        var counts = Sections.SelectMany(s => s.Issues).Select(i => i.Year).OfType<int>()
            .GroupBy(y => y).ToDictionary(g => g.Key, g => g.Count());
        if (counts.Count > 0)
        {
            int max = counts.Values.Max();
            int first = counts.Keys.Min();
            var (width, step) = BarSizing(counts.Keys.Max() - first + 1);
            for (int year = counts.Keys.Min(); year <= counts.Keys.Max(); year++)
            {
                int count = counts.GetValueOrDefault(year);
                YearBars.Add(new TimelineYearBar
                {
                    Year = year,
                    Count = count,
                    Fraction = (double)count / max,
                    BarWidth = width,
                    AxisLabel = year == first || year % step == 0 ? year.ToString(CultureInfo.InvariantCulture) : null,
                });
            }
        }

        OnPropertyChanged(nameof(HasYearBars));
    }

    /// <summary>
    /// Bar width and label spacing for a histogram of <paramref name="years"/> slots: a handful of years gets broad bars labelled every
    /// year, a century gets 7px bars labelled every decade, so the chart stays readable at both ends.
    /// </summary>
    internal static (double Width, int LabelStep) BarSizing(int years) => years switch
    {
        <= 5 => (48, 1),
        <= 12 => (32, 1),
        <= 30 => (18, 5),
        <= 60 => (10, 10),
        _ => (7, 10),
    };

    /// <summary>A histogram bar: unfold that year's era if needed and bring its first cover into view.</summary>
    [RelayCommand]
    private void JumpToYear(TimelineYearBar? bar)
    {
        if (bar is null || bar.IsEmpty)
        {
            return;
        }

        foreach (var section in Sections)
        {
            if (section.Issues.FirstOrDefault(i => i.Year == bar.Year) is { } card)
            {
                section.IsFolded = false;
                JumpRequested?.Invoke(card);
                return;
            }
        }
    }

    private void AcceptInferredAge(InferredAgeRowViewModel row)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            BookAgeReviewResolver.Accept(context, row.IssueId, row.Age);
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(Reload);    // the Accept button is in a row this rebuilds
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

        Reload();
    }

    [RelayCommand]
    private void ToggleInferredAges() => InferredAgesExpanded = !InferredAgesExpanded;

    /// <summary>Clicking an issue opens it in the reader, as every issue grid does.</summary>
    [RelayCommand]
    private void OpenIssue(TimelineIssueCard? card)
    {
        if (card is not null)
        {
            _goToReader(card.IssueId);
        }
    }
}
