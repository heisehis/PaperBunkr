using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.CeMigration;
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
public partial class DetailScreenViewModel : ViewModelBase, IDetailHeaderSource
{
    public DetailScreenViewModel(Action goBack, Action<int> goToReader, Action<int> goToProperties, Action<IReadOnlyList<int>> goToBulkProperties, Action<int>? goDetailForSeries = null, Action<string>? goLibraryWithSearch = null, Action<int>? onQuickRate = null, Action<int>? goLibraryWithCollection = null, Action<int>? enqueueMetadataWriteBack = null)
    {
        _goBack = goBack;
        _goToReader = goToReader;
        _goToProperties = goToProperties;
        _goToBulkProperties = goToBulkProperties;
        _goDetailForSeries = goDetailForSeries ?? (_ => { });
        _enqueueMetadataWriteBack = enqueueMetadataWriteBack;
        CoverBrush = SeriesCardSample.Gradient("#442a1c", "#c9803f");
        Tabs = new DetailTabsViewModel(goToProperties, goToBulkProperties, RefreshForSelection, onQuickRate, _goDetailForSeries, goToReader, goLibraryWithCollection);
        Band = new DetailBandViewModel(goLibraryWithSearch, () => Tabs.GoDetailsCommand.Execute(null), ReweightTag);
    }

    private readonly Action _goBack;
    private readonly Action<int> _goToReader;
    private readonly Action<int> _goToProperties;
    private readonly Action<IReadOnlyList<int>> _goToBulkProperties;
    private readonly Action<int> _goDetailForSeries;
    private readonly Action<int>? _enqueueMetadataWriteBack;
    private int? _continueIssueId;
    private int? _focusedIssueId;
    private string _focusedIssueLabel = "Read Issue";
    private int? _coverIssueId;
    private int? _seriesId;
    private bool _isLoadingSeries;
    private Bitmap? _seriesCoverImage;
    private Bitmap? _seriesBackdrop;
    private string _seriesSummary = string.Empty;
    private string _seriesMetaLine = string.Empty;

    public DetailTabsViewModel Tabs { get; }
    public DetailBandViewModel Band { get; }

    // --- IDetailHeaderSource ---

    [ObservableProperty]
    private Bitmap? _backdropImage;

    [ObservableProperty]
    private string _metaLine = string.Empty;

    public string HeaderTitle => SeriesTitle;
    string? IDetailHeaderSource.SecondaryTitle => null;
    DetailHeroProgress? IDetailHeaderSource.TrackerProgress => null;

    private string? _readingStatus;
    private ReadingStatusPickerViewModel? _readingStatusPicker;

    private SeriesMetaFields _seriesFields = SeriesMetaFields.Empty;
    private bool _seriesComplete;
    private string _issueCountBadge = string.Empty;
    private string? _unreadBadge;
    private string? _issueSummaryLine;
    private IReadOnlyList<DetailMetaBadge> _metaBadges = System.Array.Empty<DetailMetaBadge>();

    /// <summary>Explicit impl (not a public member) so the name doesn't clash with the
    /// <see cref="Data.Entities.ReadingStatus"/> enum type in this file's scope.</summary>
    string? IDetailHeaderSource.ReadingStatus => _readingStatus;
    ReadingStatusPickerViewModel? IDetailHeaderSource.ReadingStatusPicker => _readingStatusPicker;
    IReadOnlyList<DetailMetaBadge> IDetailHeaderSource.MetaBadges => _metaBadges;
    string? IDetailHeaderSource.IssueSummaryLine => _issueSummaryLine;

    /// <summary>
    /// Recomputes every series-level aggregate the hero shows - <see cref="_seriesFields"/>
    /// (publisher/year/format/age-rating/language), <see cref="_seriesComplete"/>, and the
    /// issue-count/unread <see cref="IssueSummaryLine"/> - from a freshly-loaded <paramref name="series"/>.
    /// Called from both <see cref="LoadSeries"/> and <see cref="RefreshForSelection"/> so an
    /// in-place "Mark as Read"/"Mark as Unread" (which never leaves this screen) updates the
    /// unread count immediately instead of only on the next full reload (bug found 2026-09-04:
    /// <see cref="RefreshForSelection"/> refreshed the focused issue's own data but never touched
    /// these series-wide fields at all).
    /// </summary>
    private void RefreshSeriesAggregates(Series series)
    {
        _seriesFields = SeriesMetaFields.FromSeries(series);
        _seriesComplete = series.IsComplete;
        int unread = series.Issues.Count(i => i.LastPageRead is null or 0);
        _issueCountBadge = $"{series.Issues.Count} issue{(series.Issues.Count == 1 ? "" : "s")}";
        _unreadBadge = unread > 0 ? $"{unread} unread" : null;
        _issueSummaryLine = _unreadBadge is null ? _issueCountBadge : $"{_issueCountBadge}  ·  {_unreadBadge}";
        OnPropertyChanged(nameof(IDetailHeaderSource.IssueSummaryLine));
    }

