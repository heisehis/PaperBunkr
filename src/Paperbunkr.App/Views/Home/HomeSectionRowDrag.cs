using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.ViewModels.Home;

namespace Paperbunkr.App.Views.Home;

/// <summary>
/// Drag-to-reorder for the Home section rows in Preferences › Appearance › Home (docs/superpowers/specs/2026-09-28-home-improvements-
/// design.md I1). Put <c>Handle</c> on the grip and <c>Target</c> on the row; the row's DataContext is a <see cref="HomeSectionRow"/>
/// and <c>Owner</c> is the <see cref="HomePreferencesViewModel"/>. Same in-process format approach as the reading-list page's row drag;
/// the ↑/↓ buttons stay as the keyboard path.
/// </summary>
public static class HomeSectionRowDrag
{
    private static readonly DataFormat<HomeSectionRow> RowFormat = DataFormat.CreateInProcessFormat<HomeSectionRow>("paperbunkr-home-section-row");

    public static readonly AttachedProperty<bool> HandleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Handle", typeof(HomeSectionRowDrag));

    public static readonly AttachedProperty<HomePreferencesViewModel?> OwnerProperty =
        AvaloniaProperty.RegisterAttached<Control, HomePreferencesViewModel?>("Owner", typeof(HomeSectionRowDrag));

    public static bool GetHandle(Control control) => control.GetValue(HandleProperty);

    public static void SetHandle(Control control, bool value) => control.SetValue(HandleProperty, value);

    public static HomePreferencesViewModel? GetOwner(Control control) => control.GetValue(OwnerProperty);

    public static void SetOwner(Control control, HomePreferencesViewModel? value) => control.SetValue(OwnerProperty, value);

    static HomeSectionRowDrag()
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
            control.RemoveHandler(DragDrop.DropEvent, OnDrop);
            if (e.GetNewValue<HomePreferencesViewModel?>() is not null)
            {
                DragDrop.SetAllowDrop(control, true);
                control.AddHandler(DragDrop.DragOverEvent, OnDragOver);
                control.AddHandler(DragDrop.DropEvent, OnDrop);
            }
        });
    }

    private static async void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control handle || handle.DataContext is not HomeSectionRow row
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
            Services.DiagnosticsService.LogMilestone($"Home section drag failed ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
        => e.DragEffects = e.DataTransfer.Formats.Contains(RowFormat) ? DragDropEffects.Move : DragDropEffects.None;

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is Control target && target.DataContext is HomeSectionRow targetRow && GetOwner(target) is { } owner
            && e.DataTransfer.TryGetValue(RowFormat) is { } dragged)
        {
            owner.MoveTo(dragged, targetRow);
            e.Handled = true;
        }
    }
}
