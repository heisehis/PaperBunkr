using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Reading Lists gallery home (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §2, decisions S3/H1/Q11/Q17/Q18):
/// a "Continue reading" strip, then one grid of folder and list tiles for the current folder (folders first, in the manual order kept by
/// <see cref="ReadingListFolders"/>), a breadcrumb, and tag chips that filter every list flat. Folder semantics follow ComicRack CE's list
/// folders (see the organize spec); the gallery presentation itself is a Paperbunkr deviation.
/// </summary>
public partial class ReadingGalleryViewModel : ViewModelBase
{
    public const int MaxTagChips = 12;

    private readonly IFilePickerService _filePicker;
    private readonly IActivityService _activity;
    private readonly IDialogService? _dialogs;
    private readonly Action<int> _openList;
    private readonly Func<ReadingListExportRequest, Task> _exportList;

    private List<ReadingListFolder> _folders = new();
    private List<ListStats> _lists = new();

    /// <summary>What the gallery knows about each list after <see cref="Refresh"/>.</summary>
    private sealed record ListStats(
        int Id, string Name, int? FolderId, int SortOrder, int Total, int Read, int OwnedUnread, IReadOnlyList<string> CoverKeys,
        string? CoverImageUrl, IReadOnlyList<string> Tags, string? NextLabel, string? FirstCoverKey);

    public ReadingGalleryViewModel(IFilePickerService filePicker, IActivityService activity, IDialogService? dialogs, Action<int> openList,
        Func<ReadingListExportRequest, Task> exportList)
    {
        _filePicker = filePicker;
        _activity = activity;
        _dialogs = dialogs;
        _openList = openList;
        _exportList = exportList;
    }

    public ObservableCollection<ReadingGalleryTile> Tiles { get; } = new();

    public ObservableCollection<ReadingContinueCard> ContinueCards { get; } = new();

    public ObservableCollection<ReadingBreadcrumb> Breadcrumb { get; } = new();

    public ObservableCollection<ReadingTagChip> TagChips { get; } = new();

    /// <summary>The folder being shown; null = the top level.</summary>
    [ObservableProperty]
    private int? _currentFolderId;

    /// <summary>The tag chip picked; while set, the grid shows every matching list flat and the Continue row hides.</summary>
    [ObservableProperty]
    private string? _activeTag;

    [ObservableProperty]
    private int _moreTagCount;

    public bool HasMoreTags => MoreTagCount > 0;

    public string MoreTagsLabel => $"+{MoreTagCount}";

