using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace Paperbunkr.App.Services;

/// <summary>Copies text to the system clipboard through the main window, for view models that have no view of their own to ask.</summary>
public static class ClipboardHelper
{
    public static async Task CopyTextAsync(string text)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow.Clipboard: { } clipboard })
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
