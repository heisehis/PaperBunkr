using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
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
using Paperbunkr.Data.ReadingLists;
using Paperbunkr.Data.ReadingLists.Sources;

namespace Paperbunkr.App.ViewModels;

/// <summary>A list on the list page's cover rail (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §3, Q10).</summary>
public sealed partial class ReadingRailItem : ObservableObject
{
    public int ListId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? CoverKey { get; init; }

    public double Progress { get; init; }

    public bool IsActive { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCoverKey))]
    private Bitmap? _arcCover;

    public bool ShowCoverKey => ArcCover is null && CoverKey is not null;

    public string Tooltip => $"{Name} · {Math.Round(Progress * 100)}% read";
}

/// <summary>
/// An open reading list (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §3-§9): the cover rail, the streaming-style
/// hero, the Checks strip, the journey path or cover wall, Edit mode and the Add-issues drawer. Split into partials by concern; the data
/// paths are the ones the previous screen used (docs/superpowers/specs/2026-08-06-reading-lists-design.md onward), ported unchanged.
/// Edits persist immediately, as before - there is no Save/Cancel draft.
/// </summary>
public partial class ReadingListPageViewModel : ViewModelBase
{
    private readonly IFilePickerService _filePicker;
    private readonly Action<int, int> _goReaderForIssueInReadingList;
    private readonly Action<int> _openProperties;
    private readonly IActivityService _activity;
    private readonly IDialogService? _dialogs;
    private readonly ITrackerAutoSyncService _trackerAutoSync;
    private readonly Action<int?> _openGallery;
    private readonly Action<int> _openList;
    private readonly Action<string> _filterByTag;
    private int? _activeReadingListId;
    private List<ReadingListItemRowViewModel> _rows = new();

    public ReadingListPageViewModel(
        IFilePickerService filePicker,
        Action<int, int> goReaderForIssueInReadingList,
        Action<int>? openProperties,
        IActivityService activity,
        IDialogService? dialogs,
        ITrackerAutoSyncService trackerAutoSync,
        Action<int?> openGallery,
        Action<int> openList,
        Action<string> filterByTag)
    {
        _filePicker = filePicker;
        _goReaderForIssueInReadingList = goReaderForIssueInReadingList;
        _openProperties = openProperties ?? (_ => { });
        _activity = activity;
        _dialogs = dialogs;
        _trackerAutoSync = trackerAutoSync;
        _openGallery = openGallery;
        _openList = openList;
        _filterByTag = filterByTag;
    }

    public int? ActiveListId => _activeReadingListId;

    public bool IsListOpen => _activeReadingListId is not null;

    /// <summary>Every row in reading order (the path and cover wall are views of these).</summary>
    public IReadOnlyList<ReadingListItemRowViewModel> Rows => _rows;

    public ObservableCollection<ReadingRailItem> RailLists { get; } = new();

    /// <summary>Weighted/categorized tags on the list itself (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md).</summary>
    public ObservableCollection<TagPillViewModel> Tags { get; } = new();

    public bool HasTags => Tags.Count > 0;

    // --- Header state ---

