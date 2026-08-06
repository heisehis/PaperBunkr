using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Paperbunkr.App.Services;

public class FilePickerService : IFilePickerService
{
    private static TopLevel? GetTopLevel() =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(extensionLabel) { Patterns = new[] { $"*.{extension}" } } },
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return null;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            FileTypeChoices = new[] { new FilePickerFileType(extensionLabel) { Patterns = new[] { $"*.{extension}" } } },
        });

        return file?.TryGetLocalPath();
    }

    public async Task SetClipboardTextAsync(string text)
    {
        var clipboard = GetTopLevel()?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
