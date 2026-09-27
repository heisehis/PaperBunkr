using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;

namespace Paperbunkr.App.Services;

/// <summary>Copies text or a bitmap to the system clipboard through the main window, for view models that have no view of their own to ask.</summary>
public static class ClipboardHelper
{
    /// <summary>The last bitmap handed to the clipboard, kept alive (the platform may render it lazily on paste) until the next copy replaces it.</summary>
    private static Bitmap? _lastCopiedBitmap;

    public static async Task CopyTextAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.Clipboard: { } clipboard })
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <summary>
    /// Puts <paramref name="bitmap"/> on the clipboard (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md section 2). Takes ownership: the previous copied bitmap is disposed
    /// when the next one arrives. Returns false when there is no clipboard or the platform refused.
    /// </summary>
    public static async Task<bool> TryCopyBitmapAsync(Bitmap bitmap)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow.Clipboard: { } clipboard })
        {
            return false;
        }

        try
        {
            await clipboard.SetBitmapAsync(bitmap);
        }
        catch (Exception)
        {
            return false;
        }

        var previous = _lastCopiedBitmap;
        _lastCopiedBitmap = bitmap;
        previous?.Dispose();
        return true;
    }
}
