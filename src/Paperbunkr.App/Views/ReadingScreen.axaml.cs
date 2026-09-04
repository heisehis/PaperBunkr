using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Paperbunkr.App.ViewModels;

namespace Paperbunkr.App.Views;

public partial class ReadingScreen : UserControl
{
    public ReadingScreen()
    {
        InitializeComponent();
    }

    // --- Drag-and-drop import (docs/superpowers/specs/2026-08-31-drag-and-drop-import-design.md) ---
    // Thin handlers: DragOver gates on the File format, Drop resolves local paths and delegates to
    // the ViewModel, which owns the service call / member attach / reload / toast.

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool enabled = (DataContext as ReadingScreenViewModel)?.DragDropImportEnabled == true;
        e.DragEffects = enabled && e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ReadingScreenViewModel vm || !vm.DragDropImportEnabled)
        {
            return;
        }

        var paths = DragDropPaths.Extract(e);
        if (paths.Count > 0)
        {
            await vm.ImportDroppedPathsAsync(paths);
        }
    }
}
