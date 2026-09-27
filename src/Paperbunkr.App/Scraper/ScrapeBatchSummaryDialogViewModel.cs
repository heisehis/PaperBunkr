using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.Data.ComicVine.Scraping;

namespace Paperbunkr.App.Scraper;

/// <summary>
/// Backs <see cref="ScrapeBatchSummaryDialogView"/> (docs/superpowers/specs/2026-09-24-scraper-
/// review-tables-and-batch-summary-design.md §3.2) - CE's real <c>FinishForm</c>, the blocking modal
/// shown at the end of a scrape batch, but with Paperbunkr's own richer per-bucket breakdown
/// (<see cref="ScrapeBatchResult.Outcomes"/>) instead of CE's plain scraped/skipped pair. Shown only
/// at the end of an <b>interactive</b> batch (<c>ScrapeCoordinator.ScrapeIssuesAsync</c>'s own
/// wiring) - an unattended/scheduled run keeps using the Activity Center job result exclusively,
/// matching the existing headless-automation gate everywhere else in this scraper never showing a
/// modal on such a run. Informational only: a single OK button, no per-row retry action (CE never had
/// one either).
/// </summary>
public sealed partial class ScrapeBatchSummaryDialogViewModel : ObservableObject
{
    private readonly Action _resolve;

    public int Applied { get; }
    public int SkippedByUser { get; }
    public int NoMatchFound { get; }
    public int Failed { get; }
    public int Total { get; }

    public ObservableCollection<ScrapeBookOutcome> AppliedOutcomes { get; }
    public ObservableCollection<ScrapeBookOutcome> SkippedOutcomes { get; }
    public ObservableCollection<ScrapeBookOutcome> NoMatchOutcomes { get; }
    public ObservableCollection<ScrapeBookOutcome> FailedOutcomes { get; }

    public bool HasApplied => AppliedOutcomes.Count > 0;
    public bool HasSkipped => SkippedOutcomes.Count > 0;
    public bool HasNoMatch => NoMatchOutcomes.Count > 0;
    public bool HasFailed => FailedOutcomes.Count > 0;

    public ScrapeBatchSummaryDialogViewModel(ScrapeBatchResult result, Action resolve)
    {
        _resolve = resolve;
        Applied = result.Applied;
        SkippedByUser = result.SkippedByUser;
        NoMatchFound = result.NoMatchFound;
        Failed = result.Failed;
        Total = result.Total;

        AppliedOutcomes = new ObservableCollection<ScrapeBookOutcome>(result.Outcomes.Where(o => o.Kind == ScrapeOutcomeKind.Applied));
        SkippedOutcomes = new ObservableCollection<ScrapeBookOutcome>(result.Outcomes.Where(o => o.Kind == ScrapeOutcomeKind.SkippedByUser));
        NoMatchOutcomes = new ObservableCollection<ScrapeBookOutcome>(result.Outcomes.Where(o => o.Kind == ScrapeOutcomeKind.NoMatchFound));
        FailedOutcomes = new ObservableCollection<ScrapeBookOutcome>(result.Outcomes.Where(o => o.Kind == ScrapeOutcomeKind.Failed));
    }

    [RelayCommand]
    private void Ok() => _resolve();
}
