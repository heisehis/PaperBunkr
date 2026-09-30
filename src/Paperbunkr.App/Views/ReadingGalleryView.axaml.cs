using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentIcons.Common;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// The gallery's code-behind (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §2): tile drag-and-drop - a press that
/// moves a few pixels starts an in-process drag; on a tile the left or right third means before/after, the middle of a folder means into,
/// and <see cref="ReadingGalleryViewModel.ApplyDrop"/> does the move - the inline rename's keys and focus, and a dropped directory importing
/// as a folder. Mirrors the poster drag in <see cref="ContinuityOverviewView"/>.
/// </summary>
public partial class ReadingGalleryView : UserControl
{
    /// <summary>The glyph on a tile with no cover at all.</summary>
    public static readonly IValueConverter TileGlyph = new FuncValueConverter<bool, Symbol>(isFolder => isFolder ? Symbol.Folder : Symbol.TextBulletListSquare);

    private static readonly DataFormat<ReadingGalleryTile> DragFormat = DataFormat.CreateInProcessFormat<ReadingGalleryTile>("paperbunkr-reading-gallery-tile");
    private const double DragThreshold = 6;

    private Point? _pressPoint;
    private PointerPressedEventArgs? _pressArgs;
    private ReadingGalleryTile? _pressTile;
    private bool _dragging;
    private Border? _feedbackHost;

    /// <summary>Focuses the last-used tile (or the first) whenever the gallery attaches, becomes visible, or its Tiles are reset in place
    /// by a folder navigation - see <see cref="FocusReclaimer"/> for why each of those loses focus on its own.</summary>
    private readonly FocusReclaimer _focus;

