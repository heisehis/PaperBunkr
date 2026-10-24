using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;
using Paperbunkr.Data.Metadata.Manga;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Edit Series overlay for a manga-family series (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §6, D11). Rooted in the series id, never in a chapter:
/// one Save changes the series, and every chapter that has no override of its own follows at once. The work (loading the series, finding chapters that already differ,
/// saving and passing values down) is <see cref="MangaSeriesMetadataService"/>'s; this is the edit buffer around it, with the same Save/Cancel discipline as the other
/// property overlays. Saving never waits on files: archive writes are queued afterwards and run in the background.
/// </summary>
public sealed partial class MangaSeriesEditorViewModel : ViewModelBase
{
    private readonly Action _goBack;
    private readonly MangaSeriesMetadataService _service;
    private readonly Action<string, string>? _notify;
    private readonly Action<int>? _enqueueWriteBack;
    private readonly Func<EmbeddedSyncActions?>? _syncActions;
    private readonly Func<PaperbunkrDbContext> _contextFactory;

    private MangaSeriesEdit? _edit;
    private int _seriesId;
    private string _loadSignature = string.Empty;
    private readonly HashSet<string> _unlockedKeys = new(StringComparer.Ordinal);

    public MangaSeriesEditorViewModel(Action goBack, Action<string, string>? notify = null, Action<int>? enqueueWriteBack = null, Func<EmbeddedSyncActions?>? syncActions = null)
        : this(goBack, new MangaSeriesMetadataService(PaperbunkrDb.CreateContext), notify, enqueueWriteBack, syncActions)
    {
    }

    /// <summary>Test seam: a service over a throwaway database.</summary>
    internal MangaSeriesEditorViewModel(Action goBack, MangaSeriesMetadataService service, Action<string, string>? notify = null, Action<int>? enqueueWriteBack = null, Func<EmbeddedSyncActions?>? syncActions = null, Func<PaperbunkrDbContext>? contextFactory = null, Func<ExternalMetadataProvider, IMetadataProvider?>? providers = null)
    {
        _goBack = goBack;
        _service = service;
        _notify = notify;
        _enqueueWriteBack = enqueueWriteBack;
        _syncActions = syncActions;
        Genres = new MangaTagListViewModel(MangaInheritedFields.Genre, "Genres", "Genre", key => _unlockedKeys.Add(key));
        Tags = new MangaTagListViewModel(MangaInheritedFields.Tags, "Tags", "Uncategorized", key => _unlockedKeys.Add(key));
        var factory = contextFactory ?? PaperbunkrDb.CreateContext;
        _contextFactory = factory;
        Sources = new MangaSeriesSourcesViewModel(
            new MangaSourceCandidates(factory, service), providers ?? MetadataProviderCatalog.For, factory, notify, enqueueWriteBack,
            HasUnsavedChanges, () => Load(_seriesId, keepTab: true));
    }

    /// <summary>The Sources tab: linked providers, refresh, and the candidates a refresh left to accept or reject.</summary>
    public MangaSeriesSourcesViewModel Sources { get; }

    // ===================== Identity =====================

    [ObservableProperty] private string _headerLabel = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _nativeTitle = string.Empty;
    [ObservableProperty] private string _romanizedTitle = string.Empty;
    [ObservableProperty] private string _localizedTitle = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _statusText = nameof(SeriesStatus.Unknown);

    public string[] StatusOptions { get; } = Enum.GetNames<SeriesStatus>();

    // ===================== Fields =====================

    /// <summary>The genres, a list of weighted rows (docs/superpowers/specs/2026-10-14-manga-series-metadata-design.md §14).</summary>
    public MangaTagListViewModel Genres { get; }

    /// <summary>The tags, a list of weighted rows.</summary>
    public MangaTagListViewModel Tags { get; }

    /// <summary>How the series is read, by the reader's own names (all seven modes).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadingModeDescription))]
    private string _readingModeText = ReadingModeCatalog.LabelFor(ReadingMode.LeftToRight);

    public string[] ReadingModeOptions => ReadingModeCatalog.Labels;