    /// <summary>Rebuilds the hero badge row (Part 4). Publisher / year / status are always
    /// series-level (<see cref="_seriesFields"/> aggregates across every issue); format /
    /// age-rating / language come from the focused issue when one is selected, otherwise from the
    /// same series aggregate - so a series whose cover issue happens to leave those blank still
    /// shows them. Called from <see cref="UpdateBandIssueMarks"/>.</summary>
    private void RebuildMetaBadges(Issue? issue, bool issueFocused)
    {
        var f = _seriesFields;
        _metaBadges = DetailMetaBadge.Build(
            f.Publisher, StatusLabel, _seriesComplete, f.Year,
            format:    issueFocused ? issue?.Format      : f.Format,
            ageRating: issueFocused ? issue?.AgeRating   : f.AgeRating,
            languageIso: issueFocused ? issue?.LanguageISO : f.LanguageIso);
            // issueCountLabel/unreadLabel deliberately not passed - Part 4 revision moved them to
            // IssueSummaryLine, a plain-text line rendered separately (see DetailHero.axaml).
        OnPropertyChanged(nameof(IDetailHeaderSource.MetaBadges));
        OnPropertyChanged(nameof(IDetailHeaderSource.HasMetaBadges));
    }

    /// <summary>The picker VM wrote a new <c>Series.ReadingStatus</c> - mirror it onto the hero's
    /// read-only string and the band so both surfaces refresh in step (Part 2 §C).</summary>
    private void OnReadingStatusPicked()
    {
        _readingStatus = _readingStatusPicker?.CurrentValue;
        OnPropertyChanged(nameof(IDetailHeaderSource.ReadingStatus));
        Band.ReadingStatusValue = _readingStatus;
    }

    partial void OnSeriesTitleChanged(string value) => OnPropertyChanged(nameof(HeaderTitle));

    /// <summary>
    /// The primary hero button is the focused issue's own "Read this issue" action when exactly one
    /// Issues/Specials tile is selected (docs/superpowers/specs/2026-08-07-detail-screen-issue-
    /// focus-design.md, extended 2026-09-04) - label reflects that issue's read state
    /// (Read / Continue / Re-read). With no single tile focused it falls back to the series-level
    /// Continue button.
    /// </summary>
    public IReadOnlyList<DetailHeroAction> Actions => new[]
    {
        _focusedIssueId is not null
            ? new DetailHeroAction(_focusedIssueLabel, ReadFocusedIssueCommand, IsPrimary: true, IsEnabled: true, Icon: Symbol.Play)
            : new DetailHeroAction(ContinueLabel, ContinueCommand, IsPrimary: true, IsEnabled: _continueIssueId is not null, Icon: Symbol.Play),
        new DetailHeroAction(EditButtonLabel, EditCommand, IsEnabled: CanEdit, Icon: Symbol.Edit),
        new DetailHeroAction("Change Cover", ChangeSeriesCoverCommand, Icon: Symbol.Image),
    };

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

    /// <summary>
    /// Reclassifying away from Comic re-invokes <see cref="_goDetailForSeries"/> (docs/superpowers/
    /// specs/2026-08-23-manga-detail-screen-design.md), which routes to
    /// <see cref="MangaDetailScreenViewModel"/> instead when the new <see cref="ContentType"/> is
    /// Manga/Manhua/Manhwa - a misclassification is a one-click fix from either screen, not a dead
    /// end. Reclassifying between Comic and Unknown (both stay on this screen) just reloads in
    /// place via the same call, which is harmless.
    /// </summary>
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

        // Content type drives the ComicInfo <Manga> field for every issue in the series
        // (docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md).
        if (_enqueueMetadataWriteBack is not null)
        {
            foreach (int issueId in context.Issues.Where(i => i.SeriesId == seriesId).Select(i => i.Id).ToList())
            {
                _enqueueMetadataWriteBack(issueId);
            }
        }

        _goDetailForSeries(seriesId);
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
        var series = context.Series.Include(s => s.Issues).ThenInclude(i => i.MetadataProposals)
            .Include(s => s.Issues).ThenInclude(i => i.Tags)
            .FirstOrDefault(s => s.Id == seriesId);
        if (series is null)
        {
            return;
        }

