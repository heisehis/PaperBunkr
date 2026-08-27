using System;
using System.Runtime.InteropServices;

namespace Paperbunkr.App.Services;

/// <summary>
/// Result of <see cref="NativeMessageBox.ShowNotResponding"/> - which native button the user clicked.
/// </summary>
internal enum NativeMessageBoxResult
{
    Wait,
    ForceExit,
}

/// <summary>
/// A raw <c>user32.dll</c> <c>MessageBoxW</c> P/Invoke shim, used only by
/// <see cref="FreezeWatchdogService"/> to notify the user when the UI thread has stopped
/// responding. This deliberately does not go through Avalonia at all: Avalonia has a single
/// Dispatcher/UI thread per process, so an Avalonia-rendered window can't be shown from the
/// watchdog thread while that same thread is stuck - a native message box, by contrast, is
/// entirely independent of Avalonia's dispatcher and works regardless of the app's state. See
/// docs/superpowers/specs/2026-08-23-app-chrome-crash-reporter-and-tray-design.md §3. Windows-only,
/// consistent with this project's "Windows-first, cross-platform later" scope
/// (docs/onboarding.md §1).
/// </summary>
internal static class NativeMessageBox
{
    private const uint MB_RETRYCANCEL = 0x00000005;
    private const uint MB_ICONWARNING = 0x00000030;
    private const uint MB_TOPMOST = 0x00040000;
    private const int IDRETRY = 4;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static NativeMessageBoxResult ShowNotResponding()
    {
        int result = MessageBoxW(
            IntPtr.Zero,
            "Paperbunkr isn't responding.\n\nClick Retry to keep waiting, or Cancel to force it to quit.",
            "Paperbunkr",
            MB_RETRYCANCEL | MB_ICONWARNING | MB_TOPMOST);

        return result == IDRETRY ? NativeMessageBoxResult.Wait : NativeMessageBoxResult.ForceExit;
    }
}
