using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Paperbunkr.App.Views;

/// <summary>
/// Shared path extraction for the Library and Reading List screens' drag-and-drop <c>Drop</c>
/// handlers (docs/superpowers/specs/2026-08-31-drag-and-drop-import-design.md). Resolves each
/// dropped <see cref="IStorageItem"/> to a local filesystem path via <c>TryGetLocalPath()</c> - the
/// same accessor <c>FilePickerService</c> uses - and silently drops items with no local path (e.g.
/// a browser-sourced drag with no real file behind it), since those were never real files to import.
/// </summary>
internal static class DragDropPaths
{
    public static IReadOnlyList<string> Extract(DragEventArgs e)
    {
        var paths = new List<string>();

        if (e.DataTransfer.Formats.Contains(DataFormat.File) && e.DataTransfer.TryGetFiles() is { } files)
        {
            foreach (var item in files)
            {
                if (item.TryGetLocalPath() is { } path)
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }
}
