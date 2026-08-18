using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Series Detail screen, "stacked" layout variant (the default selected in the parent
/// "Paperbunkr App" wireframe), ported from DetailScreen.dc.html. Loads a real
/// <see cref="Series"/> by id via <see cref="LoadSeries"/> instead of the wireframe's
/// hardcoded "Brass Horizon" sample content. Switches to a single issue's own cover/credits/pills
/// when exactly one Issues-tab tile is selected (docs/superpowers/specs/
/// 2026-08-07-detail-screen-issue-focus-design.md) - series title/status stay series-level
/// regardless (revised after live verification: the description switches too, since
/// <see cref="Issue.Summary"/> is a real distinct field and leaving it static looked wrong next to
/// everything else that does switch).
/// </summary>
public partial class DetailScreenViewModel : ViewModelBase
{
    public DetailScreenViewModel(Action goBack, Action<int> goToReader, Action<int> goToProperties, Action<IReadOnlyList<int>> goToBulkProperties)
    {
        _goBack = goBack;
        _goToReader = goToReader;
        _goToProperties = goToProperties;
        _goToBulkProperties = goToBulkProperties;
        CoverBrush = SeriesCardSample.Gradient("#442a1c", "#c9803f");
        Tabs = new DetailTabsViewModel(goToProperties, goToBulkProperties, RefreshForSelection);
        Meta = new DetailMetaViewModel();
        Pills = new DetailPillsViewModel();
    }

    private readonly Action _goBack;
    private readonly Action<int> _goToReader;
    private readonly Action<int> _goToProperties;
    private readonly Action<IReadOnlyList<int>> _goToBulkProperties;
    private int? _continueIssueId;
    private int? _seriesId;
    private bool _isLoadingSeries;
    private Bitmap? _seriesCoverImage;
    private string _seriesSummary = string.Empty;

    public DetailTabsViewModel Tabs { get; }
    public DetailMetaViewModel Meta { get; }
    public DetailPillsViewModel Pills { get; }

    /// <summary>
    /// docs/superpowers/specs/2026-08-06-migration-ux-design.md §A: a plain manual picker, real
    /// but not the eventual §7/§9 scraper-driven classification flow (which doesn't exist yet
    /// anywhere in the app) - what lets a Needs Review "content type" item actually get resolved.
    /// </summary>
    public ContentType[] ContentTypeOptions { get; } = Enum.GetValues<ContentType>();

    [ObservableProperty]
    private IBrush _coverBrush;

    [ObservableProperty]
    private Bitmap? _coverImage;

    [ObservableProperty]
    private string _seriesTitle = string.Empty;

    [ObservableProperty]
    private string _coverTitle = string.Empty;

    [ObservableProperty]
    private ContentType _selectedContentType;

    partial void OnSelectedContentTypeChanged(ContentType value)
    {
        if (_isLoadingSeries || _seriesId is not int seriesId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Find(seriesId);
        if (series is null)
        {
            return;
        }

        series.ContentType = value;
        context.SaveChanges();
    }

    [ObservableProperty]
    private string _statusLabel = string.Empty;

    [ObservableProperty]
    private string _issueCountLabel = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _continueLabel = "Start Reading";

    /// <summary>Loads the series with the given id from the database and refreshes every bound field.</summary>
    public void LoadSeries(int seriesId)
    {
        using var context = PaperbunkrDb.CreateContext();
        var series = context.Series.Include(s => s.Issues).ThenInclude(i => i.MetadataProposals).FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return;
        }

        _isLoadingSeries = true;
        _seriesId = seriesId;

        var card = SeriesCardSample.FromSeries(series);
        CoverBrush = card.CoverBrush;
        CoverImage = card.CoverImage;
        _seriesCoverImage = card.CoverImage;
        SeriesTitle = series.Name;
        CoverTitle = series.Name.ToUpperInvariant();
        SelectedContentType = series.ContentType;
        StatusLabel = series.IsComplete ? "Complete" : "Ongoing";
        IssueCountLabel = $"{series.Issues.Count} Issues";
        _seriesSummary = string.IsNullOrWhiteSpace(series.Summary) ? "No summary available." : series.Summary;
        Summary = _seriesSummary;

        // Priority: an issue actually in progress (resume where they left off) beats one never
        // opened at all, which beats falling back to a re-read. Found in review: the old logic
        // only checked LastPageRead is null/0 ("unread"), so a partially-read issue never won
        // out over re-reading issue #1 - exactly backwards for what "Continue" is for.
        var inProgress = series.Issues
            .Where(i => i.LastPageRead is > 0 && i.PageCount is > 0 && i.LastPageRead < i.PageCount)
            .OrderByNumber()
            .FirstOrDefault();
        var nextUnread = series.Issues
            .Where(i => i.LastPageRead is null or 0)
            .OrderByNumber()
            .FirstOrDefault();
        var continueIssue = inProgress ?? nextUnread;
        bool isReread = continueIssue is null;
        continueIssue ??= series.Issues.OrderByNumber().FirstOrDefault();

        _continueIssueId = continueIssue?.Id;
        ContinueLabel = continueIssue is null
            ? "No Issues"
            : isReread
                ? $"Re-read — Issue #{continueIssue.EffectiveNumber()}"
                : $"Continue — Issue #{continueIssue.EffectiveNumber()}";

        var enabledVirtualTags = context.VirtualTagDefinitions.Where(t => t.IsEnabled).OrderBy(t => t.SortOrder).ToList();

        Tabs.LoadSeries(series);
        Meta.LoadSeries(series);
        Pills.LoadSeries(series, enabledVirtualTags);

        _isLoadingSeries = false;
        RaiseEditStateChanged();
    }

    /// <summary>
    /// Re-runs <see cref="LoadSeries"/> for whichever series is currently loaded - used when
    /// returning from a screen that may have edited this series' data out from under it (e.g. the
    /// Issue Properties editor changing an issue's <c>Number</c>, which the Issues tab's tile
    /// label is derived from) without a full series-id round-trip through the caller. No-op if no
    /// series has been loaded yet.
    /// </summary>
    public void ReloadCurrentSeries()
    {
        if (_seriesId is int seriesId)
        {
            LoadSeries(seriesId);
        }
    }

    /// <summary>
    /// Switches <see cref="CoverImage"/>/<see cref="Meta"/>/<see cref="Pills"/> between the series
    /// aggregate (0 or 2+ issues selected) and one issue's own data (exactly 1 selected) - invoked
    /// via <see cref="Tabs"/>' selection-changed callback (docs/superpowers/specs/
    /// 2026-08-07-detail-screen-issue-focus-design.md §1). Skipped while <see cref="LoadSeries"/>
    /// itself is mid-flight - that method already loads the series-mode state directly, so running
    /// this too would just be a redundant extra query.
    /// </summary>
    private void RefreshForSelection()
    {
        if (_isLoadingSeries || _seriesId is not int seriesId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var enabledVirtualTags = context.VirtualTagDefinitions.Where(t => t.IsEnabled).OrderBy(t => t.SortOrder).ToList();

        if (Tabs.SelectedIssueIds.Count == 1)
        {
            int issueId = Tabs.SelectedIssueIds.Single();
            var issue = context.Issues.Include(i => i.Series).Include(i => i.MetadataProposals).FirstOrDefault(i => i.Id == issueId);
            if (issue is null)
            {
                return;
            }

            CoverImage = CoverImageCache.Get(issue.Id);
            Summary = string.IsNullOrWhiteSpace(issue.Summary) ? "No summary available." : issue.Summary;
            Meta.LoadIssue(issue);
            Pills.LoadIssue(issue, enabledVirtualTags);
        }
        else
        {
            var series = context.Series.Include(s => s.Issues).FirstOrDefault(s => s.Id == seriesId);
            if (series is null)
            {
                return;
            }

            CoverImage = _seriesCoverImage;
            Summary = _seriesSummary;
            Meta.LoadSeries(series);
            Pills.LoadSeries(series, enabledVirtualTags);
        }

        RaiseEditStateChanged();
    }

    private void RaiseEditStateChanged()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(EditButtonLabel));
    }

    public bool CanEdit => Tabs.SelectedIssueIds.Count > 0;

    public string EditButtonLabel => Tabs.SelectedIssueIds.Count switch
    {
        0 => "Edit",
        1 => "Edit Issue",
        int n => $"Edit {n} Issues",
    };

    /// <summary>
    /// Dispatches exactly like the right-click menu (docs/superpowers/specs/
    /// 2026-08-07-bulk-issue-editing-design.md §2) but purely off the current selection - there's
    /// no "clicked tile" to union with here, it's a toolbar button, not a per-tile context menu.
    /// </summary>
    [RelayCommand]
    private void Edit()
    {
        var ids = Tabs.SelectedIssueIds.ToList();
        if (ids.Count == 1)
        {
            _goToProperties(ids[0]);
        }
        else if (ids.Count > 1)
        {
            _goToBulkProperties(ids);
        }
    }

    [RelayCommand]
    private void GoBack() => _goBack();

    [RelayCommand]
    private void Continue()
    {
        if (_continueIssueId is int issueId)
        {
            _goToReader(issueId);
        }
    }
}