    public string ReadingModeDescription => ReadingModeCatalog.FromLabel(ReadingModeText) is ReadingMode mode ? ReadingModeCatalog.DescriptionFor(mode) : string.Empty;

    public ObservableCollection<MangaFieldRowViewModel> CreditRows { get; } = new();

    public ObservableCollection<MangaFieldRowViewModel> PublicationRows { get; } = new();

    public ObservableCollection<MangaFieldRowViewModel> ClassificationRows { get; } = new();

    private IEnumerable<MangaFieldRowViewModel> AllRows => CreditRows.Concat(PublicationRows).Concat(ClassificationRows);

    /// <summary>The chapters disagreed on at least one field when this series was opened; nothing is confirmed until Save.</summary>
    [ObservableProperty]
    private bool _needsReview;

    [ObservableProperty]
    private string _reviewSummary = string.Empty;

    // ===================== Advanced =====================

    [ObservableProperty] private bool _embeddedSyncEnabled = true;
    [ObservableProperty] private string _syncSummaryText = string.Empty;
    [ObservableProperty] private string _verifyResultText = string.Empty;
    [ObservableProperty] private bool _isVerifying;
    [ObservableProperty] private bool _hasSyncProblems;

    // ===================== Save state =====================

    [ObservableProperty] private bool _isSaving;

    /// <summary>Shown after Save finds chapters that already hold a different value, to ask Keep or Replace (design D9).</summary>
    [ObservableProperty] private bool _showExceptionPrompt;

    [ObservableProperty] private string _exceptionPromptText = string.Empty;

    // ===================== Tabs =====================

    [ObservableProperty] private string _activeTab = "identity";

    public bool IsIdentityTab => ActiveTab == "identity";
    public bool IsCreatorsTab => ActiveTab == "creators";
    public bool IsPublicationTab => ActiveTab == "publication";
    public bool IsClassificationTab => ActiveTab == "classification";
    public bool IsDescriptionTab => ActiveTab == "description";
    public bool IsSourcesTab => ActiveTab == "sources";
    public bool IsAdvancedTab => ActiveTab == "advanced";

