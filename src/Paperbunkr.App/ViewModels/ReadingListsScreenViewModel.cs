using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Paperbunkr.App.ContextMenus;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.ReadingLists;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// The Reading Lists section (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md, approach 2 - rebuilt beside the previous
/// screen and swapped in, as the Continuity redesign was): a gallery home (<see cref="Gallery"/>) and an open list (<see cref="List"/>). This
/// is the one object <see cref="MainViewModel"/> holds, and it keeps the previous screen's outside contract (<see cref="EnsureListLoaded"/>,
/// <see cref="RefreshSidebar"/>, <see cref="LoadReadingList"/>, <see cref="PlaceCreatedList"/>, <see cref="OpenContinuity"/>,
/// <see cref="InvalidateForecast"/>, <see cref="ArcSourceOptions"/>).
/// </summary>
public partial class ReadingListsScreenViewModel : ViewModelBase, IContextMenuProvider
{
    public ReadingListsScreenViewModel(
        IFilePickerService filePicker,
        Action<int, int> goReaderForIssueInReadingList,
        Action<int>? openProperties = null,
        IActivityService? activity = null,
        bool loadOnConstruction = true,
        ITrackerAutoSyncService? trackerAutoSync = null,
        IDialogService? dialogs = null)
    {
        var activityService = activity ?? new ActivityService();
        Gallery = new ReadingGalleryViewModel(filePicker, activityService, dialogs, id => LoadReadingList(id, triggerEntrance: true), ExportListAsync);
        List = new ReadingListPageViewModel(
            filePicker,
            goReaderForIssueInReadingList,
            openProperties,
            activityService,
            dialogs,
            trackerAutoSync ?? NoOpTrackerAutoSyncService.Instance,
            ShowGallery,
            id => LoadReadingList(id, triggerEntrance: true),
            FilterByTag);

        if (loadOnConstruction)
        {
            Gallery.Refresh();
        }
    }

    public ReadingGalleryViewModel Gallery { get; }

    public ReadingListPageViewModel List { get; }

    /// <summary>Gallery or an open list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGalleryMode))]
    private bool _isListMode;

    public bool IsGalleryMode => !IsListMode;

    public static ArcSourceOption[] ArcSourceOptions => ReadingListPageViewModel.ArcSourceOptions;

    /// <summary>Delegates to the list page (its Continuity link opens the continuity).</summary>
    public Action<int>? OpenContinuity
    {
        get => List.OpenContinuity;
        set => List.OpenContinuity = value;
    }

    /// <summary>Called on every visit: reload whichever view is showing. The first visit lands on the gallery.</summary>
    public void EnsureListLoaded()
    {
        if (IsListMode && List.ActiveListId is int id)
        {
            List.LoadReadingList(id, triggerEntrance: true);
        }
        else
        {
            Gallery.Refresh();
        }
    }

    /// <summary>Refreshes what shows lists - the gallery, and the open list's rail.</summary>
    public void RefreshSidebar()
    {
        Gallery.Refresh();
        if (IsListMode && List.ActiveListId is not null)
        {
            using var context = PaperbunkrDb.CreateContext();
            List.RefreshRail(context);
        }
    }

    public void LoadReadingList(int readingListId, bool triggerEntrance = false)
    {
        IsListMode = true;
        List.LoadReadingList(readingListId, triggerEntrance);
        if (List.ActiveListId is null)
        {
            ShowGallery(null);
        }
    }

    /// <summary>Back to the gallery, optionally inside a folder (the hero's folder link, ⌂ on the rail).</summary>
    public void ShowGallery(int? folderId)
    {
        List.EndEditCommand.Execute(null);
        IsListMode = false;
        Gallery.OpenFolder(folderId);
    }

    [RelayCommand]
    private void GoGalleryHome() => ShowGallery(null);

    private void FilterByTag(string tag)
    {
        List.EndEditCommand.Execute(null);
        IsListMode = false;
        Gallery.FilterByTag(tag);
    }

    /// <summary>
    /// A list just created by the New Reading List dialog goes into the folder the user is looking at (decision Q8): the gallery's folder,
    /// or the open list's folder.
    /// </summary>
    public void PlaceCreatedList(int listId)
    {
        int? folder = IsListMode ? List.FolderId : Gallery.CurrentFolderId;
        using var context = PaperbunkrDb.CreateContext();
        if (context.ReadingLists.Find(listId) is { } list)
        {
            ReadingListFolders.PlaceNewList(context, list, folder);
            context.SaveChanges();
        }
    }

    public void InvalidateForecast() => List.InvalidateForecast();

