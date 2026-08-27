using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
    private readonly Action<string, string>? _notify;
    private int? _issueId;

    /// <summary>
    /// True only for the manual "add a physical book" flow (docs/superpowers/specs/2026-08-16-
    /// reveal-in-explorer-and-fileless-entries-design.md §3) - lets Cancel delete the placeholder
    /// row this exact flow just created, if the user backs out without touching anything. Defaults
    /// false, so every other Load call site (e.g. Detail screen's "Edit Properties") is unaffected.
    /// </summary>
    private bool _deleteIfUnedited;

    /// <summary>Captured at <see cref="Load"/> purely so the templated/token text field editor's
    /// "{Series}" token has something to resolve against - not itself an editable/bindable field.</summary>
    private string _seriesName = string.Empty;

    private readonly MetadataEditHistoryService _history;

    /// <summary>Every field's value as of <see cref="Load"/>, before any edit - the Undo half of the
    /// entry <see cref="Save"/> records (docs/ce-feature-inventory.md §A "Undo/Redo").</summary>
    private Dictionary<string, string?> _beforeSnapshot = new();

    public IssuePropertiesScreenViewModel(Action goBack, Action<string, string>? notify = null, MetadataEditHistoryService? history = null)
        : this(goBack, PaperbunkrDb.CreateContext, notify, history)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal IssuePropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory, Action<string, string>? notify = null, MetadataEditHistoryService? history = null)
    {
        _goBack = goBack;
        _contextFactory = contextFactory;
        _notify = notify;
        _history = history ?? MetadataEditHistoryService.Shared;
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
        nameof(HasClipboard),
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

    /// <summary>
    /// Category/Weight rows for existing Genre/Tags values (docs/superpowers/specs/2026-08-23-
    /// weighted-categorized-tags-design.md) - adding/removing values still goes through the plain
    /// CSV text boxes above (<see cref="Genre"/>/<see cref="Tags"/>); these only edit Category/
    /// Weight for values that survive that diff. Repopulated by <see cref="Load"/>, applied in
    /// <see cref="Save"/> after the CSV diff has run.
    /// </summary>
    public ObservableCollection<TagEditRowViewModel> GenreTagRows { get; } = new();

    public ObservableCollection<TagEditRowViewModel> TagsTagRows { get; } = new();

    public bool HasGenreTagRows => GenreTagRows.Count > 0;

    public bool HasTagsTagRows => TagsTagRows.Count > 0;
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

    /// <summary>Weight picker options for <see cref="GenreTagRows"/>/<see cref="TagsTagRows"/> (docs/superpowers/specs/2026-08-23-weighted-categorized-tags-design.md).</summary>
    public static IssueTagWeight[] WeightOptions { get; } = Enum.GetValues<IssueTagWeight>();

    /// <summary>Replaces CE's per-issue Yes/No/Unknown <c>SeriesComplete</c> checkbox - had shipped
    /// data (docs/superpowers/specs/2026-08-17-metadata-model-phase1-canonical-metadata-design.md)
    /// with no editor UI until now (docs/superpowers/specs/2026-08-18-metadata-model-ui-gaps-status-
    /// and-bookmarks-design.md). A plain bool, not tri-state like CE's - CE's "Unknown" state doesn't
    /// have a real Paperbunkr equivalent here since this is a user-set flag, not inferred data.</summary>
    [ObservableProperty] private bool _isFinalIssue;

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

    // ===================== Copy / Paste fields (docs/ce-feature-inventory.md §A) =====================

    /// <summary>
    /// Plain snapshot of every copyable buffered field - deliberately excludes the Summary tab's
    /// read-only file info (<see cref="HeaderLabel"/>/<see cref="CoverImage"/>/<see
    /// cref="FilePathLabel"/>/<see cref="PageCountLabel"/>, all specific to the book the copy was
    /// taken from, never meaningful on another book) and the Category/Weight sub-rows (<see
    /// cref="GenreTagRows"/>/<see cref="TagsTagRows"/> - those re-derive from whichever Genre/Tags
    /// CSV value ends up staged, same as a fresh <see cref="Load"/> would, so pasting doesn't try to
    /// carry them across too). No CE precedent for this feature exists (verified against
    /// <c>ComicBookDialog.cs</c>/<c>MultipleComicBooksDialog.cs</c>) - it's a deliberate Paperbunkr
    /// addition per docs/ce-feature-inventory.md's 2026-08-07 triage, not a port.
    /// </summary>
    private sealed record FieldClipboard(
        int? MyRating, int? CommunityRating,
        string Number, string VolumeText, string CountText, string Title, string AlternateSeries,
        string AlternateNumber, string StoryArc, string StoryArcNumber, string SeriesGroup,
        string Publisher, string Imprint, string Format, string YearText, string MonthText,
        string DayText, string Genre, string Tags, string Writer, string Penciller, string Inker,
        string Colorist, string Letterer, string CoverArtist, string Editor, string Translator,
        string AgeRating, string LanguageIso, string ColorModeText, bool IsFinalIssue,
        string Characters, string Teams, string MainCharacterOrTeam, string Locations, string Web,
        string ScanInformation, string Summary, string Notes, string Review);

    /// <summary>
    /// Lives on this ViewModel instance, not a static/app-wide field - <see cref="MainViewModel"/>
    /// constructs exactly one <see cref="IssuePropertiesScreenViewModel"/> for the whole app session
    /// and reuses it across every edit (<see cref="Load"/> just re-populates it), so an instance
    /// field already survives "copy from book A, close, open book B" without needing global state.
    /// </summary>
    private FieldClipboard? _clipboard;

    public bool HasClipboard => _clipboard is not null;

    [RelayCommand]
    private void CopyFields()
    {
        _clipboard = new FieldClipboard(
            MyRating, CommunityRating, Number, VolumeText, CountText, Title, AlternateSeries,
            AlternateNumber, StoryArc, StoryArcNumber, SeriesGroup, Publisher, Imprint, Format,
            YearText, MonthText, DayText, Genre, Tags, Writer, Penciller, Inker, Colorist, Letterer,
            CoverArtist, Editor, Translator, AgeRating, LanguageIso, ColorModeText, IsFinalIssue,
            Characters, Teams, MainCharacterOrTeam, Locations, Web, ScanInformation, Summary, Notes,
            Review);
        OnPropertyChanged(nameof(HasClipboard));
        PasteFieldsCommand.NotifyCanExecuteChanged();
    }

    private bool CanPasteFields() => HasClipboard;

    [RelayCommand(CanExecute = nameof(CanPasteFields))]
    private void PasteFields()
    {
        if (_clipboard is not { } c)
        {
            return;
        }

        MyRating = c.MyRating;
        CommunityRating = c.CommunityRating;
        Number = c.Number;
        VolumeText = c.VolumeText;
        CountText = c.CountText;
        Title = c.Title;
        AlternateSeries = c.AlternateSeries;
        AlternateNumber = c.AlternateNumber;
        StoryArc = c.StoryArc;
        StoryArcNumber = c.StoryArcNumber;
        SeriesGroup = c.SeriesGroup;
        Publisher = c.Publisher;
        Imprint = c.Imprint;
        Format = c.Format;
        YearText = c.YearText;
        MonthText = c.MonthText;
        DayText = c.DayText;
        Genre = c.Genre;
        Tags = c.Tags;
        Writer = c.Writer;
        Penciller = c.Penciller;
        Inker = c.Inker;
        Colorist = c.Colorist;
        Letterer = c.Letterer;
        CoverArtist = c.CoverArtist;
        Editor = c.Editor;
        Translator = c.Translator;
        AgeRating = c.AgeRating;
        LanguageIso = c.LanguageIso;
        ColorModeText = c.ColorModeText;
        IsFinalIssue = c.IsFinalIssue;
        Characters = c.Characters;
        Teams = c.Teams;
        MainCharacterOrTeam = c.MainCharacterOrTeam;
        Locations = c.Locations;
        Web = c.Web;
        ScanInformation = c.ScanInformation;
        Summary = c.Summary;
        Notes = c.Notes;
        Review = c.Review;
    }

    // ===================== Templated/token text field editor (docs/ce-feature-inventory.md §A) =====================

    /// <summary>Token catalog for the Details tab's naming fields' token-insert flyouts.</summary>
    public static IReadOnlyList<string> TokenOptions => TemplateTokenCatalog.Names;

    /// <summary>
    /// Immediate evaluation, unlike the bulk editor's deferred per-issue expansion - this screen is
    /// always editing exactly one issue, so there's no ambiguity about which issue's values a token
    /// like "{Number}" means; reads whatever's currently buffered in this screen's own properties
    /// rather than the stale value <see cref="Load"/> first populated them with, so a token typed
    /// after editing another field (e.g. bumping Number before naming the Title) picks up the edit.
    /// </summary>
    private string ResolveTokenNow(string token) => token switch
    {
        "Series" => _seriesName,
        "Number" => Number,
        "Volume" => VolumeText,
        "Year" => YearText,
        "Title" => Title,
        "Publisher" => Publisher,
        _ => string.Empty,
    };

    [RelayCommand] private void InsertTokenIntoTitle(string token) => Title += ResolveTokenNow(token);
    [RelayCommand] private void InsertTokenIntoAlternateSeries(string token) => AlternateSeries += ResolveTokenNow(token);
    [RelayCommand] private void InsertTokenIntoStoryArc(string token) => StoryArc += ResolveTokenNow(token);
    [RelayCommand] private void InsertTokenIntoSeriesGroup(string token) => SeriesGroup += ResolveTokenNow(token);

    // ===================== Load / Save / Cancel =====================

    public void Load(int issueId, bool deleteIfUnedited = false)
    {
        _isLoading = true;
        _issueId = issueId;
        _deleteIfUnedited = deleteIfUnedited;

        using var context = _contextFactory();
        var issue = context.Issues.Include(i => i.Series).Include(i => i.MetadataProposals).Include(i => i.Tags).FirstOrDefault(i => i.Id == issueId);
        if (issue is null)
        {
            _isLoading = false;
            return;
        }

        _seriesName = issue.Series?.Name ?? string.Empty;
        _beforeSnapshot = MetadataEditHistoryService.CaptureSnapshot(issue);
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
        Genre = issue.JoinedGenre() ?? string.Empty;
        Tags = issue.JoinedTags() ?? string.Empty;
        LoadTagRows(GenreTagRows, issue.Tags, IssueTagField.Genre);
        LoadTagRows(TagsTagRows, issue.Tags, IssueTagField.Tags);
        OnPropertyChanged(nameof(HasGenreTagRows));
        OnPropertyChanged(nameof(HasTagsTagRows));
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
        IsFinalIssue = issue.IsFinalIssue;

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

    private static void LoadTagRows(ObservableCollection<TagEditRowViewModel> target, IEnumerable<IssueTag> tags, IssueTagField field)
    {
        target.Clear();
        foreach (var tag in tags.Where(t => t.Field == field).OrderBy(t => t.Value, StringComparer.OrdinalIgnoreCase))
        {
            target.Add(new TagEditRowViewModel(tag.Value, tag.Category, tag.Weight));
        }
    }

    /// <summary>
    /// Applies each row's edited Category/Weight onto the matching <see cref="IssueTag"/> - called
    /// after <see cref="Issue.MergeFrom"/> so it's applying onto the post-diff state. A row whose
    /// value no longer survives the diff (the user retyped/removed it in the CSV box) has nothing
    /// to match and is silently skipped - same "rename resets Category/Weight" tradeoff
    /// <see cref="Issue.MergeFrom"/> already accepts.
    /// </summary>
    private static void ApplyTagRows(Issue issue, IReadOnlyList<TagEditRowViewModel> rows, IssueTagField field)
    {
        var byValue = issue.Tags.Where(t => t.Field == field).ToDictionary(t => t.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (byValue.TryGetValue(row.Value, out var tag))
            {
                tag.Category = NullIfEmpty(row.Category);
                tag.Weight = row.Weight;
            }
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (_issueId is not int issueId)
        {
            return;
        }

        using var context = _contextFactory();
        // Not .Find() - that skips Include entirely, which would leave issue.Tags empty and make
        // MergeFrom below treat every value as brand new (duplicating rows and losing existing
        // Category/Weight) instead of diffing against what's actually there.
        // Include(Series) - needed by MetadataEditHistoryService.CaptureSnapshot below, whose
        // Content Type/Status/Reading Status fields reach through Issue.Series (docs/ce-feature-
        // inventory.md §A "Undo/Redo").
        var issue = context.Issues.Include(i => i.Series).Include(i => i.Tags).FirstOrDefault(i => i.Id == issueId);
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
        // Snapshot before the diff so Save can tell afterward whether the Genre/Tags *value set*
        // actually changed - a Category/Weight-only edit (via ApplyTagRows below, or the Detail
        // screen's reweight popover) never touches this, so it shouldn't trigger a file rewrite.
        string? genreBefore = issue.JoinedGenre();
        string? tagsBefore = issue.JoinedTags();

        issue.MergeFrom(IssueTagField.Genre, new[] { NullIfEmpty(Genre) });
        issue.MergeFrom(IssueTagField.Tags, new[] { NullIfEmpty(Tags) });
        ApplyTagRows(issue, GenreTagRows, IssueTagField.Genre);
        ApplyTagRows(issue, TagsTagRows, IssueTagField.Tags);

        string? genreAfter = issue.JoinedGenre();
        string? tagsAfter = issue.JoinedTags();
        bool tagValuesChanged = genreBefore != genreAfter || tagsBefore != tagsAfter;
        string? filePath = issue.FilePath;
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
        issue.IsFinalIssue = IsFinalIssue;

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

        // Undo/Redo (docs/ce-feature-inventory.md §A) - records this Save as one undoable entry.
        // Captured after SaveChanges so the "After" snapshot reflects what's actually now persisted,
        // not just this screen's own buffered properties.
        _history.Record($"Edited {HeaderLabel}", new() { [issueId] = _beforeSnapshot },
            new() { [issueId] = MetadataEditHistoryService.CaptureSnapshot(issue) });

        if (tagValuesChanged && filePath is not null)
        {
            TriggerComicInfoWriteBack(filePath, genreAfter, tagsAfter, _notify);
        }

        _goBack();
    }

    /// <summary>
    /// Fires the CBZ ComicInfo.xml write-back (docs/superpowers/specs/2026-08-23-weighted-
    /// categorized-tags-design.md) off the UI thread and never blocks Save/navigation on it - a full
    /// page re-encode is slow, and the database write above is already the source of truth
    /// regardless of whether the file rewrite succeeds. <see cref="ComicInfoWriteBackService"/>
    /// never throws (catches internally), so this is safe as true fire-and-forget. Static + a static
    /// helper on <see cref="ComicInfoWriteBackService"/> means bulk-edit's per-issue loop
    /// (<see cref="BulkIssuePropertiesScreenViewModel"/>) can call the exact same path.
    /// </summary>
    internal static void TriggerComicInfoWriteBack(string filePath, string? genre, string? tags, Action<string, string>? notify)
    {
        Task.Run(() =>
        {
            var outcome = ComicInfoWriteBackService.WriteGenreTags(filePath, genre, tags);
            if (outcome.Result == ComicInfoWriteBackResult.Success || notify is null)
            {
                return;
            }

            string fileName = Path.GetFileName(filePath);
            (string Title, string Message) toast = outcome.Result switch
            {
                ComicInfoWriteBackResult.SkippedNotCbz => ("Tags saved to library only",
                    $"{fileName} isn't a CBZ file, so its own ComicInfo.xml wasn't updated."),
                _ => ("Couldn't update the file", $"{fileName}: {outcome.ErrorMessage}"),
            };
            Dispatcher.UIThread.Post(() => notify(toast.Title, toast.Message));
        });
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
                CoverImageCache.Invalidate(issueId);
            }
        }

        _goBack();
    }
}
