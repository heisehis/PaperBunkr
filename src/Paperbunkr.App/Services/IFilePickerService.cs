using System.Threading.Tasks;

namespace Paperbunkr.App.Services;

/// <summary>
/// Minimal file-open/save abstraction (docs/superpowers/specs/2026-08-06-reading-lists-design.md
/// §7) — first screen in the app needing one. Injected into ViewModels via constructor, the same
/// way existing screens inject navigation callbacks, so no ViewModel takes a View/Window
/// dependency.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Returns the picked file's local path, or null if the user cancelled.</summary>
    Task<string?> PickOpenFileAsync(string title, string extension, string extensionLabel);

    /// <summary>Returns the chosen save path, or null if the user cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string extension, string extensionLabel);

    Task SetClipboardTextAsync(string text);
}
