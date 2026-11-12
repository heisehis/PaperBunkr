using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Paperbunkr.App.Models;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// Drag-to-reorder for the sidebar's Collections rows (docs/superpowers/specs/2026-11-06-collection-identity-design.md), modelled on
/// <see cref="CollectionMemberRowDrag"/>: put <c>Handle</c> on the grip (a plain Border - the row's own button swallows a left press) and <c>Owner</c> on the
/// row; the row's DataContext is a <see cref="CollectionSummary"/> and <c>Owner</c> is the <see cref="LibraryScreenViewModel"/>. A drop only counts inside the
/// dragged row's own group (pinned or unpinned); Move up / Move down in the row menu stay as the keyboard path.
/// </summary>
public static class CollectionSidebarRowDrag
{
    private static readonly DataFormat<CollectionSummary> RowFormat =
        DataFormat.CreateInProcessFormat<CollectionSummary>("paperbunkr-collection-sidebar-row");

    public static readonly AttachedProperty<bool> HandleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Handle", typeof(CollectionSidebarRowDrag));

    public static readonly AttachedProperty<LibraryScreenViewModel?> OwnerProperty =
        AvaloniaProperty.RegisterAttached<Control, LibraryScreenViewModel?>("Owner", typeof(CollectionSidebarRowDrag));

    public static bool GetHandle(Control control) => control.GetValue(HandleProperty);

    public static void SetHandle(Control control, bool value) => control.SetValue(HandleProperty, value);

    public static LibraryScreenViewModel? GetOwner(Control control) => control.GetValue(OwnerProperty);

    public static void SetOwner(Control control, LibraryScreenViewModel? value) => control.SetValue(OwnerProperty, value);

    static CollectionSidebarRowDrag()
    {
        HandleProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.PointerPressed -= OnHandlePressed;
            if (e.GetNewValue<bool>())
            {
                control.PointerPressed += OnHandlePressed;
            }
        });

        OwnerProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
            control.RemoveHandler(DragDrop.DragLeaveEvent, OnDragLeave);
            control.RemoveHandler(DragDrop.DropEvent, OnDrop);
            if (e.GetNewValue<LibraryScreenViewModel?>() is not null)
            {
                DragDrop.SetAllowDrop(control, true);
                control.AddHandler(DragDrop.DragOverEvent, OnDragOver);
                control.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
                control.AddHandler(DragDrop.DropEvent, OnDrop);
            }
        });
    }

    /// <summary>True when a drag of <paramref name="dragged"/> may land on <paramref name="target"/>: another row of the same group.</summary>
    internal static bool CanDrop(CollectionSummary dragged, CollectionSummary target) =>
        dragged.Id != target.Id && dragged.IsPinned == target.IsPinned;

    private static async void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle || handle.DataContext is not CollectionSummary row
            || !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
        {
            return;
        }

        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(RowFormat, row));
            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            Services.DiagnosticsService.LogMilestone($"Collection sidebar drag failed ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        bool accepts = sender is Control { DataContext: CollectionSummary target }
            && e.DataTransfer.TryGetValue(RowFormat) is { } dragged
            && CanDrop(dragged, target);
        e.DragEffects = accepts ? DragDropEffects.Move : DragDropEffects.None;

        if (sender is Control { DataContext: CollectionSummary row })
        {
            row.IsDropTarget = accepts;
        }
    }

    private static void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control { DataContext: CollectionSummary row })
        {
            row.IsDropTarget = false;
        }
    }

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: CollectionSummary target } control)
        {
            return;
        }

        target.IsDropTarget = false;
        if (GetOwner(control) is { } owner && e.DataTransfer.TryGetValue(RowFormat) is { } dragged && CanDrop(dragged, target))
        {
            // The drop is still routing through a row the reorder will rebuild: do it after the event has finished (CLAUDE.md routed-event rule).
            Dispatcher.UIThread.Post(() => owner.MoveCollectionTo(dragged, target));
            e.Handled = true;
        }
    }
}
