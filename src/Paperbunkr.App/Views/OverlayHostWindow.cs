using Avalonia.Controls;
using Avalonia.Media;

namespace Paperbunkr.App.Views;

/// <summary>
/// A borderless, transparent, owned child window used to host Book reader drawer/sheet content
/// above the NativeWebView reading pane (docs/superpowers/specs/2026-09-16-book-reader-drawer-
/// window-fix-design.md). Unlike Avalonia's Popup, a real Window is NOT subject to
/// Win32PlatformOptions.OverlayPopups (that setting only embeds Popups, per Avalonia's own docs -
/// "Embeds popups to the window when set to true" - Window creation is unaffected). Showing this
/// with <c>Show(owner)</c> makes it an OS-level owned window, which Windows guarantees renders
/// above its owner - including the owner's own native child HWNDs, such as NativeWebView's
/// Chromium host - restoring the behavior the drawers had right after the reader was first built
/// (before Win32PlatformOptions.OverlayPopups was added for an unrelated ComboBox-freeze fix and
/// silently broke the Popup-based approach). See BookReaderScreen.axaml's own airspace-problem note
/// for the full history.
/// </summary>
internal sealed class OverlayHostWindow : Window
{
    public OverlayHostWindow()
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        CanResize = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
    }
}
