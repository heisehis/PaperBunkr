using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Paperbunkr.App.Models;
using Paperbunkr.App.Services;
using Paperbunkr.Data.Entities;

namespace Paperbunkr.App.ViewModels;

/// <summary>
/// Copy and stitched-spread export (docs/superpowers/specs/2026-09-25-comic-reader-comfort-design.md section 2). Like Save Page As, this works on the real decoded page content, not on
/// a screenshot of the zoom, pan, rotation or colour adjustments. Kept in its own file so the already-large reader view model does not grow further.
/// </summary>
public partial class ReaderScreenViewModel
{
    /// <summary>Where a copied bitmap goes; a test seam (default: the main window's clipboard).</summary>
    internal Func<Bitmap, Task<bool>> BitmapClipboardWriter { get; set; } = ClipboardHelper.TryCopyBitmapAsync;

    /// <summary>True while paged double-page mode shows a pair, i.e. there is a spread to copy or save.</summary>
    public bool IsSpreadShowing => !IsContinuousMode && CurrentPageSecondary is not null;

    /// <summary>
    /// Decodes the current page, or the stitched spread when <paramref name="spread"/> is set and a pair is showing. The caller owns (and disposes) the result.
    /// <paramref name="label"/> is "Page 12" or "Pages 12-13", for file names and toasts.
    /// </summary>
    internal Bitmap? BuildExportBitmap(bool spread, out string label)
    {
        label = string.Empty;
        if (LoadedIssue is not { FilePath: { Length: > 0 } filePath })
        {
            return null;
        }

        int index = _currentPageIndex;
        bool wantSpread = spread && IsSpreadShowing;
        var first = PageDecodeCore.DecodeSinglePage(filePath, index);
        if (first is null)
        {
            return null;
        }

        if (wantSpread)
        {
            var second = PageDecodeCore.DecodeSinglePage(filePath, index + 1);
            if (second is not null)
            {
                try
                {
                    label = $"Pages {index + 1}-{index + 2}";
                    return SpreadComposer.Compose(first, second, EffectiveReadingMode == ReadingMode.RightToLeft);
                }
                finally
                {
                    first.Dispose();
                    second.Dispose();
                }
            }
        }

        label = $"Page {index + 1}";
        return first;
    }

    private string ExportBaseName(string label) => $"{LoadedIssue?.Series?.Name ?? LoadedIssue?.Title ?? "Comic"} - {label}";

    [RelayCommand]
    private Task CopyPageAsync() => CopyAsync(spread: false);

    [RelayCommand]
    private Task CopySpreadAsync() => CopyAsync(spread: true);

    /// <summary>Ctrl+C: copies what you see, the stitched pair in spread mode and the page otherwise.</summary>
    [RelayCommand]
    private Task CopyWhatYouSeeAsync() => CopyAsync(spread: IsSpreadShowing);

    private async Task CopyAsync(bool spread)
    {
        (Bitmap? bitmap, string label) = await Task.Run(() =>
        {
            var built = BuildExportBitmap(spread, out string builtLabel);
            return (built, builtLabel);
        });

        if (bitmap is null)
        {
            ToastRequested?.Invoke(new ToastRequest("Couldn't copy", "The page could not be read.", ToastSeverity.Error));
            return;
        }

        bool copied = await BitmapClipboardWriter(bitmap);
        if (!copied)
        {
            bitmap.Dispose();
        }

        ToastRequested?.Invoke(copied
            ? new ToastRequest(spread && label.StartsWith("Pages") ? "Spread copied" : "Page copied", label, ToastSeverity.Success)
            : new ToastRequest("Couldn't copy", "The clipboard is not available.", ToastSeverity.Error));
    }

    [RelayCommand]
    private Task SaveSpreadAsPngAsync() => SaveExportAsync(spread: true, PageExportFormat.Png, "png", "PNG Image");

    [RelayCommand]
    private Task SaveSpreadAsJpegAsync() => SaveExportAsync(spread: true, PageExportFormat.Jpeg, "jpg", "JPEG Image");

    /// <summary>Decodes (off the UI thread), asks where to save, and writes the page or the stitched spread. Shared by Save Page As and Save Spread As.</summary>
    private async Task SaveExportAsync(bool spread, PageExportFormat format, string extension, string extensionLabel)
    {
        (Bitmap? bitmap, string label) = await Task.Run(() =>
        {
            var built = BuildExportBitmap(spread, out string builtLabel);
            return (built, builtLabel);
        });

        if (bitmap is null)
        {
            return;
        }

        using (bitmap)
        {
            string? path = await new FilePickerService().PickSaveFileAsync(spread ? "Save Spread As" : "Save Page As", ExportBaseName(label), extension, extensionLabel);
            if (path is null)
            {
                return;
            }

            PageExportService.TryExport(bitmap, path, format);
        }
    }
}