    partial void OnMoreTagCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasMoreTags));
        OnPropertyChanged(nameof(MoreTagsLabel));
    }

    public bool ShowContinue => ContinueCards.Count > 0;

    public bool HasNoLists => _lists.Count == 0 && _folders.Count == 0;

    public bool IsEmptyFolder => !HasNoLists && Tiles.Count == 0;

    public bool IsInFolder => CurrentFolderId is not null;

    public string? CurrentFolderName => CurrentFolderId is int id ? _folders.FirstOrDefault(f => f.Id == id)?.Name : null;

    /// <summary>Loads folders and lists and rebuilds the current view.</summary>
    public void Refresh()
    {
        using var context = PaperbunkrDb.CreateContext();
        _folders = context.ReadingListFolders.AsNoTracking().ToList();
        var lists = context.ReadingLists.AsNoTracking()
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.Series)
            .Include(r => r.Items).ThenInclude(i => i.Issue).ThenInclude(i => i!.MetadataProposals)
            .Include(r => r.Tags)
            .ToList();

        _lists = lists.Select(list =>
        {
            var items = list.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
            var coverKeys = items.Select(i => i.Issue).Where(i => i is { FilePath: not null })
                .Select(i => CoverFingerprint.Stem(i!.Id, i.FilePath, i.FileSize)).ToList();
            var next = items.Select(i => i.Issue).FirstOrDefault(i => i is { FileIsMissing: false } && !i.HasBeenRead());
            return new ListStats(
                list.Id, list.Name, list.FolderId, list.SortOrder, items.Count,
                items.Count(i => i.Issue?.HasBeenRead() == true),
                items.Count(i => i.Issue is { FileIsMissing: false } issue && !issue.HasBeenRead()),
                ReadingListCoverMosaic.PickCoverKeys(coverKeys).Take(4).ToList(),
                list.CoverImageUrl,
                list.Tags.Select(t => t.Value).ToList(),
                next is null ? null : $"{next.Series?.Name ?? "Unknown"} #{next.EffectiveNumber()}",
                coverKeys.FirstOrDefault());
        }).ToList();

        if (CurrentFolderId is int current && _folders.All(f => f.Id != current))
        {
            CurrentFolderId = null;
        }

        RebuildContinue(context);
        RebuildTagChips();
        RebuildTiles();
        RebuildBreadcrumb();
        OnPropertyChanged(nameof(HasNoLists));
        OnPropertyChanged(nameof(IsEmptyFolder));
        OnPropertyChanged(nameof(IsInFolder));
        OnPropertyChanged(nameof(CurrentFolderName));
    }

    private void RebuildContinue(PaperbunkrDbContext context)
    {
        ContinueCards.Clear();
        var eligible = _lists.Where(l => l.Read > 0 && l.OwnedUnread > 0).ToList();
        if (eligible.Count > 0)
        {
            var memberIssues = context.ReadingListItems.AsNoTracking()
                .Where(i => eligible.Select(l => l.Id).Contains(i.ReadingListId))
                .Select(i => new { i.ReadingListId, i.IssueId })
                .ToList();
            var issueIds = memberIssues.Select(m => m.IssueId).Distinct().ToList();
            var lastByIssue = context.ReadingEvents.AsNoTracking()
                .Where(e => e.ItemType == ReadingItemType.Comic && issueIds.Contains(e.ItemId))
                .GroupBy(e => e.ItemId)
                .Select(g => new { IssueId = g.Key, Last = g.Max(e => e.TimestampUtc) })
                .ToDictionary(x => x.IssueId, x => x.Last);
            var lastByList = memberIssues
                .GroupBy(m => m.ReadingListId)
                .ToDictionary(g => g.Key, g => g.Select(m => lastByIssue.TryGetValue(m.IssueId, out var t) ? t : (DateTime?)null).Max());

            var order = ReadingContinueRow.Select(eligible.Select(l =>
                new ContinueCandidate(l.Id, l.Read, l.OwnedUnread, lastByList.GetValueOrDefault(l.Id))));
            foreach (int id in order)
            {
                var l = _lists.First(x => x.Id == id);
                ContinueCards.Add(new ReadingContinueCard
                {
                    ListId = l.Id,
                    Name = l.Name,
                    CoverKey = l.FirstCoverKey,
                    NextLine = $"next: {l.NextLabel} · {l.Read}/{l.Total}",
                });
            }
        }

        OnPropertyChanged(nameof(ShowContinue));
        OnPropertyChanged(nameof(ShowContinueRow));
    }

    private void RebuildTagChips()
    {
        TagChips.Clear();
        var counts = _lists.SelectMany(l => l.Tags.Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.Key)
            .ToList();
        if (counts.Count == 0)
        {
            MoreTagCount = 0;
            return;
        }

        TagChips.Add(new ReadingTagChip(null, "All", ActiveTag is null));
        foreach (var tag in counts.Take(MaxTagChips))
        {
            TagChips.Add(new ReadingTagChip(tag, "#" + tag, string.Equals(tag, ActiveTag, StringComparison.OrdinalIgnoreCase)));
        }

        MoreTagCount = Math.Max(0, counts.Count - MaxTagChips);
    }

    private void RebuildTiles()
    {
        Tiles.Clear();
        if (ActiveTag is { } tag)
        {
            foreach (var list in _lists.Where(l => l.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Tiles.Add(ListTile(list));
            }
        }
        else
        {
            var parents = _folders.ToDictionary(f => f.Id, f => f.ParentFolderId);
            foreach (var folder in _folders.Where(f => f.ParentFolderId == CurrentFolderId).OrderBy(f => f.SortOrder).ThenBy(f => f.Id))
            {
                var inside = _lists.Where(l => IsUnder(l.FolderId, folder.Id, parents)).ToList();
                int total = inside.Sum(l => l.Total);
                int read = inside.Sum(l => l.Read);
                int percent = total == 0 ? 0 : (int)Math.Round(100.0 * read / total);
                Tiles.Add(new ReadingGalleryTile
                {
                    IsFolder = true,
                    Id = folder.Id,
                    Name = folder.Name,
                    ParentFolderId = folder.ParentFolderId,
                    SortOrder = folder.SortOrder,
                    SubLine = $"{(inside.Count == 1 ? "1 list" : $"{inside.Count} lists")} · {percent}%",
                    Progress = total == 0 ? 0 : (double)read / total,
                    CoverKeys = inside.OrderBy(l => l.SortOrder).Select(l => l.FirstCoverKey).OfType<string>().Take(4).ToList(),
                });
            }

            foreach (var list in _lists.Where(l => l.FolderId == CurrentFolderId).OrderBy(l => l.SortOrder).ThenBy(l => l.Id))
            {
                Tiles.Add(ListTile(list));
            }
        }

        OnPropertyChanged(nameof(IsEmptyFolder));
    }

    private ReadingGalleryTile ListTile(ListStats list)
    {
        var tile = new ReadingGalleryTile
        {
            Id = list.Id,
            Name = list.Name,
            ParentFolderId = list.FolderId,
            SortOrder = list.SortOrder,
            SubLine = $"{list.Read} / {list.Total} read",
            Progress = list.Total == 0 ? 0 : (double)list.Read / list.Total,
            CoverKeys = list.CoverKeys,
            Tags = list.Tags,
            ArcCover = ArcCoverImageCache.Get(list.Id),
        };
        if (tile.ArcCover is null && !string.IsNullOrEmpty(list.CoverImageUrl))
        {
            _ = LoadArcCoverAsync(tile, list.CoverImageUrl);
        }

        return tile;
    }

    private static async Task LoadArcCoverAsync(ReadingGalleryTile tile, string url)
    {
        var bitmap = await ArcCoverImageCache.DownloadAndCacheAsync(tile.Id, url, System.Threading.CancellationToken.None);
        if (bitmap is not null)
        {
            tile.ArcCover = bitmap;
        }
    }

    private static bool IsUnder(int? folderId, int ancestor, IReadOnlyDictionary<int, int?> parents)
    {
        var seen = new HashSet<int>();
        for (int? f = folderId; f is int id && seen.Add(id); f = parents.GetValueOrDefault(id))
        {
            if (id == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    private void RebuildBreadcrumb()
    {
        Breadcrumb.Clear();
        var chain = new List<ReadingListFolder>();
        var byId = _folders.ToDictionary(f => f.Id);
        var seen = new HashSet<int>();
        for (int? f = CurrentFolderId; f is int id && byId.TryGetValue(id, out var folder) && seen.Add(id); f = folder.ParentFolderId)
        {
            chain.Insert(0, folder);
        }

        Breadcrumb.Add(new ReadingBreadcrumb(null, "Reading Lists", chain.Count == 0));
        for (int i = 0; i < chain.Count; i++)
        {
            Breadcrumb.Add(new ReadingBreadcrumb(chain[i].Id, chain[i].Name, i == chain.Count - 1));
        }
    }

    // --- Navigation ---

    [RelayCommand]
    public void OpenFolder(int? folderId)
    {
        ActiveTag = null;
        CurrentFolderId = folderId;
        Refresh();
    }

    [RelayCommand]
    private void OpenCrumb(ReadingBreadcrumb? crumb)
    {
        if (crumb is not null)
        {
            OpenFolder(crumb.FolderId);
        }
    }

    [RelayCommand]
    private void OpenTile(ReadingGalleryTile? tile)
    {
        if (tile is null)
        {
            return;
        }

        if (tile.IsFolder)
        {
            OpenFolder(tile.Id);
        }
        else
        {
            _openList(tile.Id);
        }
    }

    [RelayCommand]
    private void OpenContinue(ReadingContinueCard? card)
    {
        if (card is not null)
        {
            _openList(card.ListId);
        }
    }

    [RelayCommand]
    private void SelectTag(ReadingTagChip? chip)
    {
        ActiveTag = chip?.Value;
        RebuildTagChips();
        RebuildTiles();
        OnPropertyChanged(nameof(ShowContinue));
        OnPropertyChanged(nameof(ShowContinueRow));
    }

    /// <summary>Filter by a tag from outside (a tag chip in a list's hero).</summary>
    public void FilterByTag(string tag)
    {
        ActiveTag = tag;
        Refresh();
    }

    partial void OnActiveTagChanged(string? value) => OnPropertyChanged(nameof(ShowContinueRow));

    /// <summary>The Continue row shows at the top level only, and not while a tag filter is on.</summary>
    public bool ShowContinueRow => ActiveTag is null && CurrentFolderId is null && ContinueCards.Count > 0;

    partial void OnCurrentFolderIdChanged(int? value) => OnPropertyChanged(nameof(ShowContinueRow));

    // --- Folders and lists ---

    [RelayCommand]
    private void CreateFolder(ReadingGalleryTile? parent)
    {
        int? parentId = parent is { IsFolder: true } ? parent.Id : CurrentFolderId;
        int newId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var folder = ReadingListFolders.Create(context, "New folder", parentId);
            context.SaveChanges();
            newId = folder.Id;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (parentId != CurrentFolderId)
            {
                OpenFolder(parentId);
            }
            else
            {
                Refresh();
            }

            if (Tiles.FirstOrDefault(t => t.IsFolder && t.Id == newId) is { } tile)
            {
                BeginRename(tile);
            }
        });
    }

    /// <summary>A blank list created in <paramref name="folder"/> (or the current folder), then opened.</summary>
    [RelayCommand]
    private void CreateListInFolder(ReadingGalleryTile? folder)
    {
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var now = DateTime.UtcNow;
            var list = new ReadingList { Name = "New Reading List", Type = ReadingListType.User, CreatedAt = now, UpdatedAt = now };
            context.ReadingLists.Add(list);
            ReadingListFolders.PlaceNewList(context, list, folder is { IsFolder: true } ? folder.Id : CurrentFolderId);
            context.SaveChanges();
            listId = list.Id;
        }

        _openList(listId);
    }

    [RelayCommand]
    private void SortFolder(ReadingGalleryTile? folder)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            ReadingListFolders.SortAlphabetically(context, folder is { IsFolder: true } ? folder.Id : CurrentFolderId);
            context.SaveChanges();
        }

        Dispatcher.UIThread.Post(Refresh);
    }

    public void MoveListToFolder(int listId, int? folderId)
    {
        using (var context = PaperbunkrDb.CreateContext())
        {
            ReadingListFolders.MoveList(context, listId, folderId, int.MaxValue);
            context.SaveChanges();
        }

        Dispatcher.UIThread.Post(Refresh);
    }

    [RelayCommand]
    private void MoveListTo(ReadingListMoveRequest? request)
    {
        if (request is not null)
        {
            MoveListToFolder(request.ListId, request.FolderId);
        }
    }

    [RelayCommand]
    private Task ExportListAs(ReadingListExportRequest? request) => request is null ? Task.CompletedTask : _exportList(request);

    /// <summary>Every folder as (id, indented label) for "Move to folder ▸", tree order.</summary>
    public IReadOnlyList<(int? Id, string Label)> MoveTargets()
    {
        var byId = _folders.ToDictionary(f => f.Id);
        var targets = new List<(int?, string)> { (null, "Top level") };
        var infos = _folders.Select(f => new SidebarFolderInfo(f.Id, f.ParentFolderId, f.SortOrder, false)).ToList();
        foreach (var slot in ReadingSidebarTree.Flatten(infos, Array.Empty<SidebarListInfo>()))
        {
            targets.Add((slot.Id, new string(' ', slot.Depth * 3) + byId[slot.Id].Name));
        }

        return targets;
    }

    /// <summary>Delete from a menu: confirmed with a dialog. A folder's contents move up a level; a list's comics stay in the library.</summary>
    [RelayCommand]
    private async Task DeleteTile(ReadingGalleryTile? tile)
    {
        if (tile is null)
        {
            return;
        }

        string message = tile.IsFolder
            ? $"Delete the folder \"{tile.Name}\"? Its lists and folders move up a level; nothing is deleted with it."
            : $"Delete the reading list \"{tile.Name}\"? The comics themselves stay in your library.";
        if (_dialogs is not null && !await _dialogs.ConfirmAsync(message, title: tile.IsFolder ? "Delete folder" : "Delete reading list", confirmLabel: "Delete", isDestructive: true))
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            if (tile.IsFolder)
            {
                ReadingListFolders.Delete(context, tile.Id);
            }
            else if (context.ReadingLists.Find(tile.Id) is { } list)
            {
                context.ReadingLists.Remove(list);
            }

            context.SaveChanges();
        }

        Dispatcher.UIThread.Post(Refresh);
    }

    // --- Inline rename ---

    [RelayCommand]
    private void BeginRename(ReadingGalleryTile? tile)
    {
        if (tile is null)
        {
            return;
        }

        foreach (var other in Tiles.Where(t => t.IsRenaming))
        {
            other.IsRenaming = false;
        }

        tile.RenameText = tile.Name;
        tile.IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename(ReadingGalleryTile? tile)
    {
        if (tile is not { IsRenaming: true })
        {
            return;
        }

        tile.IsRenaming = false;
        string name = tile.RenameText.Trim();
        if (name.Length == 0 || name == tile.Name)
        {
            return;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            if (tile.IsFolder)
            {
                ReadingListFolders.Rename(context, tile.Id, name);
            }
            else if (context.ReadingLists.Find(tile.Id) is { } list)
            {
                list.Name = name;
                list.UpdatedAt = DateTime.UtcNow;
            }

            context.SaveChanges();
        }

        Dispatcher.UIThread.Post(Refresh);
    }

    [RelayCommand]
    private void CancelRename(ReadingGalleryTile? tile)
    {
        if (tile is not null)
        {
            tile.IsRenaming = false;
        }
    }

    // --- Drag and drop (Q17) ---

    private IReadOnlyList<SidebarSlot> LevelSlots() =>
        Tiles.Select(t => new SidebarSlot(t.IsFolder, t.Id, 0, t.ParentFolderId)).ToList();

    private static SidebarSlot SlotOf(ReadingGalleryTile tile) => new(tile.IsFolder, tile.Id, 0, tile.ParentFolderId);

    public bool CanDrop(ReadingGalleryTile source, ReadingGalleryTile target, SidebarDropZone zone) =>
        ActiveTag is null && ReadingSidebarTree.ResolveDrop(SlotOf(source), SlotOf(target), zone, LevelSlots()) is not null;

    /// <summary>Applies a tile drop: before/after a tile of the same kind reorders, into a folder moves inside.</summary>
    public bool ApplyDrop(ReadingGalleryTile source, ReadingGalleryTile target, SidebarDropZone zone)
    {
        if (ActiveTag is not null || ReadingSidebarTree.ResolveDrop(SlotOf(source), SlotOf(target), zone, LevelSlots()) is not { } move)
        {
            return false;
        }

        using (var context = PaperbunkrDb.CreateContext())
        {
            bool moved = move.IsFolder
                ? ReadingListFolders.Move(context, move.Id, move.TargetFolderId, move.Index)
                : ReadingListFolders.MoveList(context, move.Id, move.TargetFolderId, move.Index);
            if (!moved)
            {
                return false;
            }

            context.SaveChanges();
        }

        Dispatcher.UIThread.Post(Refresh);
        return true;
    }

    // --- Folder import / export ---

    [RelayCommand]
    private async Task ImportFolder()
    {
        string? root = await _filePicker.PickFolderAsync("Import a folder of reading lists");
        if (root is not null)
        {
            await ImportFolderTreeAsync(root);
        }
    }

    public async Task ImportFolderTreeAsync(string directory)
    {
        using var job = _activity.StartJob(ActivityJobKind.Import, $"Importing reading lists from {Path.GetFileName(directory)}");
        int? parent = CurrentFolderId;
        try
        {
            var result = await Task.Run(() =>
            {
                using var context = PaperbunkrDb.CreateContext();
                return ReadingListFolderIO.Import(context, directory, parent);
            });
            string summary = result.Failures.Count == 0
                ? $"Imported {result.ListsImported} list{(result.ListsImported == 1 ? "" : "s")} in {result.FoldersCreated} folder{(result.FoldersCreated == 1 ? "" : "s")}."
                : $"Imported {result.ListsImported} list(s); {result.Failures.Count} file(s) failed: {string.Join("; ", result.Failures.Take(3))}";
            job.Succeed(summary, itemsProcessed: result.ListsImported, itemsFailed: result.Failures.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            job.Fail($"Import failed: {ex.Message}", ex: ex);
        }

        Refresh();
    }

    [RelayCommand]
    private async Task ExportFolder(ReadingGalleryTile? folder)
    {
        int? folderId = folder is { IsFolder: true } ? folder.Id : CurrentFolderId;
        if (folderId is not int id)
        {
            return;
        }

        string? root = await _filePicker.PickFolderAsync("Export folder as .CBL files");
        if (root is null)
        {
            return;
        }

        using var job = _activity.StartJob(ActivityJobKind.Other, "Exporting folder as .CBL files");
        try
        {
            using var context = PaperbunkrDb.CreateContext();
            string name = context.ReadingListFolders.Find(id)?.Name ?? "Reading lists";
            int written = ReadingListFolderIO.Export(context, id, Path.Combine(root, ReadingListFolderIO.SafeName(name)));
            job.Succeed($"Exported {written} list{(written == 1 ? "" : "s")} to {root}.", itemsProcessed: written);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            job.Fail($"Export failed: {ex.Message}", ex: ex);
        }
    }

    /// <summary>The folder a list sits in (for the list page's caption link and rail).</summary>
    public int? FolderOf(int listId) => _lists.FirstOrDefault(l => l.Id == listId)?.FolderId;
}