        _isLoadingSeries = true;
        _seriesId = seriesId;
        _focusedIssueId = null;

        var card = SeriesCardSample.FromSeries(series);
        CoverBrush = card.CoverBrush;
        _coverIssueId = card.CoverIssueId;
        // Single-item, low-volume display (unlike the Library grid) - eager decode here is fine.
        var seriesCover = card.CoverIssueId is int coverIssueId ? CoverImageCache.Get(coverIssueId) : null;
        CoverImage = seriesCover;
        _seriesCoverImage = seriesCover;
        _seriesBackdrop = seriesCover is not null ? BackdropBlurRenderer.Render(seriesCover, new PixelSize(1600, 680)) : null;
        BackdropImage = _seriesBackdrop;
        SeriesTitle = series.Name;
        CoverTitle = series.Name.ToUpperInvariant();
        SelectedContentType = series.ContentType;
        StatusLabel = series.IsComplete ? "Complete" : "Ongoing";
        _readingStatus = series.ReadingStatus == Data.Entities.ReadingStatus.Unknown ? null : series.ReadingStatus.ToString();
        OnPropertyChanged(nameof(IDetailHeaderSource.ReadingStatus));
        _readingStatusPicker = new ReadingStatusPickerViewModel(seriesId, onChanged: OnReadingStatusPicked);
        OnPropertyChanged(nameof(IDetailHeaderSource.ReadingStatusPicker));
        Band.ReadingStatusPicker = _readingStatusPicker;
        IssueCountLabel = $"{series.Issues.Count} Issues";
        _seriesSummary = string.IsNullOrWhiteSpace(series.Summary) ? "No summary available." : series.Summary;
        Summary = _seriesSummary;

        int unread = series.Issues.Count(i => i.LastPageRead is null or 0);
        RefreshSeriesAggregates(series);
        string publisher = _seriesFields.Publisher ?? string.Empty;
        _seriesMetaLine = string.Join("  ·  ", new[]
        {
            publisher,
            StatusLabel,
            _issueCountBadge,
            unread > 0 ? $"{unread} unread" : string.Empty,
        }.Where(s => s.Length > 0));
        MetaLine = _seriesMetaLine;
        Band.StatusText = StatusLabel;
        Band.PublisherText = publisher;
        Band.YearText = _seriesFields.Year ?? string.Empty;
        Band.Summary = _seriesSummary;
        Band.IsSynopsisExpanded = false;

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
        Band.LoadSeries(series, enabledVirtualTags);
        UpdateBandIssueMarks(series.Issues.FirstOrDefault(i => i.Id == _coverIssueId) ?? series.Issues.FirstOrDefault());

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
    /// Right-click reweight popover's persist step (docs/superpowers/specs/2026-08-23-weighted-
    /// categorized-tags-design.md) - <see cref="DetailPillsViewModel"/> stays DB-free, this is the
    /// one write seam it's given. Only re-fetches/updates the single matching <see cref="IssueTag"/>
    /// row (never touches Value/Category), matching the "Weight-only" scope the design gives this
    /// popover - Category stays editable only from the Issue Properties Editor.
    /// </summary>
    private void ReweightTag(int issueId, IssueTagField field, string value, IssueTagWeight weight)
    {
        using var context = PaperbunkrDb.CreateContext();
        var tag = context.Issues.Where(i => i.Id == issueId).SelectMany(i => i.Tags)
            .FirstOrDefault(t => t.Field == field && t.Value == value);
        if (tag is null)
        {
            return;
        }

        tag.Weight = weight;
        context.SaveChanges();

        // Tag weight lives in the paperbunkr.json sidecar, not the flat ComicInfo CSV
        // (docs/superpowers/specs/2026-09-03-file-metadata-write-back-design.md).
        _enqueueMetadataWriteBack?.Invoke(issueId);
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
            var issue = context.Issues.Include(i => i.Series).ThenInclude(s => s.Issues)
                .Include(i => i.MetadataProposals).Include(i => i.Tags).FirstOrDefault(i => i.Id == issueId);
            if (issue is null)
            {
                return;
            }

            if (issue.Series is not null)
            {
                // Series-wide aggregates (unread count, publisher/format/rating fallbacks) must
                // stay current even while an issue is focused - a "Mark as Read" on the focused
                // tile itself must move the unread count too.
                RefreshSeriesAggregates(issue.Series);
            }

            _focusedIssueId = issue.Id;
            _focusedIssueLabel = FocusedIssueLabelFor(issue);

            var issueCover = CoverImageCache.Get(issue.Id);
            CoverImage = issueCover;
            BackdropImage = issueCover is not null ? BackdropBlurRenderer.Render(issueCover, new PixelSize(1600, 680)) : _seriesBackdrop;
            Summary = string.IsNullOrWhiteSpace(issue.Summary) ? "No summary available." : issue.Summary;
            MetaLine = string.Join("  ·  ", new[]
            {
                issue.EffectiveNumber() is { Length: > 0 } n ? $"Issue #{n}" : string.Empty,
                string.IsNullOrWhiteSpace(issue.StoryArc) ? string.Empty : issue.StoryArc!,
                issue.ReleasedTime is { } rt ? rt.ToString("MMM yyyy") : string.Empty,
            }.Where(s => s.Length > 0));
            Band.Summary = Summary;
            Band.IsSynopsisExpanded = false;
            Band.LoadIssue(issue, enabledVirtualTags);
            UpdateBandIssueMarks(issue, issueFocused: true);
        }
        else
        {
            var series = context.Series.Include(s => s.Issues).ThenInclude(i => i.Tags).FirstOrDefault(s => s.Id == seriesId);
            if (series is null)
            {
                return;
            }

            _focusedIssueId = null;
            RefreshSeriesAggregates(series);

            CoverImage = _seriesCoverImage;
            BackdropImage = _seriesBackdrop;
            Summary = _seriesSummary;
            MetaLine = _seriesMetaLine;
            Band.Summary = _seriesSummary;
            Band.IsSynopsisExpanded = false;
            Band.LoadSeries(series, enabledVirtualTags);
            UpdateBandIssueMarks(series.Issues.FirstOrDefault(i => i.Id == _coverIssueId) ?? series.Issues.FirstOrDefault());
        }

