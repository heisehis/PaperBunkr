using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.SignatureVerifiers;

namespace Paperbunkr.App.Services;

/// <summary>
/// Wraps NetSparkle's <see cref="SparkleUpdater"/> for checking, downloading, and applying updates
/// (docs/superpowers/specs/2026-09-01-auto-update-and-changelog-design.md). Reverted from an earlier
/// Velopack-based implementation: Velopack retires the existing Inno Setup installer and switches to
/// a per-user install layout, and its own Avalonia UI story turned out rough (see the design doc's
/// revision note and a real crash this class's Velopack version hit -
/// <c>Velopack.Exceptions.NotInstalledException</c> from a mis-scoped "is this a managed install"
/// check). NetSparkle is installer-agnostic - it downloads and runs whatever installer the appcast
/// points at - so Inno Setup stays exactly as it was; there is no Velopack-style "is this a real
/// managed install" concept to gate calls behind here at all.
///
/// Unlike Velopack's callback-based <c>DownloadUpdatesAsync(info, onProgress)</c>, NetSparkle reports
/// progress and completion via events (<see cref="SparkleUpdater.DownloadMadeProgress"/>/
/// <see cref="SparkleUpdater.DownloadFinished"/>/<see cref="SparkleUpdater.DownloadHadError"/>), and
/// <see cref="SparkleUpdater.InitAndBeginDownload"/>'s own <see cref="Task"/> is not guaranteed to
/// complete only once the download itself finishes (confirmed against NetSparkle's own
/// HandleEventsYourself sample, which awaits <c>InitAndBeginDownload</c> AND separately waits on
/// <c>DownloadFinished</c> before installing - not either alone). <see cref="DownloadUpdatesAsync"/>
/// below does the same: a <see cref="TaskCompletionSource{TResult}"/> completed by the finished/error
/// events, awaited after (not instead of) awaiting <c>InitAndBeginDownload</c> itself.
/// </summary>
public class UpdateService
{
    private const string AppcastUrl = "https://github.com/heisehis/PaperBunkr/releases/latest/download/appcast.xml";

    // Ed25519 public key, generated 2026-09-01 via `netsparkle-generate-appcast --generate-keys`
    // (design doc's CI section). The matching private key is NOT in this repo - it must be stored as
    // a GitHub Actions secret (NETSPARKLE_PRIVATE_KEY) for CI to sign releases with; this app only
    // ever needs the public half to verify what it downloads.
    private const string PublicKey = "e/I/0elRAtWpiqhrkwEZTr0afc7TMwb3Z+cBHeTPc3k=";

    private readonly SparkleUpdater _sparkle = new(
        AppcastUrl,
        new Ed25519Checker(SecurityMode.Strict, PublicKey))
    {
        UIFactory = null,
        UserInteractionMode = UserInteractionMode.NotSilent,
        // NetSparkle's default (true) asks the server for the destination file name. A GitHub release
        // URL redirects to a signed blob URL whose name carries no extension, so the installer landed
        // as an extensionless file and "Restart" opened the "Open with" dialog instead of running it.
        // False takes the name from the enclosure URL (PaperbunkrSetup-x.y.z.exe).
        CheckServerFileName = false,
        // Left unset, NetSparkle drops the downloaded installer straight in Path.GetTempPath() (its
        // own SparkleUpdater.cs default) - fine for the immediate-restart path, but "Later" from the
        // download-ready toast (MainViewModel.DownloadUpdateAsync) keeps a reference to that path for
        // an indefinite amount of time, and raw OS Temp is volatile - a reboot or the OS's own temp-
        // cleanup can remove the file before the user acts, leaving "Restart" to fail against a
        // missing installer. Same %APPDATA%\Paperbunkr\<subfolder> convention this app already uses
        // for backups (BackupService.DefaultBackupLocation) - persistent, not swept by Temp cleanup.
        TmpDownloadFilePath = Paperbunkr.Data.AppDataPaths.Combine("updates"),
    };

    private static void CloseApplication()
    {
        // Posted so the shutdown runs after the Restart button's click has finished routing.
        Dispatcher.UIThread.Post(() =>
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // ApplicationShutdown close reason passes through MainWindow's minimize-to-tray and
                // confirm-before-close interceptors.
                desktop.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }
        });
    }

    public Task<UpdateInfo> CheckForUpdatesAsync() => _sparkle.CheckForUpdatesQuietly();

    /// <summary>Downloads <paramref name="item"/>, reporting 0-100 progress via <paramref name="onProgress"/>, and returns the local path NetSparkle downloaded it to.</summary>
    public async Task<string> DownloadUpdatesAsync(AppCastItem item, Action<int>? onProgress = null)
    {
        var downloadCompleted = new TaskCompletionSource<string>();

        void OnProgress(object sender, AppCastItem downloadingItem, ItemDownloadProgressEventArgs e) =>
            onProgress?.Invoke(e.ProgressPercentage);
        void OnFinished(AppCastItem finishedItem, string path) => downloadCompleted.TrySetResult(path);
        void OnError(AppCastItem erroredItem, string? path, Exception exception) => downloadCompleted.TrySetException(exception);

        _sparkle.DownloadMadeProgress += OnProgress;
        _sparkle.DownloadFinished += OnFinished;
        _sparkle.DownloadHadError += OnError;
        try
        {
            await _sparkle.InitAndBeginDownload(item);
            return await downloadCompleted.Task;
        }
        finally
        {
            _sparkle.DownloadMadeProgress -= OnProgress;
            _sparkle.DownloadFinished -= OnFinished;
            _sparkle.DownloadHadError -= OnError;
        }
    }

    /// <summary>
    /// Verifies and starts the downloaded installer, then closes Paperbunkr. Returns null once the
    /// installer is running, or why it couldn't start (Paperbunkr then stays open).
    ///
    /// Deliberately not <see cref="SparkleUpdater.InstallUpdate"/>: that closes the app first and
    /// runs the installer from a hidden cmd script afterwards. The installer needs admin rights
    /// (Installer.iss PrivilegesRequired=admin), and Windows shows a UAC request from a windowless
    /// background process only as a flashing taskbar button, not a prompt. The
    /// script also gives up silently if this process takes over 90s to exit. Starting it here,
    /// while Paperbunkr still owns the foreground, puts the UAC prompt in front; the installer's
    /// InitializeSetup then waits for this process's AppMutex to clear before touching files.
    /// </summary>
    public string? ApplyUpdatesAndRestart(AppCastItem item, string downloadPath)
    {
        if (!File.Exists(downloadPath))
        {
            return "The downloaded installer is missing. Check for updates again to re-download it.";
        }

        ValidationResult validation;
        try
        {
            validation = _sparkle.SignatureVerifier.VerifySignatureOfFile(item.DownloadSignature ?? string.Empty, downloadPath);
        }
        catch (Exception)
        {
            // A malformed signature throws (e.g. wrong length) rather than returning Invalid.
            validation = ValidationResult.Invalid;
        }

        if (validation != ValidationResult.Valid)
        {
            return "The downloaded installer failed its signature check, so it wasn't run.";
        }

        try
        {
            // /PAPERBUNKRUPDATE=1 is read by Installer.iss's InitializeSetup (the mutex wait).
            Process.Start(new ProcessStartInfo(downloadPath, "/PAPERBUNKRUPDATE=1") { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            // Includes ERROR_CANCELLED when the user declines the UAC prompt.
            return $"The installer didn't start: {ex.Message}";
        }

        CloseApplication();
        return null;
    }
}
