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
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Single-book properties editor (docs/superpowers/specs/2026-08-07-issue-properties-editor-design.md),
/// reached via right-click on an issue tile on the Detail screen's Issues tab. Edit-buffer pattern
/// (§3): <see cref="Load"/> copies every field into plain properties on this ViewModel and disposes
/// its context immediately - nothing stays open across the edit session. <see cref="Save"/> opens a
/// fresh context, re-fetches the real entity, writes every buffered field across, and persists.
/// <see cref="Cancel"/> never touches the database at all - Cancel is just navigating away.
/// </summary>
public partial class IssuePropertiesScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private int? _issueId;

    /// <summary>
    /// True only for the manual "add a physical book" flow (docs/superpowers/specs/2026-08-16-
    /// reveal-in-explorer-and-fileless-entries-design.md §3) - lets Cancel delete the placeholder
    /// row this exact flow just created, if the user backs out without touching anything. Defaults
    /// false, so every other Load call site (e.g. Detail screen's "Edit Properties") is unaffected.
    /// </summary>
    private bool _deleteIfUnedited;

    public IssuePropertiesScreenViewModel(Action goBack) : this(goBack, PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal IssuePropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory)
    {
        _goBack = goBack;
        _contextFactory = contextFactory;
        PropertyChanged += (_, e) =>
        {
            if (_isLoading || e.PropertyName is null || NonDataProperties.Contains(e.PropertyName))
            {
                return;
            }

            _isDirty = true;
        };
    }

    /// <summary>
    /// Properties the blanket dirty-tracking subscription above should ignore - tab navigation
    /// (<see cref="ActiveTab"/> plus the three booleans <see cref="OnActiveTabChanged"/> derives
    /// from it) isn't a data edit, and neither is <see cref="HasUnsavedChanges"/> itself needing to
    /// re-trigger (it isn't an ObservableProperty, but listed here for clarity if that ever changes).
    /// </summary>
    private static readonly HashSet<string> NonDataProperties = new()
    {
        nameof(ActiveTab), nameof(IsSummaryTab), nameof(IsDetailsTab), nameof(IsPlotNotesTab),
    };

    /// <summary>
    /// Blanket dirty-tracking (P6 follow-up, docs/alpha-todo.md) rather than a per-field snapshot
    /// diff - there are ~30 buffered fields here, and every one of them mutating is equally "the
    /// user made an edit" for the purposes of the unsaved-changes guard in
    /// <see cref="MainViewModel"/>, so a single PropertyChanged subscription covers all of them
    /// without having to keep a parallel list in sync. <see cref="ActiveTab"/> is excluded since
    /// switching tabs isn't a data edit.
    /// </summary>
    private bool _isLoading;
    private bool _isDirty;

    public bool HasUnsavedChanges() => _isDirty;

    // ===================== Tab strip =====================

    [ObservableProperty]
    private string _activeTab = "summary";

    public bool IsSummaryTab => ActiveTab == "summary";
    public bool IsDetailsTab => ActiveTab == "details";
    public bool IsPlotNotesTab => ActiveTab == "plotNotes";

    partial void OnActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsSummaryTab));
        OnPropertyChanged(nameof(IsDetailsTab));
        OnPropertyChanged(nameof(IsPlotNotesTab));
    }

    [RelayCommand]
    private void GoSummary() => ActiveTab = "summary";

    [RelayCommand]
    private void GoDetails() => ActiveTab = "details";

    [RelayCommand]
    private void GoPlotNotes() => ActiveTab = "plotNotes";

    // ===================== Summary tab (read-only info + ratings) =====================

    [ObservableProperty]
    private string _headerLabel = string.Empty;

    [ObservableProperty]
    private Bitmap? _coverImage;

    [ObservableProperty]
    private IBrush _coverBrush = SeriesCardSample.Gradient("#442a1c", "#c9803f");

    [ObservableProperty]
    private string _typeLabel = "Comic Book";

    [ObservableProperty]
    private string _filePathLabel = string.Empty;

    [ObservableProperty]
    private string _pageCountLabel = "Unknown";

    [ObservableProperty]
    private int? _myRating;

    [ObservableProperty]
    private int? _communityRating;

    public bool MyRatingStar1 => (MyRating ?? 0) >= 1;
    public bool MyRatingStar2 => (MyRating ?? 0) >= 2;
    public bool MyRatingStar3 => (MyRating ?? 0) >= 3;
    public bool MyRatingStar4 => (MyRating ?? 0) >= 4;
    public bool MyRatingStar5 => (MyRating ?? 0) >= 5;

    public bool CommunityRatingStar1 => (CommunityRating ?? 0) >= 1;
    public bool CommunityRatingStar2 => (CommunityRating ?? 0) >= 2;
    public bool CommunityRatingStar3 => (CommunityRating ?? 0) >= 3;
    public bool CommunityRatingStar4 => (CommunityRating ?? 0) >= 4;
    public bool CommunityRatingStar5 => (CommunityRating ?? 0) >= 5;

    partial void OnMyRatingChanged(int? value)
    {
        OnPropertyChanged(nameof(MyRatingStar1));
        OnPropertyChanged(nameof(MyRatingStar2));
        OnPropertyChanged(nameof(MyRatingStar3));
        OnPropertyChanged(nameof(MyRatingStar4));
        OnPropertyChanged(nameof(MyRatingStar5));
    }

    partial void OnCommunityRatingChanged(int? value)
    {
        OnPropertyChanged(nameof(CommunityRatingStar1));
        OnPropertyChanged(nameof(CommunityRatingStar2));
        OnPropertyChanged(nameof(CommunityRatingStar3));
        OnPropertyChanged(nameof(CommunityRatingStar4));
        OnPropertyChanged(nameof(CommunityRatingStar5));
    }

    /// <summary>Toggle-to-clear: clicking the currently-set star unrates it. Whole-star only (§4).</summary>
    private static int? ToggleStar(int? current, int star) => current == star ? null : star;

    [RelayCommand] private void SetMyRating1() => MyRating = ToggleStar(MyRating, 1);
    [RelayCommand] private void SetMyRating2() => MyRating = ToggleStar(MyRating, 2);
    [RelayCommand] private void SetMyRating3() => MyRating = ToggleStar(MyRating, 3);
    [RelayCommand] private void SetMyRating4() => MyRating = ToggleStar(MyRating, 4);
    [RelayCommand] private void SetMyRating5() => MyRating = ToggleStar(MyRating, 5);

    [RelayCommand] private void SetCommunityRating1() => CommunityRating = ToggleStar(CommunityRating, 1);
    [RelayCommand] private void SetCommunityRating2() => CommunityRating = ToggleStar(CommunityRating, 2);
    [RelayCommand] private void SetCommunityRating3() => CommunityRating = ToggleStar(CommunityRating, 3);
    [RelayCommand] private void SetCommunityRating4() => CommunityRating = ToggleStar(CommunityRating, 4);
    [RelayCommand] private void SetCommunityRating5() => CommunityRating = ToggleStar(CommunityRating, 5);

    // ===================== Details tab =====================

    [ObservableProperty] private string _number = string.Empty;
    [ObservableProperty] private string _volumeText = string.Empty;
    [ObservableProperty] private string _countText = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _alternateSeries = string.Empty;
    [ObservableProperty] private string _alternateNumber = string.Empty;
    [ObservableProperty] private string _storyArc = string.Empty;
    [ObservableProperty] private string _storyArcNumber = string.Empty;
    [ObservableProperty] private string _seriesGroup = string.Empty;
    [ObservableProperty] private string _publisher = string.Empty;
    [ObservableProperty] private string _imprint = string.Empty;
    [ObservableProperty] private string _format = string.Empty;
    [ObservableProperty] private string _yearText = string.Empty;
    [ObservableProperty] private string _monthText = string.Empty;
    [ObservableProperty] private string _dayText = string.Empty;
    [ObservableProperty] private string _genre = string.Empty;
    [ObservableProperty] private string _tags = string.Empty;
    [ObservableProperty] private string _writer = string.Empty;
    [ObservableProperty] private string _penciller = string.Empty;
    [ObservableProperty] private string _inker = string.Empty;
    [ObservableProperty] private string _colorist = string.Empty;
    [ObservableProperty] private string _letterer = string.Empty;
    [ObservableProperty] private string _coverArtist = string.Empty;
    [ObservableProperty] private string _editor = string.Empty;
    [ObservableProperty] private string _translator = string.Empty;
    [ObservableProperty] private string _ageRating = string.Empty;
    [ObservableProperty] private string _languageIso = string.Empty;

    /// <summary>String-backed enum picker, same shape as the other *Text fields in this VM - avoids needing an Avalonia enum value converter. Replaces the old bool BlackAndWhite checkbox (docs/superpowers/specs/2026-08-17-metadata-model-phase1-canonical-metadata-design.md).</summary>
    [ObservableProperty] private string _colorModeText = nameof(ColorMode.Unknown);

    public static string[] ColorModeOptions { get; } = Enum.GetNames<ColorMode>();

    // ===================== Plot & Notes tab =====================

    [ObservableProperty] private string _characters = string.Empty;
    [ObservableProperty] private string _teams = string.Empty;
    [ObservableProperty] private string _mainCharacterOrTeam = string.Empty;
    [ObservableProperty] private string _locations = string.Empty;
    [ObservableProperty] private string _web = string.Empty;
    [ObservableProperty] private string _scanInformation = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _review = string.Empty;

    // ===================== Load / Save / Cancel =====================

    public void Load(int issueId, bool deleteIfUnedited = false)
    {
        _isLoading = true;
        _issueId = issueId;
        _deleteIfUnedited = deleteIfUnedited;

        using var context = _contextFactory();
        var issue = context.Issues.Include(i => i.Series).Include(i => i.MetadataProposals).FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            _isLoading = false;
            return;
        }

        HeaderLabel = $"{issue.Series?.Name ?? "Unknown Series"} #{issue.EffectiveNumber()}";
        CoverImage = CoverImageCache.Get(issue.Id);
        CoverBrush = SeriesCardSample.CoverBrushFor(issue.Series?.Name ?? string.Empty);
        FilePathLabel = issue.FilePath ?? string.Empty;
        PageCountLabel = issue.PageCount?.ToString() ?? "Unknown";

        MyRating = issue.Rating.HasValue ? (int)issue.Rating.Value : null;
        CommunityRating = issue.CommunityRating.HasValue ? (int)issue.CommunityRating.Value : null;

        // Pre-filled with the effective (stored-or-accepted-proposal) value, not just the raw
        // field (docs/superpowers/specs/2026-08-17-metadata-model-phase2a-metadata-proposals-
        // design.md) - a filename-inferred guess shows up here as editable, same as CE's
        // ShadowNumber-in-editor precedent. Save below still writes to the raw field only.
        Number = issue.EffectiveNumber() ?? string.Empty;
        VolumeText = issue.EffectiveVolume() ?? string.Empty;
        CountText = issue.Count?.ToString() ?? string.Empty;
        Title = issue.Title ?? string.Empty;
        AlternateSeries = issue.AlternateSeries ?? string.Empty;
        AlternateNumber = issue.AlternateNumber ?? string.Empty;
        StoryArc = issue.StoryArc ?? string.Empty;
        StoryArcNumber = issue.StoryArcNumber ?? string.Empty;
        SeriesGroup = issue.SeriesGroup ?? string.Empty;
        Publisher = issue.Publisher ?? string.Empty;
        Imprint = issue.Imprint ?? string.Empty;
        Format = issue.Format ?? string.Empty;
        YearText = issue.EffectiveYear()?.ToString() ?? string.Empty;
        MonthText = issue.Month?.ToString() ?? string.Empty;
        DayText = issue.Day?.ToString() ?? string.Empty;
        Genre = issue.Genre ?? string.Empty;
        Tags = issue.Tags ?? string.Empty;
        Writer = issue.Writer ?? string.Empty;
        Penciller = issue.Penciller ?? string.Empty;
        Inker = issue.Inker ?? string.Empty;
        Colorist = issue.Colorist ?? string.Empty;
        Letterer = issue.Letterer ?? string.Empty;
        CoverArtist = issue.CoverArtist ?? string.Empty;
        Editor = issue.Editor ?? string.Empty;
        Translator = issue.Translator ?? string.Empty;
        AgeRating = issue.AgeRating ?? string.Empty;
        LanguageIso = issue.LanguageISO ?? string.Empty;
        ColorModeText = issue.ColorMode.ToString();

        Characters = issue.Characters ?? string.Empty;
        Teams = issue.Teams ?? string.Empty;
        MainCharacterOrTeam = issue.MainCharacterOrTeam ?? string.Empty;
        Locations = issue.Locations ?? string.Empty;
        Web = issue.Web ?? string.Empty;
        ScanInformation = issue.ScanInformation ?? string.Empty;
        Summary = issue.Summary ?? string.Empty;
        Notes = issue.Notes ?? string.Empty;
        Review = issue.Review ?? string.Empty;

        ActiveTab = "summary";
        _isLoading = false;
        _isDirty = false;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int? ParseInt(string value) => int.TryParse(value, out int result) ? result : null;

    [RelayCommand]
    private void Save()
    {
        if (_issueId is not int issueId)
        {
            return;
        }

        using var context = _contextFactory();
        var issue = context.Issues.Find(issueId);
        if (issue is null)
        {
            _goBack();
            return;
        }

        issue.Rating = MyRating.HasValue ? (float?)MyRating.Value : null;
        issue.CommunityRating = CommunityRating.HasValue ? (float?)CommunityRating.Value : null;

        issue.Number = NullIfEmpty(Number);
        issue.Volume = NullIfEmpty(VolumeText);
        issue.Count = ParseInt(CountText);
        issue.Title = NullIfEmpty(Title);
        issue.AlternateSeries = NullIfEmpty(AlternateSeries);
        issue.AlternateNumber = NullIfEmpty(AlternateNumber);
        issue.StoryArc = NullIfEmpty(StoryArc);
        issue.StoryArcNumber = NullIfEmpty(StoryArcNumber);
        issue.SeriesGroup = NullIfEmpty(SeriesGroup);
        issue.Publisher = NullIfEmpty(Publisher);
        issue.Imprint = NullIfEmpty(Imprint);
        issue.Format = NullIfEmpty(Format);
        issue.Year = ParseInt(YearText);
        issue.Month = ParseInt(MonthText);
        issue.Day = ParseInt(DayText);
        issue.Genre = NullIfEmpty(Genre);
        issue.Tags = NullIfEmpty(Tags);
        issue.Writer = NullIfEmpty(Writer);
        issue.Penciller = NullIfEmpty(Penciller);
        issue.Inker = NullIfEmpty(Inker);
        issue.Colorist = NullIfEmpty(Colorist);
        issue.Letterer = NullIfEmpty(Letterer);
        issue.CoverArtist = NullIfEmpty(CoverArtist);
        issue.Editor = NullIfEmpty(Editor);
        issue.Translator = NullIfEmpty(Translator);
        issue.AgeRating = NullIfEmpty(AgeRating);
        issue.LanguageISO = NullIfEmpty(LanguageIso);
        issue.ColorMode = Enum.Parse<ColorMode>(ColorModeText);

        issue.Characters = NullIfEmpty(Characters);
        issue.Teams = NullIfEmpty(Teams);
        issue.MainCharacterOrTeam = NullIfEmpty(MainCharacterOrTeam);
        issue.Locations = NullIfEmpty(Locations);
        issue.Web = NullIfEmpty(Web);
        issue.ScanInformation = NullIfEmpty(ScanInformation);
        issue.Summary = NullIfEmpty(Summary);
        issue.Notes = NullIfEmpty(Notes);
        issue.Review = NullIfEmpty(Review);

        context.SaveChanges();
        _isDirty = false;
        _goBack();
    }

    /// <summary>
    /// Deletes the placeholder row this flow just created if the user backs out without touching
    /// anything (docs/superpowers/specs/2026-08-16-reveal-in-explorer-and-fileless-entries-design.md
    /// §3) - reuses HasUnsavedChanges()/_isDirty exactly as-is, so this can never delete a real
    /// pre-existing book: _deleteIfUnedited is only ever true when the caller's ResolveOrCreatePlaceholder
    /// call actually inserted a new row, never an existing match.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        if (_deleteIfUnedited && !HasUnsavedChanges() && _issueId is int issueId)
        {
            using var context = _contextFactory();
            var issue = context.Issues.Find(issueId);
            if (issue is not null)
            {
                context.Issues.Remove(issue);
                context.SaveChanges();
            }
        }

        _goBack();
    }
}