        RaiseEditStateChanged();
    }

    /// <summary>Feeds the band's Format / AgeRating / Special marks from whichever issue is the
    /// current focus (the selected issue, or the cover issue in whole-series view).</summary>
    private void UpdateBandIssueMarks(Issue? issue, bool issueFocused = false)
    {
        Band.FormatText = issue?.Format ?? string.Empty;
        Band.AgeRatingText = issue?.AgeRating ?? string.Empty;
        Band.SetSpecialMarks(issue);
        RebuildMetaBadges(issue, issueFocused);
    }

    private void RaiseEditStateChanged()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(EditButtonLabel));
        OnPropertyChanged(nameof(Actions));
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

    /// <summary>"Read this issue" - opens the currently focused (single-selected) issue in the
    /// reader. Distinct from <see cref="Continue"/>, which always targets the series' resume point.</summary>
    [RelayCommand]
    private void ReadFocusedIssue()
    {
        if (_focusedIssueId is int issueId)
        {
            _goToReader(issueId);
        }
    }

    /// <summary>Read / Continue / Re-read + issue number, matching <see cref="IssueCardSample.CardActionLabel"/>'s
    /// own wording so the hero button and the Card-view tile button read the same.</summary>
    private static string FocusedIssueLabelFor(Issue issue)
    {
        bool inProgress = issue.LastPageRead is int lpr && lpr > 0
            && issue.PageCount is int pc && pc > 0 && lpr < pc;
        string verb = inProgress ? "Continue" : issue.HasBeenRead() ? "Re-read" : "Read";
        string? number = issue.EffectiveNumber();
        return string.IsNullOrWhiteSpace(number) ? $"{verb} Issue" : $"{verb} — Issue #{number}";
    }

    // --- Cover art override (docs/superpowers/specs/2026-08-23-cover-art-override-design.md) -
    // same pair as MangaDetailScreenViewModel's own, see that class's doc comment for the CE-
    // deviation rationale (any cover, linked or not, unlike CE's SetCustomBookThumbnail). ---

    [RelayCommand]
    private async Task ChangeCoverAsync(int issueId)
    {
        string? path = await new FilePickerService().PickImageFileAsync("Choose Cover Image");
        if (path is null)
        {
            return;
        }

        if (new CoverThumbnailService().TrySetCustomCover(issueId, path))
        {
            ReloadCurrentSeries();
        }
    }

    [RelayCommand]
    private void ResetCover(int issueId)
    {
        using var context = PaperbunkrDb.CreateContext();
        string? filePath = context.Issues.Find(issueId)?.FilePath;
        new CoverThumbnailService().ResetCover(issueId, filePath);
        ReloadCurrentSeries();
    }

    [RelayCommand]
    private Task ChangeSeriesCoverAsync() => _coverIssueId is int issueId ? ChangeCoverAsync(issueId) : Task.CompletedTask;
}