    partial void OnActiveTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsIdentityTab));
        OnPropertyChanged(nameof(IsCreatorsTab));
        OnPropertyChanged(nameof(IsPublicationTab));
        OnPropertyChanged(nameof(IsClassificationTab));
        OnPropertyChanged(nameof(IsDescriptionTab));
        OnPropertyChanged(nameof(IsSourcesTab));
        OnPropertyChanged(nameof(IsAdvancedTab));
        if (value == "advanced")
        {
            RefreshSyncSummary();
        }
    }

    [RelayCommand] private void GoIdentity() => ActiveTab = "identity";
    [RelayCommand] private void GoCreators() => ActiveTab = "creators";
    [RelayCommand] private void GoPublication() => ActiveTab = "publication";
    [RelayCommand] private void GoClassification() => ActiveTab = "classification";
    [RelayCommand] private void GoDescription() => ActiveTab = "description";
    [RelayCommand] private void GoSources() => ActiveTab = "sources";
    [RelayCommand] private void GoAdvanced() => ActiveTab = "advanced";


    // ===================== Suggestions =====================

    /// <summary>Set when the library's suggestions have been read; tests wait on it.</summary>
    internal Task? VocabularyLoadTask { get; private set; }

    /// <summary>Genres a new series is likely to want, offered alongside whatever the library already uses.</summary>
    private static readonly string[] StarterGenres =
    {
        "Action", "Adventure", "Comedy", "Drama", "Ecchi", "Fantasy", "Historical", "Horror", "Isekai", "Martial Arts", "Mecha", "Mystery", "Psychological",
        "Romance", "School Life", "Sci-Fi", "Seinen", "Shoujo", "Shounen", "Slice of Life", "Sports", "Supernatural", "Thriller", "Tragedy",
    };

    /// <summary>The top-level groups MangaBaka sorts its tags under, so a category typed here matches what a refresh brings in.</summary>
    private static readonly string[] StarterCategories =
    {
        "Genre", "Themes", "Settings", "Activities", "Audience Demographics", "Character Archetype", "Character Traits", "Uncategorized",
    };

    private static VocabField? VocabFor(string key) => key switch
    {
        MangaInheritedFields.Publisher => VocabField.Publisher,
        MangaInheritedFields.Imprint => VocabField.Imprint,
        MangaInheritedFields.AgeRating => VocabField.AgeRating,
        "Writer" => VocabField.Writer,
        "Penciller" => VocabField.Penciller,
        "Inker" => VocabField.Inker,
        "Colorist" => VocabField.Colorist,
        "Letterer" => VocabField.Letterer,
        "CoverArtist" => VocabField.CoverArtist,
        "Editor" => VocabField.Editor,
        _ => null,
    };

    /// <summary>
    /// Reads the library's own publishers, people, genres, tags and tag categories off the UI thread, then gives each box its list. The boxes work (without suggestions) until it lands,
    /// the same way the Issue Properties editor behaves.
    /// </summary>
    private async Task LoadVocabularyAsync()
    {
        try
        {
            var (vocabulary, categories) = await Task.Run(() =>
            {
                using var context = _contextFactory();
                var vocab = MetadataVocabularyService.Build(context);
                var cats = context.IssueTags.AsNoTracking().Select(t => t.Category).Distinct().ToList()
                    .Concat(context.SeriesTagRows.AsNoTracking().Select(t => t.Category).Distinct().ToList())
                    .Concat(StarterCategories)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return (vocab, (IReadOnlyList<string>)cats);
            });

            foreach (var row in AllRows)
            {
                if (VocabFor(row.Key) is VocabField field)
                {
                    row.Suggestions = vocabulary[field];
                }
            }

            Genres.Suggestions = vocabulary[VocabField.Genre].Concat(StarterGenres).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
            Tags.Suggestions = vocabulary[VocabField.Tags];
            Genres.CategorySuggestions = categories;
            Tags.CategorySuggestions = categories;
        }
        catch (Exception)
        {
            // Suggestions are a convenience: a failure leaves the boxes as plain text boxes.
        }
    }
    // ===================== Load =====================

    /// <summary>
    /// Loads <paramref name="seriesId"/> into the buffer. Returns false, and the caller does not open the editor, for a series that does not exist or belongs to another
    /// library (read-only here).
    /// </summary>
    public bool Load(int seriesId, bool keepTab = false)
    {
        _seriesId = seriesId;
        _unlockedKeys.Clear();
        ShowExceptionPrompt = false;
        VerifyResultText = string.Empty;
        if (!keepTab)
        {
            ActiveTab = "identity";
        }

        _edit = null;

        var edit = _service.Load(seriesId);
        if (edit is null)
        {
            return false;
        }

        if (edit.IsRemote)
        {
            _notify?.Invoke("Read-only series", "This series belongs to another library, so it can't be edited here.");
            return false;
        }

        _edit = edit;
        HeaderLabel = $"Edit series “{edit.Name}”";
        Name = edit.Name;
        Summary = edit.Summary ?? string.Empty;
        StatusText = edit.Status.ToString();
        NativeTitle = JoinTitles(edit, SeriesTitleType.Native);
        RomanizedTitle = JoinTitles(edit, SeriesTitleType.Romanized);
        LocalizedTitle = JoinTitles(edit, SeriesTitleType.Localized);
        EmbeddedSyncEnabled = edit.EmbeddedSyncEnabled;

        CreditRows.Clear();
        PublicationRows.Clear();
        ClassificationRows.Clear();
        foreach (var field in MangaInheritedFields.All.Where(f => f.Kind != MangaFieldKind.Tags))
        {
            var row = new MangaFieldRowViewModel(field, TextFor(edit.Values, field), NoteFor(edit, field.Key), edit.LockedFields.Contains(field.Key), r => _unlockedKeys.Add(r.Key));
            (field.Kind switch
            {
                MangaFieldKind.Credit => CreditRows,
                _ when field.Key is MangaInheritedFields.Publisher or MangaInheritedFields.Imprint => PublicationRows,
                _ => ClassificationRows,
            }).Add(row);
        }

        Genres.Load(edit.Values.Genres, edit.LockedFields.Contains(MangaInheritedFields.Genre), NoteFor(edit, MangaInheritedFields.Genre));
        Tags.Load(edit.Values.Tags, edit.LockedFields.Contains(MangaInheritedFields.Tags), NoteFor(edit, MangaInheritedFields.Tags));
        ReadingModeText = ReadingModeCatalog.LabelFor(edit.ReadingMode);

        NeedsReview = edit.NeedsReview;
        ReviewSummary = edit.NeedsReview
            ? $"The chapters of this series disagree on {string.Join(", ", edit.ReviewCandidates.Keys.Select(k => MangaInheritedFields.Find(k)?.Label ?? k))}. The most common value is filled in; saving confirms what you see."
            : string.Empty;

        _loadSignature = Signature();
        RefreshSyncSummary();
        Sources.Load(seriesId);
        VocabularyLoadTask = LoadVocabularyAsync();
        return true;
    }

    private static string JoinTitles(MangaSeriesEdit edit, SeriesTitleType type) => string.Join("; ", edit.Titles.Where(t => t.Type == type).Select(t => t.Value));

    private static string? NoteFor(MangaSeriesEdit edit, string key) =>
        edit.ReviewCandidates.TryGetValue(key, out var values) ? $"Chapters disagree: {string.Join(" · ", values.Select(v => v.Length == 0 ? "(blank)" : v))}. The first is the most common." : null;

    private static string TextFor(MangaFieldValues values, MangaField field) => field.Kind switch
    {
        MangaFieldKind.Text => values.Text.GetValueOrDefault(field.Key) ?? string.Empty,
        MangaFieldKind.Credit => string.Join(", ", values.Credits.GetValueOrDefault(field.Key) ?? new List<string>()),
        _ => string.Join(", ", (field.TagField == IssueTagField.Genre ? values.Genres : values.Tags).Select(t => t.Value)),
    };

    public bool HasUnsavedChanges() => _edit is not null && Signature() != _loadSignature;

    private string Signature() => string.Join('␟',
        Name, Summary, StatusText, NativeTitle, RomanizedTitle, LocalizedTitle, EmbeddedSyncEnabled, string.Join(',', _unlockedKeys.OrderBy(k => k)), ReadingModeText, Genres.Signature, Tags.Signature,
        string.Join('␞', AllRows.Select(r => r.Text)));

    // ===================== Save =====================

    /// <summary>Copies the buffer into the edit object the service saves.</summary>
    private MangaSeriesEdit BuildEdit()
    {
        var edit = _edit!;
        edit.Name = Name;
        edit.Summary = string.IsNullOrWhiteSpace(Summary) ? null : Summary.Trim();
        edit.Status = Enum.TryParse<SeriesStatus>(StatusText, out var status) ? status : edit.Status;
        edit.EmbeddedSyncEnabled = EmbeddedSyncEnabled;

        var titles = new List<MangaTitleEdit>();
        AddTitles(titles, SeriesTitleType.Native, NativeTitle);
        AddTitles(titles, SeriesTitleType.Romanized, RomanizedTitle);
        AddTitles(titles, SeriesTitleType.Localized, LocalizedTitle);
        edit.Titles = titles;

        var values = new MangaFieldValues();
        foreach (var row in AllRows)
        {
            switch (row.Field.Kind)
            {
                case MangaFieldKind.Text:
                    values.Text[row.Key] = MangaInheritedFields.NullIfBlank(row.Text);
                    break;
                case MangaFieldKind.Credit:
                    values.Credits[row.Key] = CreatorResolver.ParseNames(row.Text).ToList();
                    break;
            }
        }

        // Genres and tags are weighted lists of their own, not text boxes.
        values.Genres.AddRange(Genres.ToValues());
        values.Tags.AddRange(Tags.ToValues());
        edit.ReadingMode = ReadingModeCatalog.FromLabel(ReadingModeText) ?? edit.ReadingMode;
        edit.Values = values;
        edit.UnlockedKeys = new HashSet<string>(_unlockedKeys, StringComparer.Ordinal);
        return edit;
    }

    private static void AddTitles(List<MangaTitleEdit> titles, SeriesTitleType type, string text)
    {
        foreach (string part in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            titles.Add(new MangaTitleEdit(type, part));
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (_edit is null || IsSaving)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            _notify?.Invoke("Can't save", "The series needs a name.");
            return;
        }

        var edit = BuildEdit();
        IsSaving = true;
        try
        {
            var exceptions = await Task.Run(() => _service.PreviewExceptions(edit));
            if (exceptions.Count > 0)
            {
                ExceptionPromptText = "Some chapters already hold a different value: "
                    + string.Join(", ", exceptions.Select(e => $"{e.Label} on {e.ChapterCount} chapter{(e.ChapterCount == 1 ? "" : "s")}"))
                    + ". Keep them as they are (they become exceptions that later series edits leave alone), or replace them with the series value?";
                ShowExceptionPrompt = true;
                return;
            }

            await CommitAsync(edit, MangaExceptionChoice.KeepAsExceptions);
        }
        catch (Exception ex)
        {
            _notify?.Invoke("Couldn't save", ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private Task KeepAsExceptions() => ResolvePrompt(MangaExceptionChoice.KeepAsExceptions);

    [RelayCommand]
    private Task ReplaceAll() => ResolvePrompt(MangaExceptionChoice.ReplaceAll);

    [RelayCommand]
    private void DismissExceptionPrompt() => ShowExceptionPrompt = false;

    private async Task ResolvePrompt(MangaExceptionChoice choice)
    {
        if (_edit is null || IsSaving)
        {
            return;
        }

        ShowExceptionPrompt = false;
        IsSaving = true;
        try
        {
            await CommitAsync(BuildEdit(), choice);
        }
        catch (Exception ex)
        {
            _notify?.Invoke("Couldn't save", ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private async Task CommitAsync(MangaSeriesEdit edit, MangaExceptionChoice choice)
    {
        var result = await Task.Run(() => _service.Save(edit, choice));
        if (_enqueueWriteBack is not null)
        {
            foreach (int issueId in result.ChangedIssueIds)
            {
                _enqueueWriteBack(issueId);
            }
        }

        _loadSignature = Signature();
        _goBack();
    }

    [RelayCommand]
    private void Cancel()
    {
        if (ShowExceptionPrompt)
        {
            ShowExceptionPrompt = false;
            return;
        }

        _goBack();
    }

    // ===================== Advanced: sync =====================

    private void RefreshSyncSummary()
    {
        var actions = _syncActions?.Invoke();
        if (actions is null || _seriesId == 0)
        {
            SyncSummaryText = string.Empty;
            HasSyncProblems = false;
            return;
        }

        var summary = actions.GetSummary(_seriesId);
        SyncSummaryText = summary.Describe();
        HasSyncProblems = summary.HasProblems;
    }

    [RelayCommand]
    private void RetryFailed()
    {
        int queued = _syncActions?.Invoke()?.RetryFailed(_seriesId) ?? 0;
        _notify?.Invoke("Retrying", queued == 0 ? "Nothing needed retrying." : $"{queued} chapter{(queued == 1 ? "" : "s")} queued to be written again.");
        RefreshSyncSummary();
    }

    [RelayCommand]
    private void WriteAnyway()
    {
        int queued = _syncActions?.Invoke()?.WriteAnyway(_seriesId) ?? 0;
        _notify?.Invoke("Writing", queued == 0 ? "No files were changed outside Paperbunkr." : $"{queued} file{(queued == 1 ? "" : "s")} queued to be replaced with the library's metadata.");
        RefreshSyncSummary();
    }

    [RelayCommand]
    private async Task Verify()
    {
        var actions = _syncActions?.Invoke();
        if (actions is null || IsVerifying)
        {
            return;
        }

        IsVerifying = true;
        VerifyResultText = "Checking the files…";
        try
        {
            var result = await actions.VerifyAsync(_seriesId);
            VerifyResultText = result.Describe();
        }
        catch (Exception ex)
        {
            VerifyResultText = $"Couldn't check: {ex.Message}";
        }
        finally
        {
            IsVerifying = false;
        }
    }
}
