using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Paperbunkr.App.Services;

/// <summary>
/// The <see cref="BootstrapDecision.GiveUp"/> notice - shown when even a software-rendering retry
/// crashed during bootstrap, so Avalonia cannot be trusted to render anything
/// (docs/superpowers/specs/2026-09-10-bootstrap-crash-sentinel-safe-mode-design.md §5.5). Uses the
/// Win32 message box directly for that reason; nothing here touches Avalonia.
/// </summary>
internal static class SafeModeNotice
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x0;
    private const uint MB_ICONERROR = 0x10;
    private const uint MB_SYSTEMMODAL = 0x1000;

    public static void ShowGiveUpMessageBox(string logDirectory)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logDirectory}\"")
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // Opening the folder is a nicety, not a requirement.
        }

        try
        {
            MessageBoxW(
                IntPtr.Zero,
                "Paperbunkr couldn't start, even with hardware acceleration turned off." + Environment.NewLine + Environment.NewLine +
                "Its diagnostic logs have been opened for you:" + Environment.NewLine + logDirectory + Environment.NewLine + Environment.NewLine +
                "Updating your graphics driver often fixes this. If it keeps happening, reinstall Paperbunkr or report it at" + Environment.NewLine +
                "github.com/heisehis/PaperBunkr/issues",
                "Paperbunkr",
                MB_OK | MB_ICONERROR | MB_SYSTEMMODAL);
        }
        catch
        {
            // If even MessageBox fails there is nothing left to do.
        }
    }
}
