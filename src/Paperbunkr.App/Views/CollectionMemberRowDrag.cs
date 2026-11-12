using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

/// <summary>
/// Drag-to-reorder for the member rows of the Edit Collection dialog (docs/superpowers/specs/2026-11-06-collection-dialog-polish-design.md),
/// modelled on <see cref="Home.HomeSectionRowDrag"/>: put <c>Handle</c> on the grip and <c>Owner</c> on the row; the row's DataContext is a
/// <see cref="CollectionMemberRowViewModel"/> and <c>Owner</c> is the <see cref="CollectionPropertiesScreenViewModel"/>. Rule-matched rows
/// never start a drag and are never a drop target (they have no place in the manual order); the chevrons stay as the keyboard path.
/// </summary>
public static class CollectionMemberRowDrag
{
    private static readonly DataFormat<CollectionMemberRowViewModel> RowFormat =
        DataFormat.CreateInProcessFormat<CollectionMemberRowViewModel>("paperbunkr-collection-member-row");

    public static readonly AttachedProperty<bool> HandleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Handle", typeof(CollectionMemberRowDrag));

    public static readonly AttachedProperty<CollectionPropertiesScreenViewModel?> OwnerProperty =
        AvaloniaProperty.RegisterAttached<Control, CollectionPropertiesScreenViewModel?>("Owner", typeof(CollectionMemberRowDrag));

    public static bool GetHandle(Control control) => control.GetValue(HandleProperty);

    public static void SetHandle(Control control, bool value) => control.SetValue(HandleProperty, value);

    public static CollectionPropertiesScreenViewModel? GetOwner(Control control) => control.GetValue(OwnerProperty);

    public static void SetOwner(Control control, CollectionPropertiesScreenViewModel? value) => control.SetValue(OwnerProperty, value);

    static CollectionMemberRowDrag()
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
            if (e.GetNewValue<CollectionPropertiesScreenViewModel?>() is not null)
            {
                DragDrop.SetAllowDrop(control, true);
                control.AddHandler(DragDrop.DragOverEvent, OnDragOver);
                control.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
                control.AddHandler(DragDrop.DropEvent, OnDrop);
            }
        });
    }

    private static async void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle || handle.DataContext is not CollectionMemberRowViewModel row || row.IsRuleMatched
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
            Services.DiagnosticsService.LogMilestone($"Collection member drag failed ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        bool accepts = sender is Control { DataContext: CollectionMemberRowViewModel { IsRuleMatched: false } }
            && e.DataTransfer.Formats.Contains(RowFormat);
        e.DragEffects = accepts ? DragDropEffects.Move : DragDropEffects.None;

        if (sender is Control { DataContext: CollectionMemberRowViewModel target })
        {
            target.IsDropTarget = accepts;
        }
    }

    private static void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control { DataContext: CollectionMemberRowViewModel target })
        {
            target.IsDropTarget = false;
        }
    }

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: CollectionMemberRowViewModel target } control)
        {
            return;
        }

        target.IsDropTarget = false;
        if (GetOwner(control) is { } owner && e.DataTransfer.TryGetValue(RowFormat) is { } dragged)
        {
            // The drop is still routing through the row it will move: reorder after it has finished (CLAUDE.md routed-event rule).
            Dispatcher.UIThread.Post(() => owner.MoveMemberTo(dragged, target));
            e.Handled = true;
        }
    }
}
