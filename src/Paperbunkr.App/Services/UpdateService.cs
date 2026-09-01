using System;
using System.Threading.Tasks;
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
    };

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

    public void ApplyUpdatesAndRestart(AppCastItem item, string downloadPath) => _sparkle.InstallUpdate(item, downloadPath);
}
