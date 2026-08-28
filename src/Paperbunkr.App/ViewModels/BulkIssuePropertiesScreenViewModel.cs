using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Bulk multi-book properties editor (docs/superpowers/specs/2026-08-07-bulk-issue-editing-design.md),
/// reached via right-click when 2+ issue tiles are selected on the Detail screen. Same edit-buffer
/// Save/Cancel discipline as the single-book <see cref="IssuePropertiesScreenViewModel"/> - Cancel
/// never touches the database - but data-driven over <see cref="BulkFieldRegistry.All"/> instead of
/// one hand-written property per field, per the user's explicit choice given the field count here.
/// </summary>
public partial class BulkIssuePropertiesScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private readonly Action<string, string>? _notify;
    private List<int> _issueIds = new();
    private BulkFieldViewModel? _contentTypeField;
    private BulkFieldViewModel? _readingModeField;

    /// <summary>
    /// "Reading Direction" (docs/superpowers/specs/2026-08-16-manga-content-type-classification-
    /// design.md §2) - deliberately not in <see cref="BulkFieldRegistry.All"/>. Unlike every
    /// registry field, its row's visibility depends on a *different* field's (Content Type's) live
    /// value, which the flat, mutually-unaware <see cref="BulkFieldRegistry"/>/<see cref="AllFields"/>
    /// shape has no way to express - so this stays bespoke wiring specific to this one interaction
    /// rather than a second generalized "conditional field" registry concept. Still a real
    /// <see cref="BulkFieldDescriptor"/>/<see cref="BulkFieldViewModel"/> underneath, so it
    /// participates in the normal staging/Save loop (<see cref="AllFields"/> includes it once
    /// inserted into <see cref="MainFields"/>) with zero special-casing in <see cref="Save"/>.
    /// </summary>
    private static readonly BulkFieldDescriptor ReadingModeDescriptor = new(
        "Reading Direction", BulkFieldRegistry.Main, FieldKind.Enum, IsListField: false,
        i => i.Series.ReadingMode.ToString(),
        (i, v) => i.Series.ReadingMode = Enum.Parse<ReadingMode>(v ?? nameof(ReadingMode.LeftToRight)),
        Options: [nameof(ReadingMode.LeftToRight), nameof(ReadingMode.RightToLeft)]);

    private readonly MetadataEditHistoryService _history;

    /// <summary>Every selected issue's fields as of <see cref="Load"/>, before any edit - the Undo half of the entry <see cref="Save"/> records (docs/ce-feature-inventory.md §A "Undo/Redo").</summary>
    private Dictionary<int, Dictionary<string, string?>> _beforeSnapshots = new();

    public BulkIssuePropertiesScreenViewModel(Action goBack, Action<string, string>? notify = null, MetadataEditHistoryService? history = null)
        : this(goBack, PaperbunkrDb.CreateContext, notify, history)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal BulkIssuePropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory, Action<string, string>? notify = null, MetadataEditHistoryService? history = null)
    {
        _goBack = goBack;
        _contextFactory = contextFactory;
        _notify = notify;
        _history = history ?? MetadataEditHistoryService.Shared;
    }

    public ObservableCollection<BulkFieldViewModel> MainFields { get; } = new();
    public ObservableCollection<BulkFieldViewModel> ArtistFields { get; } = new();
    public ObservableCollection<BulkFieldViewModel> PlotNotesFields { get; } = new();

    private IEnumerable<BulkFieldViewModel> AllFields => MainFields.Concat(ArtistFields).Concat(PlotNotesFields);

    /// <summary>
    /// Reuses the existing per-field <see cref="BulkFieldViewModel.IsStaged"/> flag (already the
    /// signal <see cref="Save"/> itself uses to decide what to write) rather than adding a second,
    /// parallel dirty-tracking mechanism - unsaved-changes guard for <see cref="MainViewModel"/>
    /// (P6 follow-up, docs/alpha-todo.md).
    /// </summary>
    public bool HasUnsavedChanges() => AllFields.Any(f => f.IsStaged);

    [ObservableProperty]
    private string _headerLabel = string.Empty;

    public void Load(IReadOnlyList<int> issueIds)
    {
        _issueIds = issueIds.ToList();

        using var context = _contextFactory();
        // Include(Tags) - Genre/Tags' Get now reads Issue.Tags (docs/superpowers/specs/2026-08-23-
        // weighted-categorized-tags-design.md); without it every issue would show blank Genre/Tags
        // regardless of its real values.
        var issues = context.Issues.Include(i => i.Series).Include(i => i.Tags).Where(i => _issueIds.Contains(i.Id)).ToList();
        if (issues.Count == 0)
        {
            return;
        }

        string seriesName = issues[0].Series?.Name ?? "Unknown Series";
        HeaderLabel = $"Editing {issues.Count} issues in {seriesName}";

        _beforeSnapshots = issues.ToDictionary(i => i.Id, MetadataEditHistoryService.CaptureSnapshot);

        MainFields.Clear();
        ArtistFields.Clear();
        PlotNotesFields.Clear();

        BulkFieldViewModel PopulateField(BulkFieldDescriptor descriptor)
        {
            var field = new BulkFieldViewModel(descriptor);

            if (descriptor.IsListField)
            {
                var tokenSets = issues.Select(i => ListFieldTokens.Parse(descriptor.Get(i))).ToList();
                var intersection = tokenSets.Aggregate((a, b) => new HashSet<string>(a.Intersect(b, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase));
                field.OriginalTokens = intersection;
                field.Value = ListFieldTokens.Join(intersection);
            }
            else
            {
                var values = issues.Select(descriptor.Get).ToList();
                bool allSame = values.All(v => string.Equals(v, values[0], StringComparison.Ordinal));
                field.Value = allSame ? (values[0] ?? string.Empty) : string.Empty;
            }

            // Value assignment above auto-stages via BulkFieldViewModel.OnValueChanged - that's the
            // correct behavior for a user edit, but this is programmatic Load population, not one.
            field.IsStaged = false;
            return field;
        }

        foreach (var descriptor in BulkFieldRegistry.All)
        {
            var field = PopulateField(descriptor);

            var target = descriptor.Group switch
            {
                BulkFieldRegistry.Main => MainFields,
                BulkFieldRegistry.Artists => ArtistFields,
                _ => PlotNotesFields,
            };
            target.Add(field);

            if (descriptor.Label == BulkFieldRegistry.ContentTypeLabel)
            {
                _contentTypeField = field;
                field.PropertyChanged += OnContentTypeFieldPropertyChanged;

                // Inserted right after Content Type (docs/superpowers/specs/2026-08-16-manga-
                // content-type-classification-design.md §2) - populated from real data like every
                // other field, so it already shows the right value/visibility if the selection's
                // series are already Manga-family, not only when the user changes Content Type
                // during this session.
                _readingModeField = PopulateField(ReadingModeDescriptor);
                MainFields.Add(_readingModeField);
            }
        }

        _readingModeField!.IsRowVisible = ShowReadingModePicker;
        OnPropertyChanged(nameof(ShowReadingModePicker));
        OnPropertyChanged(nameof(SeriesAffectedCount));
        OnPropertyChanged(nameof(HasSeriesAffected));
    }

    private static readonly string[] MangaFamily = [nameof(ContentType.Manga), nameof(ContentType.Manhua), nameof(ContentType.Manhwa)];

    /// <summary>Content Type's own row stays visible/staged independent of this - only gates the bespoke Reading Direction row (see <see cref="ReadingModeDescriptor"/>'s own doc comment).</summary>
    public bool ShowReadingModePicker => _contentTypeField is not null && MangaFamily.Contains(_contentTypeField.Value);

    /// <summary>Distinct series among the current selection - Content Type writes through to each one's owning Series (docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md §1), which can silently span more series than were explicitly multi-selected.</summary>
    public int SeriesAffectedCount
    {
        get
        {
            using var context = _contextFactory();
            return context.Issues.Where(i => _issueIds.Contains(i.Id)).Select(i => i.SeriesId).Distinct().Count();
        }
    }

    public bool HasSeriesAffected => _contentTypeField?.IsStaged == true;

    private void OnContentTypeFieldPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BulkFieldViewModel.Value))
        {
            _readingModeField!.IsRowVisible = ShowReadingModePicker;
            OnPropertyChanged(nameof(ShowReadingModePicker));

            // First transition into a manga-family classification this session defaults Reading
            // Direction to RTL, per spec §2 - gated on IsStaged (has the *user* touched this row
            // yet this session), not on Value being blank: Load populates Value from real data even
            // when every selected issue's series already agrees on the schema default (LeftToRight),
            // so a blank-only guard would never fire for the common case of an as-yet-unclassified
            // series sitting at its default. Once staged (by this default or a real user pick), it's
            // never re-defaulted, so flipping Content Type back and forth never clobbers a choice.
            if (ShowReadingModePicker && _readingModeField is { IsStaged: false })
            {
                _readingModeField.Value = nameof(ReadingMode.RightToLeft);
            }
        }
        else if (e.PropertyName == nameof(BulkFieldViewModel.IsStaged))
        {
            OnPropertyChanged(nameof(HasSeriesAffected));
            OnPropertyChanged(nameof(SeriesAffectedCount));
        }
    }

    [RelayCommand]
    private void Save()
    {
        using var context = _contextFactory();
        // Include(Series) - the "Content Type"/"Reading Direction" fields' Set reaches through
        // Issue.Series (docs/superpowers/specs/2026-08-16-manga-content-type-classification-design.md
        // §1), which would otherwise be null here (this query didn't need it before those fields existed).
        // Include(Tags) - Genre/Tags' Set now diffs against Issue.Tags (docs/superpowers/specs/
        // 2026-08-23-weighted-categorized-tags-design.md); without it every issue would look like it
        // has no existing tags at all, so MergeFrom would re-add every value as brand new instead of
        // diffing, duplicating rows and losing any Category/Weight already set.
        var issues = context.Issues.Include(i => i.Series).Include(i => i.Tags).Where(i => _issueIds.Contains(i.Id)).ToList();
        var stagedFields = AllFields.Where(f => f.IsStaged).ToList();

        // Genre/Tags value-set changes trigger a CBZ ComicInfo.xml write-back after Save (docs/
        // superpowers/specs/2026-08-23-weighted-categorized-tags-design.md) - tracked per issue
        // since the shared added/removed token sets above don't tell you which specific issues in
        // the selection actually ended up with a different value (one issue's tokens might already
        // have included every "added" value and lacked every "removed" one).
        var genreOrTagsChangedIssueIds = new HashSet<int>();

        foreach (var field in stagedFields)
        {
            bool tracksFileWriteBack = field.Descriptor.Label is "Genre" or "Tags";

            if (field.Descriptor.IsListField)
            {
                var currentTokens = ListFieldTokens.Parse(field.Value);
                var added = new HashSet<string>(currentTokens.Except(field.OriginalTokens, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
                var removed = new HashSet<string>(field.OriginalTokens.Except(currentTokens, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

                foreach (var issue in issues)
                {
                    string? before = field.Descriptor.Get(issue);
                    var issueTokens = ListFieldTokens.Parse(before);
                    issueTokens.ExceptWith(removed);
                    issueTokens.UnionWith(added);
                    string joined = ListFieldTokens.Join(issueTokens);
                    field.Descriptor.Set(issue, joined);

                    if (tracksFileWriteBack && !string.Equals(before ?? string.Empty, joined, StringComparison.Ordinal))
                    {
                        genreOrTagsChangedIssueIds.Add(issue.Id);
                    }
                }
            }
            else
            {
                // Templated/token text field editor (docs/ce-feature-inventory.md §A) - a staged
                // value containing a placeholder like "{Series}" expands per issue here, right
                // before it's written; a plain value with no "{" passes through Expand unchanged.
                foreach (var issue in issues)
                {
                    field.Descriptor.Set(issue, TemplateTokenCatalog.Expand(field.Value, issue));
                }
            }
        }

        context.SaveChanges();

        // Re-derive the Character index for every edited issue (docs/superpowers/specs/2026-08-27-
        // metadata-model-phase4g-age-progression-design.md) - cheap no-op unless Characters changed.
        foreach (var issue in issues)
        {
            Paperbunkr.Data.Metadata.CharacterResolver.SyncFromIssue(context, issue.Id);
        }

        // Undo/Redo (docs/ce-feature-inventory.md §A) - only actually record an entry if some
        // field was staged; an all-cancelled-out edit (every staged field ends up equal to what it
        // started as) is harmless to skip, and avoids polluting the stack with no-op entries.
        if (stagedFields.Count > 0)
        {
            _history.Record($"Edited {issues.Count} issue{(issues.Count == 1 ? "" : "s")}",
                _beforeSnapshots, issues.ToDictionary(i => i.Id, MetadataEditHistoryService.CaptureSnapshot));
        }

        foreach (var issue in issues.Where(i => genreOrTagsChangedIssueIds.Contains(i.Id) && i.FilePath is not null))
        {
            IssuePropertiesScreenViewModel.TriggerComicInfoWriteBack(issue.FilePath!, issue.JoinedGenre(), issue.JoinedTags(), _notify);
        }

        // Matches IssuePropertiesScreenViewModel's _isDirty reset (P6 follow-up, docs/alpha-todo.md) -
        // without this, HasUnsavedChanges() would still report true immediately post-Save, until the
        // next Load() rebuilds the field list from scratch.
        foreach (var field in stagedFields)
        {
            field.IsStaged = false;
        }

        _goBack();
    }

    [RelayCommand]
    private void Cancel() => _goBack();
}
