using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Metadata;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Reading List properties overlay (docs/superpowers/specs/2026-08-23-reading-list-tags-design.md) -
/// a modal popup above the current screen (composited in MainWindow.axaml, same pattern as
/// MigrationOverlay), not a screen swap. Consolidates Name/Description/Type/arc-link editing
/// (Type was the only one of these with real inline editing before - see the design spec's
/// correction) plus the new Tags row and cover picker, all buffered Load/edit/Save/Cancel like
/// <see cref="IssuePropertiesScreenViewModel"/>.
/// </summary>
public partial class ReadingListPropertiesScreenViewModel : ViewModelBase
{
    private readonly Action _goBack;
    private readonly Func<PaperbunkrDbContext> _contextFactory;
    private int? _readingListId;

    /// <summary>Buffered until Save - picking a cover never touches disk until then, so Cancel can discard it for free (nothing was ever written).</summary>
    private string? _pendingCoverImagePath;

    public ReadingListPropertiesScreenViewModel(Action goBack) : this(goBack, PaperbunkrDb.CreateContext)
    {
    }

    /// <summary>Test-only seam - production always uses the default ctor (the real per-user database).</summary>
    internal ReadingListPropertiesScreenViewModel(Action goBack, Func<PaperbunkrDbContext> contextFactory)
    {
        _goBack = goBack;
        _contextFactory = contextFactory;
    }

    public static string[] TypeOptions { get; } = Enum.GetNames<ReadingListType>();

    [ObservableProperty]
    private string _headerLabel = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _typeText = string.Empty;

    [ObservableProperty]
    private string _source = string.Empty;

    [ObservableProperty]
    private string _arcId = string.Empty;

    [ObservableProperty]
    private string _arcName = string.Empty;

    [ObservableProperty]
    private string _coverImageUrl = string.Empty;

    /// <summary>The pending local pick (once chosen) or the list's existing cached cover - live preview only, nothing written until Save.</summary>
    [ObservableProperty]
    private Bitmap? _coverPreview;

    /// <summary>Plain CSV add/remove box, same idiom as the Issue Properties Editor's Genre/Tags fields.</summary>
    [ObservableProperty]
    private string _tagsText = string.Empty;

    public ObservableCollection<TagEditRowViewModel> TagRows { get; } = new();

    /// <summary>
    /// Covers already belonging to this list's own issues (docs/superpowers/specs/2026-09-17-
    /// reader-save-page-and-cover-picker-design.md's ask extended to reading lists) - "use what's
    /// already in the library" instead of only a file picker. Reuses <see cref="CoverPickerCandidate"/>
    /// even though this dialog isn't the issue-scoped <see cref="CoverPickerViewModel"/> - same
    /// shape (IssueId/Label/SourceKey), no need for a separate type.
    /// </summary>
    public ObservableCollection<CoverPickerCandidate> ListCoverCandidates { get; } = new();

    public bool HasListCoverCandidates => ListCoverCandidates.Count > 0;

    public void Load(int readingListId)
    {
        _readingListId = readingListId;
        _pendingCoverImagePath = null;

        using var context = _contextFactory();
        var list = context.ReadingLists.Include(r => r.Tags).FirstOrDefault(r => r.Id == readingListId);
        if (list is null)
        {
            return;
        }

        HeaderLabel = $"Edit \"{list.Name}\"";
        Name = list.Name;
        Description = list.Description ?? string.Empty;
        TypeText = list.Type.ToString();
        Source = list.Source ?? string.Empty;
        ArcId = list.ArcId ?? string.Empty;
        ArcName = list.ArcName ?? string.Empty;
        CoverImageUrl = list.CoverImageUrl ?? string.Empty;
        CoverPreview = ArcCoverImageCache.Get(readingListId);

        TagsText = list.JoinedTags() ?? string.Empty;
        TagRows.Clear();
        foreach (var tag in list.Tags.OrderBy(t => t.Value, StringComparer.OrdinalIgnoreCase))
        {
            TagRows.Add(new TagEditRowViewModel(tag.Value, tag.Category, tag.Weight));
        }

        ListCoverCandidates.Clear();
        var itemIssueIds = context.ReadingListItems
            .Where(ri => ri.ReadingListId == readingListId)
            .Select(ri => ri.IssueId)
            .ToList();
        foreach (int issueId in itemIssueIds)
        {
            if (CoverThumbnailService.GetEffectiveCoverPath(issueId) is null)
            {
                continue;
            }

            var issue = context.Issues.Include(i => i.Series).FirstOrDefault(i => i.Id == issueId);
            if (issue is null)
            {
                continue;
            }

            ListCoverCandidates.Add(new CoverPickerCandidate(issueId, $"{issue.Series?.Name ?? "Unknown"} #{issue.EffectiveNumber()}"));
        }

        OnPropertyChanged(nameof(HasListCoverCandidates));
    }