    /// <summary>Quick-create a blank list in the folder being looked at and open it (the empty gallery's "New list" button).</summary>
    [RelayCommand]
    private void CreateNew(string? name = null)
    {
        int listId;
        using (var context = PaperbunkrDb.CreateContext())
        {
            var now = DateTime.UtcNow;
            var list = new ReadingList
            {
                Name = string.IsNullOrWhiteSpace(name) ? "New Reading List" : name.Trim(),
                Type = ReadingListType.User,
                CreatedAt = now,
                UpdatedAt = now,
            };
            context.ReadingLists.Add(list);
            ReadingListFolders.PlaceNewList(context, list, IsListMode ? List.FolderId : Gallery.CurrentFolderId);
            context.SaveChanges();
            listId = list.Id;
        }

        LoadReadingList(listId, triggerEntrance: true);
    }

    /// <summary>Export ▸ from a gallery tile: opens that list (exports work on the open list), then runs the export.</summary>
    private async Task ExportListAsync(ReadingListExportRequest request)
    {
        LoadReadingList(request.ListId, triggerEntrance: true);
        await List.ExportAs(request.Format);
    }

    // --- Right-click menus: gallery tiles, rail covers, path rows, tag pills ---

    IReadOnlyList<ContextMenuEntry>? IContextMenuProvider.BuildContextMenu(object? target) => target switch
    {
        ReadingGalleryTile { IsFolder: true } folder => FolderMenu(folder),
        ReadingGalleryTile list => ListMenu(list.Id, list.ParentFolderId, Gallery.BeginRenameCommand, list),
        ReadingRailItem rail => new[]
        {
            ContextMenuEntry.Item("Open", List.OpenRailListCommand, rail),
            ContextMenuEntry.Item("Show in gallery", new RelayCommand(() => ShowGallery(List.FolderId))),
        },
        PathIssueItem path => new ReadingListMemberContextMenuBuilder().Build(path.Row),
        UpNextItem next => new ReadingListMemberContextMenuBuilder().Build(next.Row),
        CoverTileItem tile => new ReadingListMemberContextMenuBuilder().Build(tile.Row),
        TagPillViewModel pill when List.Tags.Contains(pill) =>
            [.. new TagPillContextMenuBuilder().Build(pill) ?? [], ContextMenuEntry.Separator, ContextMenuEntry.Item("Remove tag", List.RemoveTagCommand, pill, Symbol.Dismiss, isDanger: true)],
        _ => new ReadingListMemberContextMenuBuilder().Build(target) ?? new TagPillContextMenuBuilder().Build(target),
    };

    private IReadOnlyList<ContextMenuEntry> FolderMenu(ReadingGalleryTile folder) => ContextMenuEntry.Compact(new[]
    {
        ContextMenuEntry.Item("Open", Gallery.OpenTileCommand, folder, Symbol.FolderOpen),
        ContextMenuEntry.Item("New list here", Gallery.CreateListInFolderCommand, folder, Symbol.Add),
        ContextMenuEntry.Item("New folder", Gallery.CreateFolderCommand, folder, Symbol.FolderAdd),
        ContextMenuEntry.Separator,
        ContextMenuEntry.Item("Rename", Gallery.BeginRenameCommand, folder, Symbol.Rename),
        ContextMenuEntry.Item("Sort A–Z", Gallery.SortFolderCommand, folder, Symbol.TextSortAscending),
        ContextMenuEntry.Item("Export as folder of .cbl…", Gallery.ExportFolderCommand, folder, Symbol.ArrowExportUp),
        ContextMenuEntry.Separator,
        ContextMenuEntry.Item("Delete folder (lists are kept)", Gallery.DeleteTileCommand, folder, Symbol.Delete, isDanger: true),
    });

    private IReadOnlyList<ContextMenuEntry> ListMenu(int listId, int? folderId, IRelayCommand renameCommand, ReadingGalleryTile tile) => ContextMenuEntry.Compact(new[]
    {
        ContextMenuEntry.Item("Open", Gallery.OpenTileCommand, tile, Symbol.Open),
        ContextMenuEntry.Item("Rename", renameCommand, tile, Symbol.Rename),
        ContextMenuEntry.SubMenu("Move to folder", Gallery.MoveTargets().Select(t =>
            ContextMenuEntry.Item(t.Label, Gallery.MoveListToCommand, new ReadingListMoveRequest(listId, t.Id), isChecked: t.Id == folderId)),
            Symbol.FolderArrowRight),
        ContextMenuEntry.SubMenu("Export", new[]
        {
            ContextMenuEntry.Item("As .CBL…", Gallery.ExportListAsCommand, new ReadingListExportRequest(listId, "cbl")),
            ContextMenuEntry.Item("As .CSV…", Gallery.ExportListAsCommand, new ReadingListExportRequest(listId, "csv")),
            ContextMenuEntry.Item("As plain text…", Gallery.ExportListAsCommand, new ReadingListExportRequest(listId, "text")),
            ContextMenuEntry.Item("Printable checklist (PDF)…", Gallery.ExportListAsCommand, new ReadingListExportRequest(listId, "pdf")),
        }, Symbol.ArrowExportUp),
        ContextMenuEntry.Separator,
        ContextMenuEntry.Item("Delete", Gallery.DeleteTileCommand, tile, Symbol.Delete, isDanger: true),
    });
}
