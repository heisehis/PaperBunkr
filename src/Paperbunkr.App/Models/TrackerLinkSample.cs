using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.Models;

/// <summary>One already-linked tracker shown on the Detail screen's Details tab (docs/superpowers/specs/2026-08-23-tracker-write-back-sync-design.md) - separate list from <see cref="ExternalLinkSample"/>'s metadata links, per the same tracker-vs-scraper distinction that shapes this whole feature.
///
/// <para><see cref="ObservableObject"/>, not a plain record, since docs/superpowers/specs/2026-09-
/// 18-per-tracker-score-and-finish-date-design.md's expand-on-click panel binds directly to
/// <see cref="Score"/>/<see cref="FinishDate"/>/<see cref="Status"/>/<see cref="ChapterProgress"/>
/// for live inline editing - the chip row itself only ever reads <see cref="Service"/>/
/// <see cref="ExternalId"/>/<see cref="Url"/>, which stay <see langword="init"/>-only.</para></summary>
public sealed partial class TrackerLinkSample : ObservableObject
{
    public required TrackingService Service { get; init; }
    public required string ExternalId { get; init; }
    public string? Url { get; init; }

    /// <summary>The service name as a plain string for a <c>BrandMark Family="Service"</c> binding
    /// (its <c>Value</c> is <see cref="string"/>?) - docs/superpowers/specs/2026-09-04-detail-
    /// screen-icons-and-glyphs-design.md Part 2 §A.</summary>
    public string ServiceName => Service.ToString();

    /// <summary>Every one of the 8 trackers exposes some form of user score (docs/superpowers/specs/
    /// 2026-09-18-per-tracker-score-and-finish-date-design.md's capability table) - always true
    /// today, kept as a property (not a hardcoded assumption in the view) so a future 9th tracker
    /// without one doesn't need a XAML change too.</summary>
    public bool SupportsScore => true;

    /// <summary>Only AniList/MyAnimeList/MangaBaka/Kitsu expose a finish-date field on their real
    /// APIs - MangaDex/MangaUpdates/Shikimori/Bangumi confirmed absent this session (same design
    /// doc's capability table).</summary>
    public bool SupportsFinishDate => Service is TrackingService.AniList or TrackingService.MyAnimeList or TrackingService.MangaBaka or TrackingService.Kitsu;

    /// <summary>The panel's current Status/Progress/Score/FinishDate - populated by a lazy
    /// <c>GetEntryAsync</c> fetch when the panel opens (<c>DetailTabsViewModel.ToggleTrackerLinkDetails</c>),
    /// then edited in place and pushed back via <c>PushTrackerFieldAsync</c>. Not persisted anywhere
    /// itself - purely the panel's live editing buffer for this one tracker link.</summary>
    [ObservableProperty]
    private ReadingStatus _status;

    [ObservableProperty]
    private int? _chapterProgress;

    /// <summary>Bridges <see cref="ChapterProgress"/> (<see cref="int"/>?) to a plain
    /// <c>TextBox.Text</c> - the panel uses <c>Behaviors/TextSpinner.cs</c>'s compact spinner
    /// (18x9 buttons) here instead of a real <see cref="Avalonia.Controls.NumericUpDown"/> (its
    /// built-in spin buttons render much taller), same "wrapper property, not a converter" shape
    /// as <see cref="FinishDateOffset"/> below.</summary>
    public string ChapterProgressText
    {
        get => ChapterProgress?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        set => ChapterProgress = int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : null;
    }

    partial void OnChapterProgressChanged(int? value) => OnPropertyChanged(nameof(ChapterProgressText));

    [ObservableProperty]
    private decimal? _score;

    /// <summary>Bridges <see cref="Score"/> to a plain <c>TextBox.Text</c> for the same compact-
    /// spinner reason as <see cref="ChapterProgressText"/> - <c>Behaviors/TextSpinner.cs</c>'s
    /// <c>Step</c> attached property is set to <c>0.1</c> in the view for this field, the one
    /// fractional-step caller in the app.</summary>
    public string ScoreText
    {
        get => Score?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        set => Score = decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal d) ? d : null;
    }

    partial void OnScoreChanged(decimal? value) => OnPropertyChanged(nameof(ScoreText));

    [ObservableProperty]
    private DateOnly? _finishDate;

    /// <summary>Bridges <see cref="FinishDate"/> (<see cref="DateOnly"/>?) to
    /// <c>DatePicker.SelectedDate</c> (<see cref="DateTimeOffset"/>?) - same "wrapper property, not
    /// a converter" precedent as <c>BookPropertiesScreenViewModel.PublishedDate</c> for the same
    /// type mismatch.</summary>
    public DateTimeOffset? FinishDateOffset
    {
        get => FinishDate is { } date ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
        set => FinishDate = value is { } offset ? DateOnly.FromDateTime(offset.UtcDateTime) : null;
    }

    partial void OnFinishDateChanged(DateOnly? value) => OnPropertyChanged(nameof(FinishDateOffset));

    /// <summary>True while the panel's initial <c>GetEntryAsync</c> fetch is in flight, or while a
    /// field push/the explicit "Use this score" pull is running - the view disables its editable
    /// controls on this, same "don't let a second edit race the first" precedent as this file's own
    /// tracker-search <c>IsTrackerSearchInProgress</c> flag.</summary>
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _pushStatus;
}