    [RelayCommand]
    private async Task ChangeCoverAsync()
    {
        // Not IFilePickerService - PickImageFileAsync is deliberately only on the concrete
        // FilePickerService (see its own doc comment), same call shape DetailScreenViewModel's
        // Issue-level "Change Cover" already uses.
        string? path = await new FilePickerService().PickImageFileAsync("Choose Cover Image");
        if (path is null)
        {
            return;
        }

        try
        {
            _pendingCoverImagePath = path;
            CoverPreview = new Bitmap(path);
        }
        catch
        {
            _pendingCoverImagePath = null;
        }
    }

    /// <summary>Stages a list-issue's own cover the same way <see cref="ChangeCoverAsync"/> stages a file pick - nothing written to <see cref="ArcCoverImageCache"/> until Save.</summary>
    [RelayCommand]
    private void SelectListCover(CoverPickerCandidate candidate)
    {
        string? path = CoverThumbnailService.GetEffectiveCoverPath(candidate.IssueId);
        if (path is null)
        {
            return;
        }

        try
        {
            _pendingCoverImagePath = path;
            CoverPreview = new Bitmap(path);
        }
        catch
        {
            _pendingCoverImagePath = null;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (_readingListId is not int readingListId)
        {
            return;
        }

        using var context = _contextFactory();
        var list = context.ReadingLists.Include(r => r.Tags).FirstOrDefault(r => r.Id == readingListId);
        if (list is null)
        {
            _goBack();
            return;
        }

        list.Name = Name;
        list.Description = NullIfEmpty(Description);
        list.Type = Enum.Parse<ReadingListType>(TypeText);
        list.Source = NullIfEmpty(Source);
        list.ArcId = NullIfEmpty(ArcId);
        list.ArcName = NullIfEmpty(ArcName);

        bool coverUrlChanged = !string.Equals(list.CoverImageUrl ?? string.Empty, CoverImageUrl, StringComparison.Ordinal);
        list.CoverImageUrl = NullIfEmpty(CoverImageUrl);

        list.MergeFrom(new[] { NullIfEmpty(TagsText) });
        ApplyTagRows(list, TagRows);

        list.UpdatedAt = DateTime.UtcNow;
        context.SaveChanges();

        // A local pick always wins over a same-session CoverImageUrl edit (docs/superpowers/specs/
        // 2026-08-23-reading-list-tags-design.md) - a deliberate local pick is a clearer signal of
        // intent than a URL edit silently overwriting it. Both write into the same on-disk cache
        // slot, so there's nothing to reconcile beyond "which one runs."
        if (_pendingCoverImagePath is not null)
        {
            ArcCoverImageCache.TrySetCustomCover(readingListId, _pendingCoverImagePath);
        }
        else if (coverUrlChanged && !string.IsNullOrWhiteSpace(list.CoverImageUrl))
        {
            _ = ArcCoverImageCache.DownloadAndCacheAsync(readingListId, list.CoverImageUrl, default);
        }

        _goBack();
    }

    [RelayCommand]
    private void Cancel() => _goBack();

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Applies each row's edited Category/Weight onto the matching post-diff ReadingListTag - same "rename resets Category/Weight" tradeoff as the Issue Properties Editor's identical helper.</summary>
    private static void ApplyTagRows(ReadingList list, IReadOnlyList<TagEditRowViewModel> rows)
    {
        var byValue = list.Tags.ToDictionary(t => t.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (byValue.TryGetValue(row.Value, out var tag))
            {
                tag.Category = NullIfEmpty(row.Category);
                tag.Weight = row.Weight;
            }
        }
    }
}