    static ReadingGalleryView()
    {
        IsVisibleProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            if (box.IsVisible && box.Classes.Contains("rlRename"))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    box.Focus();
                    box.SelectAll();
                });
            }
        });
    }

    public ReadingGalleryView()
    {
        InitializeComponent();
        _focus = new FocusReclaimer(this, () => Vm is { } vm && vm.Tiles.Count > 0,
            () => ReadingListKeyboard.FocusIndex(TileGrid, _lastTileIndex ?? 0, 1));
        TileGrid.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        TileGrid.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        TileGrid.AddHandler(PointerReleasedEvent, (_, _) => ResetPress(), RoutingStrategies.Tunnel, handledEventsToo: true);
        Scroller.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Scroller.AddHandler(DragDrop.DragLeaveEvent, (_, _) => ClearFeedback());
        Scroller.AddHandler(DragDrop.DropEvent, OnDrop);
        TileGrid.AddHandler(KeyDownEvent, OnRenameKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnNavigationKeyDown, RoutingStrategies.Tunnel);
        TileGrid.AddHandler(LostFocusEvent, OnRenameLostFocus);
        TileGrid.AddHandler(GotFocusEvent, (_, _) =>
        {
            if (ReadingListKeyboard.FocusedIndex(TileGrid) is var index and >= 0)
            {
                _lastTileIndex = index;
            }
        });
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                _focus.Reclaim();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _focus.Reclaim();
    }

    private int? _lastTileIndex;

    private ObservableCollection<ReadingGalleryTile>? _subscribedTiles;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribedTiles is not null)
        {
            _subscribedTiles.CollectionChanged -= OnTilesChanged;
        }

        _subscribedTiles = Vm?.Tiles;
        if (_subscribedTiles is not null)
        {
            _subscribedTiles.CollectionChanged += OnTilesChanged;
        }
    }

    // A folder navigation (or "up a folder"/breadcrumb click) resets Tiles in place while the gallery stays visible throughout - the
    // tile that had focus is detached along with the old content, and nothing takes its place without this.
    private void OnTilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => _focus.Reclaim();

    private ReadingGalleryViewModel? Vm => DataContext as ReadingGalleryViewModel;

    private static Border? HostAt(object? source) =>
        source is Visual visual
            ? visual.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("rlTileHost"))
            : null;

    // --- Drag source ---

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(TileGrid).Properties.IsLeftButtonPressed || e.Source is TextBox
            || HostAt(e.Source)?.DataContext is not ReadingGalleryTile { IsRenaming: false } tile)
        {
            _pressTile = null;
            return;
        }

        _pressTile = tile;
        _pressPoint = e.GetPosition(TileGrid);
        _pressArgs = e;
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging || _pressPoint is not Point start || _pressTile is not { } tile || _pressArgs is not { } pressed)
        {
            return;
        }

        var delta = e.GetPosition(TileGrid) - start;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _dragging = true;
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(DragFormat, tile));
            await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move);
        }
        finally
        {
            _dragging = false;
            ClearFeedback();
            ResetPress();
        }
    }

    private void ResetPress()
    {
        if (!_dragging)
        {
            _pressPoint = null;
            _pressTile = null;
            _pressArgs = null;
        }
    }

    // --- Drop target ---

    private static SidebarDropZone ZoneFor(Border host, ReadingGalleryTile target, DragEventArgs e)
    {
        double x = e.GetPosition(host).X;
        double w = Math.Max(1, host.Bounds.Width);
        if (!target.IsFolder)
        {
            return x < w / 2 ? SidebarDropZone.Before : SidebarDropZone.After;
        }

        return x < w / 3 ? SidebarDropZone.Before : x > w * 2 / 3 ? SidebarDropZone.After : SidebarDropZone.Into;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (!e.DataTransfer.Formats.Contains(DragFormat))
        {
            return;
        }

        e.Handled = true;
        if (Vm is not { } vm || e.DataTransfer.TryGetValue(DragFormat) is not { } source || HostAt(e.Source) is not { DataContext: ReadingGalleryTile target } host)
        {
            e.DragEffects = DragDropEffects.None;
            ClearFeedback();
            return;
        }

        var zone = ZoneFor(host, target, e);
        bool ok = vm.CanDrop(source, target, zone);
        e.DragEffects = ok ? DragDropEffects.Move : DragDropEffects.None;
        ShowFeedback(ok ? host : null, zone);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        ClearFeedback();
        if (Vm is not { } vm)
        {
            return;
        }

        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.Handled = true;
            foreach (var path in DragDropPaths.Extract(e).Where(Directory.Exists))
            {
                await vm.ImportFolderTreeAsync(path);
            }

            return;
        }

        if (e.DataTransfer.TryGetValue(DragFormat) is { } source && HostAt(e.Source) is { DataContext: ReadingGalleryTile target } host)
        {
            e.Handled = true;
            vm.ApplyDrop(source, target, ZoneFor(host, target, e));
        }
    }

    private void ShowFeedback(Border? host, SidebarDropZone zone)
    {
        if (!ReferenceEquals(host, _feedbackHost))
        {
            ClearFeedback();
        }

        _feedbackHost = host;
        if (host is null)
        {
            return;
        }

        host.Classes.Set("dropInto", zone == SidebarDropZone.Into);
        foreach (var bar in host.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("rlDropBar")))
        {
            bar.IsVisible = (zone == SidebarDropZone.Before && bar.Classes.Contains("dropBefore"))
                            || (zone == SidebarDropZone.After && bar.Classes.Contains("dropAfter"));
        }
    }

    private void ClearFeedback()
    {
        if (_feedbackHost is { } host)
        {
            host.Classes.Set("dropInto", false);
            foreach (var bar in host.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("rlDropBar")))
            {
                bar.IsVisible = false;
            }
        }

        _feedbackHost = null;
    }

    // --- Keyboard: arrows move between tiles, F2 renames, Delete deletes, Backspace / Alt+↑ goes up a folder ---

    private void OnNavigationKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || e.Handled || e.Source is TextBox)
        {
            return;
        }

        if ((e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None) || (e.Key == Key.Up && e.KeyModifiers == KeyModifiers.Alt))
        {
            if (vm.Breadcrumb.Count > 1)
            {
                vm.OpenCrumbCommand.Execute(vm.Breadcrumb[^2]);
                ReadingListKeyboard.FocusIndex(TileGrid, 0, 1);
                e.Handled = true;
            }

            return;
        }

        int index = ReadingListKeyboard.FocusedIndex(TileGrid);
        if (index >= 0 && index < vm.Tiles.Count && e.KeyModifiers == KeyModifiers.None)
        {
            if (e.Key == Key.F2)
            {
                vm.BeginRenameCommand.Execute(vm.Tiles[index]);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                vm.DeleteTileCommand.Execute(vm.Tiles[index]);
                e.Handled = true;
                return;
            }
        }

        // 164px tiles + 18px gap. Arrows are always used here, even at an edge, so focus never drifts out to the app's nav rail.
        if (ReadingListKeyboard.HandleNavigationKey(TileGrid, e, ReadingListKeyboard.Columns(TileGrid, 182), 0)
            || (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Left or Key.Right))
        {
            e.Handled = true;
        }
    }

    // --- Inline rename ---

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is not TextBox box || !box.Classes.Contains("rlRename") || box.DataContext is not ReadingGalleryTile tile || Vm is not { } vm)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            vm.CommitRenameCommand.Execute(tile);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            vm.CancelRenameCommand.Execute(tile);
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox box && box.Classes.Contains("rlRename") && box.DataContext is ReadingGalleryTile { IsRenaming: true } tile)
        {
            Vm?.CommitRenameCommand.Execute(tile);
        }
    }
}
