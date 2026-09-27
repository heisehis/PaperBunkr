using System;
using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Paperbunkr.App.Services;

/// <summary>
/// Opens web links in the default browser, the same way the app's other "open" buttons do (<c>UseShellExecute</c>). Only http(s)
/// addresses are opened (<see cref="LinkTargetResolver"/>), so text in a changelog or document can never launch a local program.
/// </summary>
public static class ExternalLinks
{
    /// <summary>For link hosts with no document viewer to hand a bundled-document link to (the changelog): opens web links only.</summary>
    public static ICommand OpenWebCommand { get; } = new RelayCommand<string?>(target => TryOpenWeb(target));

    public static bool TryOpenWeb(string? target)
    {
        if (LinkTargetResolver.Resolve(target) != LinkTargetKind.Web)
        {
            return false;
        }

        return TryShellOpen(target!.Trim());
    }

    /// <summary>Opens a URL or folder with the shell. Failures (no browser, no file manager) are swallowed: there is nothing more to do.</summary>
    public static bool TryShellOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