    [ObservableProperty]
    private string _listName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSynopsis))]
    [NotifyPropertyChangedFor(nameof(SynopsisText))]
    [NotifyPropertyChangedFor(nameof(IsSynopsisLong))]
    private string _subtitle = string.Empty;

    public bool HasSynopsis => SynopsisText.Length > 0;

    /// <summary>The description as the hero shows it: section-heading lines ("Plot Summary", "Background") dropped, paragraphs kept.</summary>
    public string SynopsisText => CleanSynopsis(Subtitle);

    /// <summary>Long enough that the collapsed three lines cut it off, so the hero offers "more".</summary>
    public bool IsSynopsisLong => SynopsisText.Length > 240 || SynopsisText.Contains('\n');

    public int SynopsisMaxLines => SynopsisExpanded ? 0 : 3;

    /// <summary>
    /// ComicVine-sourced descriptions arrive as flattened HTML whose first line is a section heading ("Plot Summary", "Background") - the
    /// hero showed only that. A heading is a short line with no closing punctuation followed by a longer paragraph; those lines go, and the
    /// paragraphs are joined with blank lines. Short lines followed by other short lines (a plain list) are kept as they are.
    /// </summary>
    public static string CleanSynopsis(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        static bool LooksLikeHeading(string line) => line.Length <= 40 && line.Split(' ').Length <= 5 && !".!?:;,\"'”)…".Contains(line[^1]);

        var kept = new List<string>();
        for (int i = 0; i < lines.Count; i++)
        {
            // A run of up to three heading-like lines ("Background", "One World Under Doom") that ends at a real paragraph is dropped.
            int end = i;
            while (end < lines.Count && end - i < 3 && LooksLikeHeading(lines[end]))
            {
                end++;
            }

            if (end > i && end < lines.Count && lines[end].Length >= 60)
            {
                i = end - 1;
                continue;
            }

            kept.Add(lines[i]);
        }

        return string.Join("\n\n", kept);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTypeLabel))]
    [NotifyPropertyChangedFor(nameof(ShowFolderSeparator))]
    private string _typeLabel = string.Empty;

    private ReadingListType _listType;

    /// <summary>The hero's caption names the list's type only when it says something (not for an ordinary user list).</summary>
    public bool ShowTypeLabel => _listType != ReadingListType.User && TypeLabel.Length > 0;

    public bool ShowFolderSeparator => HasFolder && ShowTypeLabel;

    [ObservableProperty]
    private string? _linkedStoryEventName;

    [ObservableProperty]
    private string _createdAtLabel = string.Empty;

    /// <summary>The folder the list sits in (the caption link back to it in the gallery).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder))]
    private string? _folderName;

    public int? FolderId { get; private set; }

    public bool HasFolder => FolderName is not null;

    partial void OnFolderNameChanged(string? value) => OnPropertyChanged(nameof(ShowFolderSeparator));

    [RelayCommand]
    private void OpenFolder() => _openGallery(FolderId);

    [RelayCommand]
    private void OpenGalleryHome() => _openGallery(null);

    [RelayCommand]
    private void OpenRailList(ReadingRailItem? item)
    {
        if (item is not null && item.ListId != _activeReadingListId)
        {
            _openList(item.ListId);
        }
    }

    [RelayCommand]
    private void OpenProperties()
    {
        if (_activeReadingListId is int listId)
        {
            _openProperties(listId);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string? _statusMessage;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    [RelayCommand]
    private void DismissStatus() => StatusMessage = null;

    // --- Counts, progress, Continue ---

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _readCount;

    [ObservableProperty]
    private int _ownedCount;

    [ObservableProperty]
    private int _missingCount;

    public double ProgressFraction => TotalCount == 0 ? 0 : (double)ReadCount / TotalCount;

    public bool IsEmptyList => IsListOpen && TotalCount == 0;

    /// <summary>Caption under the progress bar.</summary>
    public string ProgressLabel => (ReadCount, TotalCount) switch
    {
        (_, 0) => "Empty",
        (0, var t) => $"Not started · {t} issues",
        (var r, var t) when r >= t => "Finished",
        (var r, var t) => $"{r} of {t} read" + ForecastSuffix,
    };

    /// <summary>The hero meta line: "31 issues · 12 read · 2 missing · at your pace, done by ~Nov 2026".</summary>
    public string MetaLine
    {
        get
        {
            var parts = new List<string> { TotalCount == 1 ? "1 issue" : $"{TotalCount} issues", $"{ReadCount} read" };
            if (MissingCount > 0)
            {
                parts.Add($"{MissingCount} missing");
            }

            string meta = string.Join(" · ", parts);
            return ForecastSuffix.Length > 0 ? meta + ForecastSuffix : meta;
        }
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(MetaLine));
        OnPropertyChanged(nameof(IsEmptyList));
    }

    /// <summary>The "Continue" target - first owned, unread item in reading order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContinueTarget))]
    private ReadingListItemRowViewModel? _continueTarget;

    [ObservableProperty]
    private string _continueLabel = string.Empty;

    public bool HasContinueTarget => ContinueTarget is not null;

    [RelayCommand]
    private void Continue() => ContinueTarget?.OpenCommand.Execute(null);

    [ObservableProperty]
    private bool _synopsisExpanded;

    public string SynopsisToggleLabel => SynopsisExpanded ? "less" : "more";

    partial void OnSynopsisExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(SynopsisToggleLabel));
        OnPropertyChanged(nameof(SynopsisMaxLines));
    }

    [RelayCommand]
    private void ToggleSynopsis() => SynopsisExpanded = !SynopsisExpanded;

    // --- Arc link, covers, hero backdrop ---

    [ObservableProperty]
    private bool _isArcLinked;

    [ObservableProperty]
    private string? _arcSourceLabel;

    [ObservableProperty]
    private string? _arcSourceName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCoverMosaic))]
    [NotifyPropertyChangedFor(nameof(ShowCoverGlyph))]
    private Bitmap? _arcCoverImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCoverMosaic))]
    [NotifyPropertyChangedFor(nameof(ShowCoverGlyph))]
    [NotifyPropertyChangedFor(nameof(CoverMosaicColumns))]
    private IReadOnlyList<string> _coverMosaicKeys = Array.Empty<string>();

    public bool ShowCoverMosaic => ArcCoverImage is null && CoverMosaicKeys.Count > 0;

    public bool ShowCoverGlyph => ArcCoverImage is null && CoverMosaicKeys.Count == 0;

    public int CoverMosaicColumns => CoverMosaicKeys.Count > 1 ? 2 : 1;

    /// <summary>The hero's blurred-cover backdrop (Q6), null until computed or when there is no cover.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHeroBackdrop))]
    private Bitmap? _heroBackdrop;

    public bool HasHeroBackdrop => HeroBackdrop is not null;

    /// <summary>Session cache of computed backdrops by list id - the blur is the expensive part.</summary>
    private static readonly Dictionary<int, Bitmap> BackdropCache = new();

    /// <summary>A list's blurred-cover backdrop if the list page has already made it (the properties overlay's header reuses it).</summary>
    public static Bitmap? CachedBackdrop(int listId) => BackdropCache.GetValueOrDefault(listId);

    [ObservableProperty]
    private bool _playEntranceAnimation;

    /// <summary>Raised after a load that should bring "Up next" into view (the view scrolls on the next layout pass).</summary>
    public event EventHandler? ScrollToUpNextRequested;

    // --- Load ---

    public void LoadReadingList(int readingListId, bool triggerEntrance = false)
    {
        bool switched = _activeReadingListId != readingListId;
        if (triggerEntrance)
        {
            PlayEntranceAnimation = true;
        }

        _activeReadingListId = readingListId;
        OnPropertyChanged(nameof(IsListOpen));
        OnPropertyChanged(nameof(ActiveListId));
        StatusMessage = null;
        if (switched)
        {
            IsEditing = false;
            CloseDrawer();
            ClearCanonicalPanel();
            RebuildNote = null;
            Compare = null;
        }

        using var context = PaperbunkrDb.CreateContext();
        var list = context.ReadingLists
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
            .Include(r => r.StoryEvent)
            .Include(r => r.Continuity)
            .Include(r => r.Folder)
            .Include(r => r.Tags)
            .FirstOrDefault(r => r.Id == readingListId);
        if (list is null)
        {
            _activeReadingListId = null;
            OnPropertyChanged(nameof(IsListOpen));
            _openGallery(null);
            return;
        }

        ListName = list.Name;
        if (switched)
        {
            SynopsisExpanded = false;
            CancelAddTag();
        }

        Subtitle = list.Description ?? string.Empty;
        _listType = list.Type;
        TypeLabel = ReadingListTypeOption.FormatLabel(list.Type);
        OnPropertyChanged(nameof(ShowTypeLabel));
        FolderId = list.FolderId;
        FolderName = list.Folder?.Name;
        LinkedStoryEventName = list.StoryEvent?.Name;
        CreatedAtLabel = $"Created {list.CreatedAt:MMM d, yyyy}";
        ContinuityLinkName = list.Continuity?.Name;
        _linkedContinuityId = list.ContinuityId;
        _linkedContinuityOrder = list.ContinuityOrderKind;
        _linkedStoryEvent = list.StoryEvent;
        RefreshCanonicalAvailability();

        ShowTags(readingListId, list.Tags);

        IsArcLinked = !string.IsNullOrEmpty(list.Source);
        _loadingFollowArc = true;
        FollowArc = IsArcLinked && list.FollowArc;
        _loadingFollowArc = false;
        ArcSourceLabel = IsArcLinked ? $"via {ReadingListSourceRegistry.GetDisplayName(list.Source!)}" : null;
        ArcSourceName = IsArcLinked ? ReadingListSourceRegistry.GetDisplayName(list.Source!) : null;
        ArcCoverImage = ArcCoverImageCache.Get(list.Id);
        if (ArcCoverImage is null && !string.IsNullOrEmpty(list.CoverImageUrl))
        {
            _ = LoadArcCoverAsync(list.Id, list.CoverImageUrl);
        }

        var items = list.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        CoverMosaicKeys = context.GetOrCreateAppSettings().ReadingListMosaic
            ? ReadingListCoverMosaic.PickCoverKeys(items
                .Select(i => i.Issue)
                .Where(issue => issue is { FilePath: not null })
                .Select(issue => CoverFingerprint.Stem(issue!.Id, issue.FilePath, issue.FileSize))
                .ToList())
            : Array.Empty<string>();

        _rows = items.Select(i => new ReadingListItemRowViewModel(i, MoveItemUp, MoveItemDown, RemoveItem, PersistFieldChange, StartLink, OpenIssue, ToggleReadRow, RequestItem)).ToList();
        foreach (var row in _rows)
        {
            // The note editor lives in the Edit-mode row template only; "Add a note" from reading mode switches to Edit mode.
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ReadingListItemRowViewModel.NoteEditing) && row.NoteEditing && !IsEditing)
                {
                    IsEditing = true;
                }
            };
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].Position = i + 1;
        }

        MemberSelection.Clear();
        RaiseSelectionState();

        TotalCount = _rows.Count;
        OwnedCount = _rows.Count(r => r.IsOwned);
        MissingCount = _rows.Count(r => r.IsMissing);
        ReadCount = _rows.Count(r => r.IsRead);
        UpdateForecast(_rows.Count(r => !r.IsRead), _rows.Count(r => !r.IsRead && r.IsMissing));
        RecomputeContinueTarget(_rows);
        RaiseCounts();

        if (switched || !_collapsedByList.ContainsKey(readingListId))
        {
            _collapsedByList[readingListId] = ReadingPathBuilder.DefaultCollapsed(_rows);
        }

        RebuildPath();
        RefreshRail(context);
        RefreshOverlaps(context);
        LoadHeroBackdrop(list.Id, items);

        if (switched || triggerEntrance)
        {
            ScrollToUpNextRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Reloads the open list (after an edit elsewhere).</summary>
    public void Reload()
    {
        if (_activeReadingListId is int id)
        {
            LoadReadingList(id);
        }
    }

    /// <summary>The lists sharing the open list's folder, in folder order, for the rail.</summary>
    public void RefreshRail(PaperbunkrDbContext context)
    {
        RailLists.Clear();
        if (_activeReadingListId is not int active)
        {
            return;
        }

        var siblings = context.ReadingLists.AsNoTracking()
            .Where(l => l.FolderId == FolderId)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
            .Include(l => l.Items).ThenInclude(i => i.Issue)
            .ToList();
        foreach (var list in siblings)
        {
            var ordered = list.Items.OrderBy(i => i.SortOrder).ToList();
            var first = ordered.Select(i => i.Issue).FirstOrDefault(i => i is { FilePath: not null });
            var item = new ReadingRailItem
            {
                ListId = list.Id,
                Name = list.Name,
                CoverKey = first is null ? null : CoverFingerprint.Stem(first.Id, first.FilePath, first.FileSize),
                Progress = ordered.Count == 0 ? 0 : (double)ordered.Count(i => i.Issue?.HasBeenRead() == true) / ordered.Count,
                IsActive = list.Id == active,
                ArcCover = ArcCoverImageCache.Get(list.Id),
            };
            RailLists.Add(item);
        }
    }

    // --- The list's own tags (hero): pills, "+ Tag" inline entry, remove from the pill's menu ---

    private void ShowTags(int readingListId, IEnumerable<ReadingListTag> tags)
    {
        Tags.Clear();
        foreach (var tag in tags.OrderBy(t => t.Value, StringComparer.OrdinalIgnoreCase))
        {
            Tags.Add(new TagPillViewModel(tag.Value, tag.Category, tag.Weight, _filterByTag, w => ReweightListTag(readingListId, tag.Value, w)));
        }

        OnPropertyChanged(nameof(HasTags));
    }

    [ObservableProperty]
    private bool _isAddingTag;

    [ObservableProperty]
    private string _newTagText = string.Empty;

    [RelayCommand]
    private void BeginAddTag()
    {
        NewTagText = string.Empty;
        IsAddingTag = true;
    }

    [RelayCommand]
    private void CancelAddTag()
    {
        IsAddingTag = false;
        NewTagText = string.Empty;
    }

    /// <summary>Adds the typed tag (commas separate several) to the open list; a value the list already has is skipped.</summary>
    [RelayCommand]
    private void CommitAddTag()
    {
        var values = NewTagText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        CancelAddTag();
        if (_activeReadingListId is not int listId || values.Count == 0)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var existing = context.ReadingListTags.Where(t => t.ReadingListId == listId).Select(t => t.Value).ToList();
        var fresh = values.Where(v => !existing.Contains(v, StringComparer.OrdinalIgnoreCase)).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        foreach (var value in fresh)
        {
            context.ReadingListTags.Add(new ReadingListTag { ReadingListId = listId, Value = value });
        }

        context.SaveChanges();
        ShowTags(listId, context.ReadingListTags.AsNoTracking().Where(t => t.ReadingListId == listId).ToList());
    }

    /// <summary>Removes a tag from the open list. Runs from the pill's own menu, so the pill leaves the hero a tick later (CLAUDE.md's
    /// don't-detach-mid-event rule).</summary>
    [RelayCommand]
    private void RemoveTag(TagPillViewModel? pill)
    {
        if (pill is null || _activeReadingListId is not int listId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            context.ReadingListTags.RemoveRange(context.ReadingListTags.Where(t => t.ReadingListId == listId && t.Value == pill.Value));
            context.SaveChanges();
        }

        Dispatcher.UIThread.Post(() =>
        {
            Tags.Remove(pill);
            OnPropertyChanged(nameof(HasTags));
        });
    }

    private void ReweightListTag(int readingListId, string value, IssueTagWeight weight)
    {
        using var context = PaperbunkrDb.CreateContext();
        var tag = context.ReadingListTags.FirstOrDefault(t => t.ReadingListId == readingListId && t.Value == value);
        if (tag is null)
        {
            return;
        }

        tag.Weight = weight;
        context.SaveChanges();
    }

    private async Task LoadArcCoverAsync(int readingListId, string coverImageUrl)
    {
        var bitmap = await ArcCoverImageCache.DownloadAndCacheAsync(readingListId, coverImageUrl, CancellationToken.None);
        if (bitmap is not null && _activeReadingListId == readingListId)
        {
            ArcCoverImage = bitmap;
            LoadHeroBackdrop(readingListId, null);
        }
    }

    /// <summary>The blurred-cover backdrop (Q6): the arc cover, else the first owned cover; blurred off the UI thread, cached per list.</summary>
    private void LoadHeroBackdrop(int listId, IReadOnlyList<ReadingListItem>? items)
    {
        if (BackdropCache.TryGetValue(listId, out var cached))
        {
            HeroBackdrop = cached;
            return;
        }

        HeroBackdrop = null;
        var arc = ArcCoverImage;
        var firstOwned = items?.Select(i => i.Issue).FirstOrDefault(i => i is { FilePath: not null, FileIsMissing: false });
        if (arc is null && firstOwned is null)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var source = arc ?? CoverImageCache.Get(firstOwned!.Id, firstOwned.FilePath, firstOwned.FileSize);
                return source is null ? null : BackdropBlurRenderer.Render(source, new PixelSize(1600, 380));
            }
            catch (Exception)
            {
                return null;        // a cover that can't be decoded just means no backdrop
            }
        }).ContinueWith(t =>
        {
            if (t.Result is { } backdrop)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    BackdropCache[listId] = backdrop;
                    if (_activeReadingListId == listId)
                    {
                        HeroBackdrop = backdrop;
                    }
                });
            }
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// "Continue" points at the first owned, not-yet-read item in reading order. If everything owned is read, it becomes a "re-read from
    /// start" pointing at the first owned item. If nothing is owned, there's no target and the button hides.
    /// </summary>
    private void RecomputeContinueTarget(IReadOnlyList<ReadingListItemRowViewModel> allRows)
    {
        foreach (var row in allRows)
        {
            row.IsNextUp = false;
        }

        var target = allRows.FirstOrDefault(r => r.IsOwned && !r.IsRead);
        if (target is not null)
        {
            ContinueLabel = target.IsInProgress ? $"Resume — {target.TitleLine}"
                : ReadCount == 0 ? $"Start reading — {target.TitleLine}"
                : $"Continue — {target.TitleLine}";
            target.IsNextUp = true;
        }
        else
        {
            target = allRows.FirstOrDefault(r => r.IsOwned);
            ContinueLabel = target is null ? string.Empty : "Re-read from start";
        }

        ContinueTarget = target;
    }

    // --- Row actions (reading mode and the row ⋯ menu) ---

    private void PersistFieldChange(ReadingListItemRowViewModel row)
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        var item = context.ReadingListItems.Find(row.Item.Id);
        var list = context.ReadingLists.Find(listId);
        if (item is null || list is null)
        {
            return;
        }

        item.Role = row.SelectedRole;
        MemberRoleApplier.CopyState(row.Item, item);
        item.Notes = row.Notes;
        list.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();
    }

    /// <summary>Manual mark-read / mark-unread for one row (docs/superpowers/specs/2026-08-23-mark-as-read-design.md).</summary>
    private void ToggleReadRow(ReadingListItemRowViewModel row)
    {
        if (row.Item.Issue is null)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            var issue = context.Issues.Find(row.Item.Issue.Id);
            if (issue is null)
            {
                return;
            }

            bool markingRead = !row.IsRead;
            if (row.IsRead)
            {
                IssueReadStateResolver.MarkAsUnread(issue);
            }
            else
            {
                IssueReadStateResolver.MarkAsRead(issue);
            }

            context.SaveChanges();
            if (markingRead)
            {
                _ = _trackerAutoSync.OnIssuesMarkedReadAsync(new[] { issue.SeriesId });
            }
        }

        // Deferred: the row's own menu item raised this and the reload rebuilds the path it lives in (CLAUDE.md runtime gotcha).
        Dispatcher.UIThread.Post(Reload);
    }

    private void MoveItemUp(ReadingListItemRowViewModel row) => Reorder(row, offset: -1);

    private void MoveItemDown(ReadingListItemRowViewModel row) => Reorder(row, offset: 1);

    private void Reorder(ReadingListItemRowViewModel row, int offset)
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        if (!ReadingListManager.MoveItem(context, listId, row.Item.Id, offset))
        {
            return;
        }

        context.SaveChanges();
        Dispatcher.UIThread.Post(Reload);
    }

    private void RemoveItem(ReadingListItemRowViewModel row)
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        if (ReadingListManager.RemoveItems(context, listId, new[] { row.Item.Id }) == 0)
        {
            return;
        }

        context.SaveChanges();
        Dispatcher.UIThread.Post(Reload);
    }

    /// <summary>Click-to-read; a missing row has Find &amp; link / Request instead.</summary>
    private void OpenIssue(ReadingListItemRowViewModel? row)
    {
        if (row is null || !row.IsOwned || _activeReadingListId is not int listId)
        {
            return;
        }

        _goReaderForIssueInReadingList(row.Item.IssueId, listId);
    }

    [RelayCommand]
    private void OpenRow(ReadingListItemRowViewModel? row) => OpenIssue(row);

    private static void BumpUpdatedAt(PaperbunkrDbContext context, int listId)
    {
        if (context.ReadingLists.Find(listId) is { } list)
        {
            list.UpdatedAt = DateTime.UtcNow;
        }
    }

    // --- Selection and bulk actions (Edit mode's bulk bar) ---

    public TileSelectionController<ReadingListItemRowViewModel> MemberSelection { get; } = new();

    public bool AnyMembersSelected => MemberSelection.Count > 0;

    public string MemberSelectionSummary => $"{MemberSelection.Count} selected";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BulkRoleText))]
    private EventMembershipRoleOption? _bulkRole;

    public string[] BulkRoleNames { get; } = ReadingListItemRowViewModel.RoleOptions.Select(o => o.Label).ToArray();

    public string BulkRoleText
    {
        get => BulkRole?.Label ?? string.Empty;
        set => BulkRole = ReadingListItemRowViewModel.RoleOptions.FirstOrDefault(o => o.Label == value);
    }

    private void RaiseSelectionState()
    {
        OnPropertyChanged(nameof(AnyMembersSelected));
        OnPropertyChanged(nameof(MemberSelectionSummary));
    }

    [RelayCommand]
    private void ToggleMemberSelection(ReadingListItemRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        MemberSelection.Toggle(_rows, row, isShiftHeld: false);
        RaiseSelectionState();
    }

    [RelayCommand]
    private void ClearMemberSelection()
    {
        MemberSelection.Clear(_rows);
        RaiseSelectionState();
    }

    [RelayCommand]
    private async Task RemoveSelectedMembers()
    {
        if (_activeReadingListId is not int listId || MemberSelection.Count == 0)
        {
            return;
        }

        int count = MemberSelection.Count;
        if (_dialogs is not null && !await _dialogs.ConfirmAsync(
                $"Remove {(count == 1 ? "1 issue" : $"{count} issues")} from \"{ListName}\"? The comics stay in your library.",
                title: "Remove from list", confirmLabel: "Remove", isDestructive: true))
        {
            return;
        }

        var ids = MemberSelection.SelectedIds.ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            ReadingListManager.RemoveItems(context, listId, ids);
            context.SaveChanges();
        }

        MemberSelection.Clear();
        RaiseSelectionState();
        Reload();
    }

    [RelayCommand]
    private void SetRoleForSelectedMembers()
    {
        if (_activeReadingListId is not int listId || MemberSelection.Count == 0 || BulkRole is null)
        {
            return;
        }

        var ids = MemberSelection.SelectedIds.ToList();
        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var item in context.ReadingListItems.Where(i => ids.Contains(i.Id)))
            {
                item.Role = BulkRole.Role;
                MemberRoleApplier.MarkUserSet(item);
            }

            BumpUpdatedAt(context, listId);
            context.SaveChanges();
        }

        MemberSelection.Clear();
        RaiseSelectionState();
        Reload();
    }

    [RelayCommand]
    private void MarkSelectedRead() => BulkMarkRead(read: true);

    [RelayCommand]
    private void MarkSelectedUnread() => BulkMarkRead(read: false);

    private void BulkMarkRead(bool read)
    {
        if (_activeReadingListId is null || MemberSelection.Count == 0)
        {
            return;
        }

        var issueIds = _rows
            .Where(r => MemberSelection.SelectedIds.Contains(r.Id) && r.Item.Issue is not null)
            .Select(r => r.Item.Issue!.Id)
            .ToList();

        using (var context = PaperbunkrDb.CreateContext())
        {
            foreach (var issue in context.Issues.Where(i => issueIds.Contains(i.Id)))
            {
                if (read)
                {
                    IssueReadStateResolver.MarkAsRead(issue);
                }
                else
                {
                    IssueReadStateResolver.MarkAsUnread(issue);
                }
            }

            context.SaveChanges();
            if (read)
            {
                _ = _trackerAutoSync.OnIssuesMarkedReadAsync(context.Issues.Where(i => issueIds.Contains(i.Id)).Select(i => i.SeriesId).Distinct().ToList());
            }
        }

        MemberSelection.Clear();
        RaiseSelectionState();
        Reload();
    }

    // --- Story event link (⋯ → Link story event…, a flyout search) ---

    public ObservableCollection<StoryEventSearchResult> StoryEventSearchResults { get; } = new();

    [ObservableProperty]
    private string _storyEventSearchQuery = string.Empty;

    partial void OnStoryEventSearchQueryChanged(string value) => SearchStoryEvents();

    [RelayCommand]
    private void SearchStoryEvents()
    {
        StoryEventSearchResults.Clear();
        if (string.IsNullOrWhiteSpace(StoryEventSearchQuery))
        {
            return;
        }

        using var context = PaperbunkrDb.CreateContext();
        foreach (var storyEvent in context.StoryEvents.AsEnumerable()
                     .Where(e => e.Name.Contains(StoryEventSearchQuery, StringComparison.OrdinalIgnoreCase))
                     .Take(20))
        {
            StoryEventSearchResults.Add(new StoryEventSearchResult { StoryEventId = storyEvent.Id, Name = storyEvent.Name });
        }
    }

    [ObservableProperty]
    private bool _isLinkingStoryEvent;

    [RelayCommand]
    private void ToggleLinkStoryEvent()
    {
        IsLinkingStoryEvent = !IsLinkingStoryEvent;
        StoryEventSearchQuery = string.Empty;
        StoryEventSearchResults.Clear();
    }

    [RelayCommand]
    private void LinkStoryEvent(StoryEventSearchResult? target)
    {
        if (target is null || _activeReadingListId is not int listId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            if (context.ReadingLists.Find(listId) is not { } list)
            {
                return;
            }

            list.StoryEventId = target.StoryEventId;
            list.UpdatedAt = DateTime.UtcNow;
            context.SaveChanges();
        }

        IsLinkingStoryEvent = false;
        StoryEventSearchQuery = string.Empty;
        StoryEventSearchResults.Clear();
        Dispatcher.UIThread.Post(Reload);
    }

    [RelayCommand]
    private void UnlinkStoryEvent()
    {
        if (_activeReadingListId is not int listId)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            if (context.ReadingLists.Find(listId) is not { } list)
            {
                return;
            }

            list.StoryEventId = null;
            list.UpdatedAt = DateTime.UtcNow;
            context.SaveChanges();
        }

        Reload();
    }

    public bool HasLinkedStoryEvent => LinkedStoryEventName is not null;

    partial void OnLinkedStoryEventNameChanged(string? value) => OnPropertyChanged(nameof(HasLinkedStoryEvent));
}
