using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace Paperbunkr.App.Services;

/// <summary>
/// Moves a file to the Windows Recycle Bin rather than permanently deleting it (docs/superpowers/
/// specs/2026-08-22-delete-functionality-design.md) - confirmed with the user: deleting a Series/
/// Issue/Book from the library also removes its physical file, but recoverably, matching CE's own
/// "remove from library" flow (which offers recycle-bin removal as an option, never a silent
/// permanent delete). Uses <c>Microsoft.VisualBasic.FileIO.FileSystem</c>, the standard .NET way to
/// reach the real OS recycle bin without a WinForms/WPF dependency - ships in the base
/// Microsoft.NETCore.App shared framework, confirmed present rather than assumed.
/// </summary>
public static class RecycleBinHelper
{
    /// <summary>Best-effort - a missing file, permission error, or unsupported OS is swallowed (the library-database removal this accompanies must never be blocked by a filesystem hiccup).</summary>
    public static void SendToRecycleBin(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        catch (System.Exception ex) when (ex is IOException or System.UnauthorizedAccessException or System.PlatformNotSupportedException)
        {
        }
    }
}
