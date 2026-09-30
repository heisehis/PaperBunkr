using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// The list page's code-behind (docs/superpowers/specs/2026-09-28-reading-lists-redesign-design.md §4, §8): bringing "Up next" into view
/// after a list opens, Edit mode's row drag (from the ⠿ handle; the drop's position above or below a row shows a 2 px line, and
/// <see cref="ReadingListPageViewModel.MoveRows"/> does the move), the keys (Esc, Ctrl+↑/↓, Delete), chapter-rename keys, and dropped comic
/// files importing into the open list. Everything else is bindings.
/// </summary>
public partial class ReadingListPageView : UserControl
{
    private static readonly DataFormat<ReadingListItemRowViewModel> RowFormat = DataFormat.CreateInProcessFormat<ReadingListItemRowViewModel>("paperbunkr-reading-list-row");
    private const double DragThreshold = 6;

    private ReadingListPageViewModel? _subscribed;
    private Point? _pressPoint;
    private PointerPressedEventArgs? _pressArgs;
    private ReadingListItemRowViewModel? _pressRow;
    private bool _dragging;
    private Border? _lineShown;

    static ReadingListPageView()
    {
        IsVisibleProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            if (box.IsVisible && (box.Classes.Contains("rlChapterRename") || box.Classes.Contains("rlTagEntry")))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    box.Focus();
                    box.SelectAll();
                });
            }
        });
    }

    public ReadingListPageView()
    {
        InitializeComponent();
        _focus = new FocusReclaimer(this, () => Vm is { IsListOpen: true }, () => FocusBody(Vm!));
        PathList.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PathList.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        PathList.AddHandler(PointerReleasedEvent, (_, _) => ResetPress(), RoutingStrategies.Tunnel, handledEventsToo: true);
        MainArea.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        MainArea.AddHandler(DragDrop.DragLeaveEvent, (_, _) => HideLine());
        MainArea.AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        PathList.AddHandler(LostFocusEvent, OnChapterRenameLostFocus);
        PathList.AddHandler(Button.ClickEvent, OnMoreClick);
        MainArea.AddHandler(DoubleTappedEvent, OnCardDoubleTapped);
        MainArea.AddHandler(LostFocusEvent, OnTagEntryLostFocus);
        PathList.AddHandler(GotFocusEvent, (_, _) => RememberBodyIndex(PathList));
        CoverWall.AddHandler(GotFocusEvent, (_, _) => RememberBodyIndex(CoverWall));
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

    /// <summary>Puts focus back on the last row (or Up next) whenever the page shows and nothing inside it holds focus - see
    /// <see cref="FocusReclaimer"/> for why that happens (a list opened from the gallery, or back from the reader).</summary>
    private readonly FocusReclaimer _focus;

    private ReadingListPageViewModel? Vm => DataContext as ReadingListPageViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.ScrollToUpNextRequested -= OnScrollToUpNext;
        }

        _subscribed = Vm;
        if (_subscribed is not null)
        {
            _subscribed.ScrollToUpNextRequested += OnScrollToUpNext;
        }
    }

    /// <summary>After layout, bring Up next (or the next-up cover) into view - a 300-issue list opens where you are, not at #1.</summary>
    private void OnScrollToUpNext(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (Vm is not { } vm)
            {
                return;
            }

            _lastBodyIndex = null;
            if (vm.IsPathMode && vm.ScrollTarget is { } target && vm.PathItems.IndexOf(target) is var index and >= 0)
            {
                ReadingListKeyboard.FocusIndex(PathList, index, 1);
            }
            else if (vm.IsCoversMode && vm.CoverTiles.Count > 0)
            {
                ReadingListKeyboard.FocusIndex(CoverWall, vm.CoverScrollIndex, 1);
            }
        }, DispatcherPriority.Loaded);

    // --- Edit mode drag (from the ⠿ handle) ---

    private static Border? RowBorderAt(object? source) =>
        source is Visual visual ? visual.GetSelfAndVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("pathRow")) : null;

    private static ReadingListItemRowViewModel? RowAt(object? source) =>
        (RowBorderAt(source)?.DataContext as PathEditRowItem)?.Row;

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _pressRow = null;
        if (Vm is not { IsEditing: true } || !e.GetCurrentPoint(PathList).Properties.IsLeftButtonPressed
            || e.Source is not TextBlock { Classes: var classes } || !classes.Contains("rlHandle") || RowAt(e.Source) is not { } row)
        {
            return;
        }

        _pressRow = row;
        _pressPoint = e.GetPosition(PathList);
        _pressArgs = e;
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging || _pressPoint is not Point start || _pressRow is not { } row || _pressArgs is not { } pressed)
        {
            return;
        }

        var delta = e.GetPosition(PathList) - start;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _dragging = true;
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(RowFormat, row));
            await DragDrop.DoDragDropAsync(pressed, data, DragDropEffects.Move);
        }
        finally
        {
            _dragging = false;
            HideLine();
            ResetPress();
        }
    }

    private void ResetPress()
    {
        if (!_dragging)
        {
            _pressPoint = null;
            _pressRow = null;
            _pressArgs = null;
        }
    }

    /// <summary>The row the dragged rows would land before: the hovered row (upper half) or the one after it (lower half); null = the end.</summary>
    private ReadingListItemRowViewModel? InsertBefore(Border rowBorder, ReadingListItemRowViewModel hovered, DragEventArgs e, out bool upper)
    {
        upper = e.GetPosition(rowBorder).Y < rowBorder.Bounds.Height / 2;
        if (upper || Vm is not { } vm)
        {
            return hovered;
        }

        int index = vm.Rows.ToList().IndexOf(hovered);
        return index >= 0 && index + 1 < vm.Rows.Count ? vm.Rows[index + 1] : null;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = Vm?.DragDropImportEnabled == true ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!e.DataTransfer.Formats.Contains(RowFormat))
        {
            return;
        }

        e.Handled = true;
        if (Vm is not { IsEditing: true } || RowBorderAt(e.Source) is not { DataContext: PathEditRowItem { Row: var hovered } } rowBorder)
        {
            e.DragEffects = DragDropEffects.None;
            HideLine();
            return;
        }

        InsertBefore(rowBorder, hovered, e, out bool upper);
        e.DragEffects = DragDropEffects.Move;
        ShowLine(rowBorder, upper);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        HideLine();
        if (Vm is not { } vm)
        {
            return;
        }

        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.Handled = true;
            var paths = DragDropPaths.Extract(e);
            if (paths.Count > 0)
            {
                await vm.ImportDroppedPathsAsync(paths);
            }

            return;
        }

        if (vm.IsEditing && e.DataTransfer.TryGetValue(RowFormat) is { } dragged
            && RowBorderAt(e.Source) is { DataContext: PathEditRowItem { Row: var hovered } } rowBorder)
        {
            e.Handled = true;
            vm.MoveRows(vm.DragSet(dragged), InsertBefore(rowBorder, hovered, e, out _));
        }
    }

    /// <summary>The insertion line sits above the hovered row, or above the next row when dropping on the lower half.</summary>
    private void ShowLine(Border rowBorder, bool upper)
    {
        HideLine();
        var line = rowBorder.GetVisualAncestors().OfType<StackPanel>().FirstOrDefault()?.Children.OfType<Border>().FirstOrDefault(b => b.Classes.Contains("rlInsertLine"));
        if (line is null)
        {
            return;
        }

        line.IsVisible = true;
        line.VerticalAlignment = upper ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Bottom;
        if (!upper)
        {
            // the line element lives above the row; for a lower-half drop draw it under the row instead
            line.IsVisible = false;
            rowBorder.BorderBrush = (Avalonia.Media.IBrush?)this.FindResource("PbAccentBrush");
            rowBorder.BorderThickness = new Thickness(0, 0, 0, 2);
        }

        _lineShown = upper ? line : rowBorder;
    }

    private void HideLine()
    {
        switch (_lineShown)
        {
            case { } border when border.Classes.Contains("rlInsertLine"):
                border.IsVisible = false;
                break;
            case { } row:
                row.ClearValue(Border.BorderBrushProperty);
                row.ClearValue(Border.BorderThicknessProperty);
                break;
        }

        _lineShown = null;
    }

    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button { Classes: var classes } button && classes.Contains("rlMore") && button.DataContext is PathIssueItem item
            && new ReadingListMemberContextMenuBuilder().Build(item.Row) is { } entries)
        {
            Controls.ContextMenuHost.ShowMenu(button, entries);
            e.Handled = true;
        }
    }

    // --- Keys ---

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm)
        {
            return;
        }

        if (e.Source is TextBox { Classes: var tagClasses } && tagClasses.Contains("rlTagEntry"))
        {
            if (e.Key == Key.Enter)
            {
                vm.CommitAddTagCommand.Execute(null);
                Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.CancelAddTagCommand.Execute(null);
                Focus();
                e.Handled = true;
            }

            return;
        }

        if (e.Source is TextBox { Classes: var classes } box && classes.Contains("rlChapterRename") && box.DataContext is ChapterHeaderItem header)
        {
            if (e.Key == Key.Enter)
            {
                vm.CommitChapterRenameCommand.Execute(header);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                vm.CancelChapterRenameCommand.Execute(header);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            if (vm.IsDrawerOpen)
            {
                vm.CloseDrawerCommand.Execute(null);
                e.Handled = true;
            }
            else if (vm.IsEditing)
            {
                vm.EndEditCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Source is TextBox || (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Controls.SuggestBox>().Any() == true || vm.IsDrawerOpen)
        {
            return;
        }

        var focusedRow = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(c => c.DataContext).OfType<PathIssueItem>().FirstOrDefault()?.Row;

        // Enter / Space on a card reads it (a click only focuses, as in Library).
        if (!vm.IsEditing && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter or Key.Space
            && FocusedElement() is Button { Classes: var cardClasses } card && cardClasses.Contains("rlCard"))
        {
            OpenCard(card.DataContext);
            e.Handled = true;
            return;
        }

        // Arrow keys never leave the page (Ctrl+arrows reorder in Edit mode, below).
        if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && HandleArrows(vm, e))
        {
            e.Handled = true;
            return;
        }

        if (vm.IsEditing && e.Key == Key.Space && focusedRow is not null && e.Source is not CheckBox)
        {
            vm.ToggleMemberSelectionCommand.Execute(focusedRow);
            e.Handled = true;
            return;
        }

        if (!vm.IsEditing)
        {
            return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.Up or Key.Down)
        {
            vm.MoveSelectionBy(focusedRow, e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && vm.AnyMembersSelected)
        {
            vm.RemoveSelectedMembersCommand.Execute(null);
            e.Handled = true;
        }
    }

    // --- Guarded arrow keys: three regions (cover rail, header, path / cover wall), movement is explicit and always handled ---

    private int? _lastBodyIndex;

    private ItemsControl Body(ReadingListPageViewModel vm) => vm.IsCoversMode ? CoverWall : PathList;

    private void RememberBodyIndex(ItemsControl list)
    {
        if (ReadingListKeyboard.FocusedIndex(list) is var index and >= 0)
        {
            _lastBodyIndex = index;
        }
    }

    private void FocusBody(ReadingListPageViewModel vm)
    {
        var body = Body(vm);
        int start = _lastBodyIndex
                    ?? (vm.IsCoversMode ? vm.CoverScrollIndex : vm.ScrollTarget is { } t ? Math.Max(0, vm.PathItems.IndexOf(t)) : 0);
        ReadingListKeyboard.FocusIndex(body, start, 1);
    }

    private static bool IsNavigationKey(Key key) =>
        key is Key.Up or Key.Down or Key.Left or Key.Right or Key.PageUp or Key.PageDown or Key.Home or Key.End;

    private static Control[] Focusables(Visual? region) =>
        region is null || !region.IsEffectivelyVisible
            ? Array.Empty<Control>()
            : region.GetVisualDescendants().OfType<Control>()
                .Where(c => c is Button or TextBox && c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible)
                .ToArray();

    private Control[] RailStops() => Focusables(CoverRail);

    /// <summary>The hero's and the Checks strip's controls, in reading order.</summary>
    private Control[] HeaderStops() =>
        MainArea.GetVisualDescendants().OfType<Control>()
            .Where(c => c is Button or TextBox && c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible)
            .Where(c => !c.GetSelfAndVisualAncestors().Any(a => ReferenceEquals(a, PathList) || ReferenceEquals(a, CoverWall)))
            .Where(c => !c.GetSelfAndVisualAncestors().OfType<Border>().Any(b => b.Classes.Contains("rlDrawer")))
            .ToArray();

    private static bool FocusStop(Control[] stops, int index)
    {
        if (stops.Length == 0)
        {
            return false;
        }

        var target = stops[Math.Clamp(index, 0, stops.Length - 1)];
        target.Focus(NavigationMethod.Directional);
        target.BringIntoView();
        return true;
    }

    private void FocusRail()
    {
        var stops = RailStops();
        int active = Array.FindIndex(stops, s => s.DataContext is ReadingRailItem { IsActive: true });
        FocusStop(stops, active >= 0 ? active : 0);
    }

    /// <summary>Up from the top of the path lands on the hero's main action: Start reading / Continue, else the first action (Done in Edit mode).</summary>
    private void FocusHeader()
    {
        var actions = Focusables(HeroActions).Concat(Focusables(EditBar)).ToArray();
        if (!FocusStop(actions, 0))
        {
            FocusStop(HeaderStops(), 0);
        }
    }

    /// <summary>
    /// The actually-focused element, via the FocusManager directly - not <c>e.Source</c>. <c>ReadingListKeyboard.FocusedIndex</c> (proven
    /// working in round 1) has always looked focus up this way; the region checks below match it for the same reason.
    /// </summary>
    private Visual? FocusedElement() => TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;

    private bool HandleArrows(ReadingListPageViewModel vm, KeyEventArgs e)
    {
        if (!IsNavigationKey(e.Key))
        {
            return false;
        }

        var focused = FocusedElement();
        bool inRail = focused?.GetSelfAndVisualAncestors().Any(a => ReferenceEquals(a, CoverRail)) == true;
        var body = Body(vm);
        bool inBody = focused?.GetSelfAndVisualAncestors().Any(a => ReferenceEquals(a, body)) == true;

        if (inRail)
        {
            var stops = RailStops();
            int at = Array.IndexOf(stops, focused as Control);
            switch (e.Key)
            {
                case Key.Up: FocusStop(stops, at - 1); break;
                case Key.Down: FocusStop(stops, at + 1); break;
                case Key.Home or Key.PageUp: FocusStop(stops, 0); break;
                case Key.End or Key.PageDown: FocusStop(stops, stops.Length - 1); break;
                case Key.Right: FocusBody(vm); break;
            }

            return true;
        }

        if (inBody)
        {
            int columns = vm.IsCoversMode ? ReadingListKeyboard.Columns(CoverWall, 118) : 1;
            int index = ReadingListKeyboard.FocusedIndex(body);
            if (e.Key == Key.Left && (columns == 1 || index % columns == 0))
            {
                FocusRail();
                return true;
            }

            if (e.Key == Key.Up && index >= 0 && index < columns)
            {
                FocusHeader();
                return true;
            }

            ReadingListKeyboard.HandleNavigationKey(body, e, columns, _lastBodyIndex ?? 0);
            return true;
        }

        // Header (the hero, the Checks strip, or the page itself after a click on empty space).
        MoveInHeader(vm, focused as Control, e.Key);
        return true;
    }

    /// <summary>
    /// Spatial movement through the header's controls: Left/Right along the visual row (Left off its start goes to the cover rail),
    /// Up/Down to the nearest control in the row above or below (Down off the last row goes into the path; Up off the first stays put).
    /// </summary>
    private void MoveInHeader(ReadingListPageViewModel vm, Control? focused, Key key)
    {
        const double SameRow = 12;
        var stops = HeaderStops();
        Point? CentreOf(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), this);

        if (focused is null || Array.IndexOf(stops, focused) < 0 || CentreOf(focused) is not { } from)
        {
            if (key is Key.Down or Key.PageDown)
            {
                FocusBody(vm);
            }
            else
            {
                FocusHeader();
            }

            return;
        }

        var others = stops.Where(s => !ReferenceEquals(s, focused))
            .Select(s => (Stop: s, At: CentreOf(s)))
            .Where(x => x.At is not null)
            .Select(x => (x.Stop, At: x.At!.Value))
            .ToList();

        Control? target = key switch
        {
            Key.Left or Key.Right => others
                .Where(o => Math.Abs(o.At.Y - from.Y) < SameRow && (key == Key.Left ? o.At.X < from.X : o.At.X > from.X))
                .OrderBy(o => Math.Abs(o.At.X - from.X)).Select(o => o.Stop).FirstOrDefault(),
            Key.Up or Key.Down => others
                .Where(o => key == Key.Up ? o.At.Y < from.Y - SameRow : o.At.Y > from.Y + SameRow)
                .OrderBy(o => (Math.Abs(o.At.Y - from.Y) * 3) + Math.Abs(o.At.X - from.X)).Select(o => o.Stop).FirstOrDefault(),
            _ => null,
        };

        if (target is not null)
        {
            target.Focus(NavigationMethod.Directional);
            target.BringIntoView();
        }
        else if (key == Key.Left)
        {
            FocusRail();
        }
        else if (key is Key.Down or Key.PageDown)
        {
            FocusBody(vm);
        }
    }

    // --- Cards: a click focuses, double-click / Enter / Space reads ---

    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Only a double-click on the card itself - not on a button inside it (⋯, Find & link, Read).
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault() is { Classes: var classes } card && classes.Contains("rlCard"))
        {
            OpenCard(card.DataContext);
            e.Handled = true;
        }
    }

    private static void OpenCard(object? item)
    {
        var row = item switch
        {
            PathIssueItem path => path.Row,
            UpNextItem next => next.Row,
            CoverTileItem tile => tile.Row,
            _ => null,
        };
        if (row is { IsOwned: true })
        {
            row.OpenCommand.Execute(null);
        }
    }

    private void OnTagEntryLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox box && box.Classes.Contains("rlTagEntry") && Vm is { IsAddingTag: true } vm)
        {
            vm.CommitAddTagCommand.Execute(null);
        }
    }

    private void OnChapterRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TextBox box && box.Classes.Contains("rlChapterRename") && box.DataContext is ChapterHeaderItem { IsRenaming: true } header)
        {
            Vm?.CommitChapterRenameCommand.Execute(header);
        }
    }
}
